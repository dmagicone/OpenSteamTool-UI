// Ports pattern entries from an old build to a new one.
//
// For every entry we collect candidate function starts in the new image (hits
// of the old signature, then byte-window votes if needed) and grade each one
// against the old function:
//
//   exact   - identical code modulo relinking
//   shape   - identical instruction sequence; only constants moved
//             (struct offsets, frame size) — typical after a header change
//   strings - same string references (>= 60% of the old function's, >= 2)
//   sig     - old signature still matches exactly once; code changed in ways
//             we can't classify, so this is what the runtime would have done
//   votes   - located only by byte-window voting; needs a human look
//
// The accepted function then gets a signature that is unique in the new image,
// preferring the old one when it still works so files churn as little as possible.

import { decodeFunction, sameCode, sameShape, sigFromInsns, stringRefs, type Func } from "./disasm";
import type { PatternEntry } from "./formats";
import { hex, type PEImage } from "./pe";
import { concreteBytes, formatSig, parseSig, scanImage, trimTrailing, type Sig } from "./sig";

export type Confidence = "exact" | "shape" | "strings" | "sig" | "votes" | "failed";

export interface PortResult {
  old: PatternEntry;
  entry?: PatternEntry;
  confidence: Confidence;
  detail: string;
  sigChanged: boolean;
}

/** Needs a human to confirm before the result should be trusted. */
export const needsReview = (c: Confidence) => c === "votes" || c === "failed";

const RANK: Record<Confidence, number> = { exact: 0, shape: 1, strings: 2, sig: 3, votes: 4, failed: 5 };

interface Graded {
  rva: number;
  confidence: Confidence;
  detail: string;
}

function grade(oldFn: Func, oldStrings: string[], newImg: PEImage, rva: number): Graded | null {
  let fn: Func;
  try {
    fn = decodeFunction(newImg, rva);
  } catch {
    return null;
  }
  if (fn.insns.length === 0) return null;
  if (sameCode(oldFn, fn)) return { rva, confidence: "exact", detail: "identical code" };
  if (sameShape(oldFn, fn)) return { rva, confidence: "shape", detail: "same instructions, constants changed" };
  if (oldStrings.length >= 2) {
    const have = new Set(stringRefs(newImg, fn));
    const shared = oldStrings.filter((s) => have.has(s)).length;
    if (shared / oldStrings.length >= 0.6) {
      return { rva, confidence: "strings", detail: `${shared}/${oldStrings.length} string refs shared` };
    }
  }
  return null;
}

function best(a: Graded | null, b: Graded | null): Graded | null {
  if (!a) return b;
  if (!b) return a;
  const d = RANK[a.confidence] - RANK[b.confidence];
  return d < 0 || (d === 0 && a.rva <= b.rva) ? a : b;
}

/**
 * Every 16-byte window of the old function that occurs exactly once in the new
 * image votes for (hit - offset). Windows spanning relocated bytes just don't
 * match, so the vote tolerates relinking; it degrades gracefully with edits.
 */
function vote(oldImg: PEImage, oldFn: Func, newImg: PEImage): { rva: number; share: number }[] {
  const W = 16;
  const STRIDE = 2;
  const body = oldImg.bytesAt(oldFn.rva, Math.min(oldFn.size, 0x1000));
  const votes = new Map<number, number>();
  let windows = 0;
  for (let k = 0; k + W <= body.length; k += STRIDE) {
    windows++;
    const w = body.subarray(k, k + W);
    const a = newImg.buf.indexOf(w);
    if (a < 0 || newImg.buf.indexOf(w, a + 1) >= 0) continue;
    const rva = newImg.offsetToRva(a);
    if (rva < 0) continue;
    votes.set(rva - k, (votes.get(rva - k) ?? 0) + 1);
  }
  return [...votes.entries()]
    .sort((x, y) => y[1] - x[1])
    .slice(0, 5)
    .map(([rva, v]) => ({ rva, share: v / Math.max(windows, 1) }));
}

/** A vote lands near, not always on, the function start: try the plausible starts around it. */
function snapCandidates(img: PEImage, rva: number): number[] {
  const out = new Set<number>([rva]);
  let off = img.rvaToOffset(rva);
  if (off < 0) return [];
  let fwd = off;
  while (img.buf[fwd] === 0xcc && fwd - off < 64) fwd++;
  out.add(img.offsetToRva(fwd));
  out.add(img.functionStartBefore(img.offsetToRva(fwd)));
  return [...out].filter((r) => r >= 0 && img.isExecRva(r));
}

function makeSig(newImg: PEImage, fn: Func, oldSig: Sig | null): { sig: string; changed: boolean; unique: boolean } {
  if (oldSig) {
    const hits = scanImage(newImg, oldSig, 2);
    if (hits.length === 1 && hits[0] === fn.rva) return { sig: formatSig(oldSig), changed: false, unique: true };
  }
  for (const which of ["robustMask", "relocMask"] as const) {
    let bytes = 0;
    for (let n = 1; n <= fn.insns.length && bytes < 128; n++) {
      bytes += fn.insns[n - 1]!.bytes.length;
      if (bytes < 12) continue;
      const sig = trimTrailing(sigFromInsns(fn, n, which));
      if (concreteBytes(sig) < 8) continue;
      const hits = scanImage(newImg, sig, 2);
      if (hits.length === 1 && hits[0] === fn.rva) return { sig: formatSig(sig), changed: true, unique: true };
    }
  }
  // Not uniquely identifiable (e.g. identical template instantiations). The
  // rva is authoritative; keep a signature whose *first* hit is this function.
  if (oldSig && scanImage(newImg, oldSig, 1)[0] === fn.rva) return { sig: formatSig(oldSig), changed: false, unique: false };
  const sig = trimTrailing(sigFromInsns(fn, Math.min(fn.insns.length, 8), "relocMask"));
  return { sig: formatSig(sig), changed: true, unique: false };
}

export function portEntry(oldImg: PEImage, newImg: PEImage, old: PatternEntry): PortResult {
  let oldSig: Sig | null = null;
  try {
    oldSig = old.sig ? parseSig(old.sig) : null;
  } catch {
    oldSig = null;
  }
  const sigHits = oldSig ? scanImage(newImg, oldSig, 64) : [];

  if (!old.rva) {
    // Nothing to compare against; behave like the runtime would.
    if (sigHits.length === 1) {
      return { old, entry: { ...old, rva: sigHits[0]! }, confidence: "sig", detail: "no old rva; unique sig hit", sigChanged: false };
    }
    return { old, confidence: "failed", detail: `no old rva and ${sigHits.length} sig hits`, sigChanged: false };
  }

  const oldFn = decodeFunction(oldImg, old.rva);
  const oldStrings = stringRefs(oldImg, oldFn);

  let pick: Graded | null = null;
  for (const rva of sigHits) pick = best(pick, grade(oldFn, oldStrings, newImg, rva));

  // Voting is the expensive path; only needed when no sig hit could be confirmed.
  if (!pick) {
    const voted = vote(oldImg, oldFn, newImg);
    for (const v of voted) for (const c of snapCandidates(newImg, v.rva)) pick = best(pick, grade(oldFn, oldStrings, newImg, c));
    if (!pick && sigHits.length === 1) {
      pick = { rva: sigHits[0]!, confidence: "sig", detail: "unique sig hit; code changed" };
    }
    const top = voted[0];
    if (!pick && top && top.share >= 0.25) {
      const start = snapCandidates(newImg, top.rva).find((r) => newImg.bytesAt(r, 1)[0] !== 0xcc) ?? top.rva;
      pick = { rva: start, confidence: "votes", detail: `${(top.share * 100).toFixed(0)}% of byte windows agree` };
    }
  }

  if (!pick) {
    return { old, confidence: "failed", detail: `${sigHits.length} sig hits, no convincing candidate`, sigChanged: false };
  }

  const newFn = decodeFunction(newImg, pick.rva);
  const s = makeSig(newImg, newFn, oldSig);
  const detail = s.unique ? pick.detail : `${pick.detail}; sig not unique, rva authoritative`;
  return {
    old,
    entry: { key: old.key, name: old.name, rva: pick.rva, sig: s.sig, note: `${pick.confidence}: ${detail} (was ${hex(old.rva)})` },
    confidence: pick.confidence,
    detail,
    sigChanged: s.changed,
  };
}

export function portPatterns(oldImg: PEImage, newImg: PEImage, entries: PatternEntry[]): PortResult[] {
  return entries.map((e) => portEntry(oldImg, newImg, e));
}
