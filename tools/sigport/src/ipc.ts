// Ports IPC spec files. funcHash/fencepost/argc are properties of the method
// signature and survive rebuilds; what moves is where the client-side wrapper
// and its vtable live. For every method we find functions embedding its
// funcHash as an imm32, find vtable slots pointing at those functions, and
// derive candidate vtable bases (slot - index*8). The base most methods of an
// interface agree on wins; wrapper RVAs are then read straight from it.

import type { IpcInterface, IpcMethod } from "./formats";
import { hex, type PEImage } from "./pe";

export interface IpcPortResult {
  ifaces: IpcInterface[];
  issues: string[];
}

const u32le = (x: number) => {
  const b = Buffer.alloc(4);
  b.writeUInt32LE(x >>> 0);
  return b;
};
const u64le = (x: bigint) => {
  const b = Buffer.alloc(8);
  b.writeBigUInt64LE(x);
  return b;
};

function candidateBases(img: PEImage, m: IpcMethod): Set<number> {
  const bases = new Set<number>();
  const hits = img.findAll(u32le(m.funcHash), (s) => s.exec, 256);
  const fns = new Set(hits.map((h) => img.functionStartBefore(h)));
  for (const fn of fns) {
    for (const slot of img.findAll(u64le(img.imageBase + BigInt(fn)), (s) => !s.exec, 64)) {
      bases.add(slot - m.methodIndex * 8);
    }
  }
  return bases;
}

export function portIpc(newImg: PEImage, old: IpcInterface[]): IpcPortResult {
  const issues: string[] = [];
  const resolved = new Map<string, number>();
  const pending: { iface: IpcInterface; support: Map<number, number> }[] = [];

  for (const iface of old) {
    const support = new Map<number, number>();
    for (const m of iface.methods) {
      const bases = candidateBases(newImg, m);
      if (bases.size === 0) issues.push(`${iface.name}.${m.name}: funcHash ${hex(m.funcHash)} not found in any vtable'd function`);
      for (const b of bases) support.set(b, (support.get(b) ?? 0) + 1);
    }
    const ranked = [...support.entries()].sort((a, b) => b[1] - a[1]);
    const [top, second] = ranked;
    if (top && (!second || top[1] > second[1])) resolved.set(iface.name, top[0]);
    else pending.push({ iface, support });
  }

  // Ambiguous interfaces: pick the candidate closest to where the vtable
  // "should" be, given how far the resolved vtables moved.
  const shifts = old.filter((i) => resolved.has(i.name) && i.vtableRva).map((i) => resolved.get(i.name)! - i.vtableRva);
  const shift = shifts.length ? shifts.sort((a, b) => a - b)[Math.floor(shifts.length / 2)]! : 0;
  for (const { iface, support } of pending) {
    const cands = [...support.keys()];
    if (cands.length === 0 || !iface.vtableRva) {
      issues.push(`${iface.name}: vtable not found; keeping old vtable_rva ${hex(iface.vtableRva)}`);
      continue;
    }
    const expect = iface.vtableRva + shift;
    const pick = cands.sort((a, b) => Math.abs(a - expect) - Math.abs(b - expect))[0]!;
    issues.push(`${iface.name}: ambiguous vtable, chose ${hex(pick)} nearest expected ${hex(expect)}`);
    resolved.set(iface.name, pick);
  }

  const ifaces = old.map((iface) => {
    const base = resolved.get(iface.name);
    if (base === undefined) return { ...iface, methods: iface.methods.map((m) => ({ ...m })) };
    return {
      ...iface,
      vtableRva: base,
      methods: iface.methods.map((m) => {
        const ptr = newImg.readU64Rva(base + m.methodIndex * 8);
        const rva = ptr > newImg.imageBase ? Number(ptr - newImg.imageBase) : 0;
        if (!rva || !newImg.isExecRva(rva)) {
          issues.push(`${iface.name}.${m.name}: vtable slot ${m.methodIndex} does not point at code`);
          return { ...m };
        }
        return { ...m, wrapperRva: rva };
      }),
    };
  });
  return { ifaces, issues };
}
