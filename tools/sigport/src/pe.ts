// Minimal PE32+ reader: section table, RVA <-> file offset mapping, and the
// handful of lookups the porting code needs. Works on the on-disk file; the
// runtime scans the mapped image, which has the same section contents.

import { readFileSync } from "node:fs";

export interface Section {
  name: string;
  va: number;
  vsize: number;
  raw: number;
  rawSize: number;
  exec: boolean;
}

const IMAGE_SCN_MEM_EXECUTE = 0x20000000;

export class PEImage {
  readonly buf: Buffer;
  readonly imageBase: bigint;
  readonly sections: Section[];

  constructor(buf: Buffer) {
    if (buf.readUInt16LE(0) !== 0x5a4d) throw new Error("not a PE file (missing MZ)");
    const pe = buf.readUInt32LE(0x3c);
    if (buf.readUInt32LE(pe) !== 0x4550) throw new Error("not a PE file (missing PE signature)");
    const optOff = pe + 24;
    if (buf.readUInt16LE(optOff) !== 0x20b) throw new Error("only PE32+ (x64) images are supported");

    const count = buf.readUInt16LE(pe + 6);
    const optSize = buf.readUInt16LE(pe + 20);
    this.buf = buf;
    this.imageBase = buf.readBigUInt64LE(optOff + 24);
    this.sections = [];
    for (let i = 0; i < count; i++) {
      const o = optOff + optSize + i * 40;
      this.sections.push({
        name: buf.toString("latin1", o, o + 8).replace(/\0+$/, ""),
        vsize: buf.readUInt32LE(o + 8),
        va: buf.readUInt32LE(o + 12),
        rawSize: buf.readUInt32LE(o + 16),
        raw: buf.readUInt32LE(o + 20),
        exec: (buf.readUInt32LE(o + 36) & IMAGE_SCN_MEM_EXECUTE) !== 0,
      });
    }
    this.sections.sort((a, b) => a.va - b.va);
  }

  static load(path: string): PEImage {
    return new PEImage(readFileSync(path));
  }

  sectionForRva(rva: number): Section | undefined {
    return this.sections.find((s) => rva >= s.va && rva < s.va + Math.min(s.vsize || s.rawSize, s.rawSize));
  }

  sectionForOffset(off: number): Section | undefined {
    return this.sections.find((s) => off >= s.raw && off < s.raw + s.rawSize);
  }

  /** File offset of an RVA, or -1 when the RVA has no file backing. */
  rvaToOffset(rva: number): number {
    const s = this.sectionForRva(rva);
    return s ? rva - s.va + s.raw : -1;
  }

  /** RVA of a file offset, or -1 when the offset is outside every section. */
  offsetToRva(off: number): number {
    const s = this.sectionForOffset(off);
    return s ? off - s.raw + s.va : -1;
  }

  isExecRva(rva: number): boolean {
    return this.sectionForRva(rva)?.exec ?? false;
  }

  bytesAt(rva: number, len: number): Buffer {
    const off = this.rvaToOffset(rva);
    if (off < 0) throw new Error(`RVA 0x${rva.toString(16)} is not file-backed`);
    return this.buf.subarray(off, off + len);
  }

  readU64Rva(rva: number): bigint {
    const off = this.rvaToOffset(rva);
    return off < 0 ? -1n : this.buf.readBigUInt64LE(off);
  }

  /** Printable ASCII C string at an RVA in a non-executable section, if any. */
  readCString(rva: number, min = 4, max = 256): string | null {
    const s = this.sectionForRva(rva);
    if (!s || s.exec) return null;
    const start = rva - s.va + s.raw;
    let end = start;
    while (end < this.buf.length && end - start < max && this.buf[end]! >= 0x20 && this.buf[end]! < 0x7f) end++;
    if (this.buf[end] !== 0 || end - start < min) return null;
    return this.buf.toString("latin1", start, end);
  }

  /** Walks back from an executable RVA to the byte after the preceding int3 padding. */
  functionStartBefore(rva: number, maxBack = 0x4000): number {
    let off = this.rvaToOffset(rva);
    const floor = Math.max(0, off - maxBack);
    while (off > floor && !(this.buf[off - 1] === 0xcc && this.buf[off - 2] === 0xcc)) off--;
    return this.offsetToRva(off);
  }

  /** All file offsets of `needle`, restricted by the section predicate. */
  findAll(needle: Uint8Array, where: (s: Section) => boolean, limit = Infinity): number[] {
    const out: number[] = [];
    for (const s of this.sections) {
      if (!where(s)) continue;
      const end = s.raw + s.rawSize;
      for (let p = this.buf.indexOf(needle, s.raw); p >= 0 && p + needle.length <= end; p = this.buf.indexOf(needle, p + 1)) {
        out.push(p - s.raw + s.va);
        if (out.length >= limit) return out;
      }
    }
    return out;
  }
}

export const hex = (n: number | bigint) => "0x" + n.toString(16).toUpperCase();
