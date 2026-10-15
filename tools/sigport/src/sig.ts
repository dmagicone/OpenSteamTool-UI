// IDA-style byte signatures ("48 8B ?? ?? C3"), matching the syntax that
// PatternLoader::ParseSig accepts at runtime.

import type { PEImage } from "./pe";

export interface Sig {
  bytes: Uint8Array;
  /** 1 = byte must match, 0 = wildcard. */
  mask: Uint8Array;
}

export function parseSig(text: string): Sig {
  const bytes: number[] = [];
  const mask: number[] = [];
  for (const tok of text.trim().split(/[\s,]+/)) {
    if (tok === "") continue;
    if (tok === "??" || tok === "?") {
      bytes.push(0);
      mask.push(0);
    } else if (/^[0-9a-fA-F]{2}$/.test(tok)) {
      bytes.push(parseInt(tok, 16));
      mask.push(1);
    } else {
      throw new Error(`malformed signature token '${tok}' in '${text}'`);
    }
  }
  if (bytes.length === 0) throw new Error("empty signature");
  return { bytes: Uint8Array.from(bytes), mask: Uint8Array.from(mask) };
}

export function formatSig(sig: Sig): string {
  return [...sig.bytes]
    .map((b, i) => (sig.mask[i] ? b.toString(16).toUpperCase().padStart(2, "0") : "??"))
    .join(" ");
}

/**
 * Drops trailing wildcards, which add nothing. Leading ones are kept: the
 * runtime resolves to the first byte of the match, so trimming them would
 * move the resolved address.
 */
export function trimTrailing(sig: Sig): Sig {
  let e = sig.bytes.length;
  while (e > 0 && !sig.mask[e - 1]) e--;
  return { bytes: sig.bytes.slice(0, e), mask: sig.mask.slice(0, e) };
}

export const concreteBytes = (sig: Sig) => sig.mask.reduce((n, m) => n + m, 0);

/** Longest run of concrete bytes; used as the indexOf anchor. */
function anchor(sig: Sig): { start: number; len: number } {
  let best = { start: 0, len: 0 };
  let runStart = -1;
  for (let i = 0; i <= sig.mask.length; i++) {
    if (i < sig.mask.length && sig.mask[i]) {
      if (runStart < 0) runStart = i;
    } else if (runStart >= 0) {
      if (i - runStart > best.len) best = { start: runStart, len: i - runStart };
      runStart = -1;
    }
  }
  return best;
}

function matchesAt(buf: Uint8Array, off: number, sig: Sig): boolean {
  for (let j = 0; j < sig.bytes.length; j++) {
    if (sig.mask[j] && buf[off + j] !== sig.bytes[j]) return false;
  }
  return true;
}

/** Offsets of every match inside buf[from, to), in ascending order. */
export function scanBuffer(buf: Buffer, sig: Sig, from = 0, to = buf.length, limit = Infinity): number[] {
  const a = anchor(sig);
  if (a.len === 0) throw new Error("signature has no concrete bytes");
  const needle = Buffer.from(sig.bytes.subarray(a.start, a.start + a.len));
  const out: number[] = [];
  for (let p = buf.indexOf(needle, from + a.start); p >= 0; p = buf.indexOf(needle, p + 1)) {
    const off = p - a.start;
    if (off + sig.bytes.length > to) break;
    if (off >= from && matchesAt(buf, off, sig)) {
      out.push(off);
      if (out.length >= limit) break;
    }
  }
  return out;
}

/**
 * RVAs of every match across all sections in address order. This mirrors
 * PatternLoader::ScanModule, which walks the whole mapped image and takes the
 * first hit, so hits[0] is what the runtime would resolve.
 */
export function scanImage(img: PEImage, sig: Sig, limit = Infinity): number[] {
  const out: number[] = [];
  for (const s of img.sections) {
    for (const off of scanBuffer(img.buf, sig, s.raw, s.raw + s.rawSize, limit - out.length)) {
      out.push(off - s.raw + s.va);
    }
    if (out.length >= limit) break;
  }
  return out;
}
