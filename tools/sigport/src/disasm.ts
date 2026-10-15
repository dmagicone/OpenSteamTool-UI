// Function-level view of x64 code built on iced-x86. Each instruction carries
// two masks so the same decode serves both comparison and signature-making:
//
//   relocMask  - hides only what moves when code is relinked: RIP-relative
//                displacements and rel32 branch/call targets. Two builds of an
//                unchanged function are byte-equal under this mask.
//   robustMask - additionally hides 32-bit memory displacements (struct field
//                offsets, frame-pointer setup) and the imm32 of `sub/add rsp`
//                (stack frame size). These change between Steam builds even when
//                the code is otherwise identical, so signatures built from this
//                mask tend to survive the next update.

import { Decoder, DecoderOptions, FlowControl, Mnemonic, Register } from "iced-x86";
import type { PEImage } from "./pe";
import type { Sig } from "./sig";

export interface Insn {
  rva: number;
  bytes: Uint8Array;
  relocMask: Uint8Array;
  robustMask: Uint8Array;
  mnemonic: Mnemonic;
  /** Target of a RIP-relative memory operand, when present. */
  ipRelTarget?: number;
}

export interface Func {
  rva: number;
  size: number;
  insns: Insn[];
}

const MAX_FUNC_BYTES = 0x4000;

export function decodeFunction(img: PEImage, rva: number, maxBytes = MAX_FUNC_BYTES): Func {
  const off = img.rvaToOffset(rva);
  if (off < 0) throw new Error(`RVA 0x${rva.toString(16)} is not file-backed`);
  const sec = img.sectionForRva(rva)!;
  const limit = Math.min(maxBytes, sec.raw + sec.rawSize - off);
  const data = img.buf.subarray(off, off + limit);

  const decoder = new Decoder(64, data, DecoderOptions.None);
  decoder.ip = BigInt(rva);
  const insns: Insn[] = [];
  // Furthest in-function branch target seen so far; a ret/jmp before it is not the end.
  let furthest = rva;

  try {
    while (decoder.canDecode) {
      const pos = decoder.position;
      const instr = decoder.decode();
      try {
        if (instr.isInvalid) break;
        const at = rva + pos;
        if (instr.mnemonic === Mnemonic.Int3 && at >= furthest) break;

        const len = instr.length;
        const bytes = data.subarray(pos, pos + len);
        const relocMask = new Uint8Array(len).fill(1);
        const robustMask = new Uint8Array(len).fill(1);
        const co = decoder.getConstantOffsets(instr);
        try {
          const ipRel = instr.isIpRelMemoryOperand;
          if (co.hasDisplacement) {
            if (ipRel) {
              relocMask.fill(0, co.displacementOffset, co.displacementOffset + co.displacementSize);
              robustMask.fill(0, co.displacementOffset, co.displacementOffset + co.displacementSize);
            } else if (co.displacementSize >= 4) {
              robustMask.fill(0, co.displacementOffset, co.displacementOffset + co.displacementSize);
            }
          }
          const fc = instr.flowControl;
          const isBranch =
            fc === FlowControl.Call || fc === FlowControl.UnconditionalBranch || fc === FlowControl.ConditionalBranch;
          if (co.hasImmediate) {
            const immEnd = co.immediateOffset + co.immediateSize;
            if (isBranch && co.immediateSize >= 4) {
              relocMask.fill(0, co.immediateOffset, immEnd);
              robustMask.fill(0, co.immediateOffset, immEnd);
            } else if (
              (instr.mnemonic === Mnemonic.Sub || instr.mnemonic === Mnemonic.Add) &&
              instr.op0Register === Register.RSP &&
              co.immediateSize >= 4
            ) {
              robustMask.fill(0, co.immediateOffset, immEnd);
            }
          }
          if (isBranch && fc !== FlowControl.Call) {
            const target = Number(instr.nearBranchTarget);
            if (target > at && target < rva + limit && target > furthest) furthest = target;
          }

          insns.push({
            rva: at,
            bytes,
            relocMask,
            robustMask,
            mnemonic: instr.mnemonic,
            ipRelTarget: ipRel ? Number(instr.ipRelMemoryAddress) : undefined,
          });

          const end = at + len;
          const terminal = fc === FlowControl.Return || fc === FlowControl.UnconditionalBranch || fc === FlowControl.IndirectBranch;
          if (terminal && end >= furthest && data[pos + len] === 0xcc) break;
        } finally {
          co.free();
        }
      } finally {
        instr.free();
      }
    }
  } finally {
    decoder.free();
  }

  const last = insns.at(-1);
  return { rva, size: last ? last.rva + last.bytes.length - rva : 0, insns };
}

/** Byte string under a mask, with wildcards as 0 — for equality checks. */
function masked(fn: Func, which: "relocMask" | "robustMask"): Buffer {
  const parts: number[] = [];
  for (const i of fn.insns) for (let k = 0; k < i.bytes.length; k++) parts.push(i[which][k] ? i.bytes[k]! : 0);
  return Buffer.from(parts);
}

/** Same code modulo relinking (moved callees/globals). */
export function sameCode(a: Func, b: Func): boolean {
  return a.size === b.size && masked(a, "relocMask").equals(masked(b, "relocMask"));
}

/** Same instruction sequence; only constants (offsets, frame sizes, immediates) differ. */
export function sameShape(a: Func, b: Func): boolean {
  if (a.insns.length !== b.insns.length) return false;
  for (let i = 0; i < a.insns.length; i++) {
    const x = a.insns[i]!;
    const y = b.insns[i]!;
    if (x.mnemonic !== y.mnemonic || x.bytes.length !== y.bytes.length) return false;
  }
  return true;
}

/** C strings referenced through RIP-relative operands, in code order. */
export function stringRefs(img: PEImage, fn: Func): string[] {
  const out: string[] = [];
  for (const i of fn.insns) {
    if (i.ipRelTarget === undefined) continue;
    const s = img.readCString(i.ipRelTarget);
    if (s !== null) out.push(s);
  }
  return out;
}

/** Signature covering the first `count` instructions of a function. */
export function sigFromInsns(fn: Func, count: number, which: "relocMask" | "robustMask"): Sig {
  const bytes: number[] = [];
  const mask: number[] = [];
  for (const i of fn.insns.slice(0, count)) {
    bytes.push(...i.bytes);
    mask.push(...i[which]);
  }
  return { bytes: Uint8Array.from(bytes), mask: Uint8Array.from(mask) };
}
