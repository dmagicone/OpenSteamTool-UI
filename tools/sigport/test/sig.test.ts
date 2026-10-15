import { describe, expect, test } from "bun:test";
import { concreteBytes, formatSig, parseSig, scanBuffer, trimTrailing } from "../src/sig";

describe("signatures", () => {
  test("parse/format round-trip keeps wildcards", () => {
    const text = "48 8B ?? ?? C3";
    const sig = parseSig(text);
    expect([...sig.mask]).toEqual([1, 1, 0, 0, 1]);
    expect(formatSig(sig)).toBe(text);
    expect(concreteBytes(sig)).toBe(3);
  });

  test("accepts commas and single '?' like the runtime parser", () => {
    expect(formatSig(parseSig("48,8B ? C3"))).toBe("48 8B ?? C3");
  });

  test("rejects malformed tokens", () => {
    expect(() => parseSig("48 8G")).toThrow();
    expect(() => parseSig("")).toThrow();
  });

  test("trimTrailing keeps leading wildcards so the match address is unchanged", () => {
    expect(formatSig(trimTrailing(parseSig("?? 48 8B ?? ??")))).toBe("?? 48 8B");
  });

  test("scanBuffer finds every match in order, honouring wildcards", () => {
    const buf = Buffer.from([0x90, 0x48, 0x8b, 0x05, 0xc3, 0x48, 0x8b, 0x07, 0xc3, 0x48, 0x8b]);
    expect(scanBuffer(buf, parseSig("48 8B ?? C3"))).toEqual([1, 5]);
    expect(scanBuffer(buf, parseSig("48 8B 07 C3"))).toEqual([5]);
    // A match that would run past the end is not reported.
    expect(scanBuffer(buf, parseSig("48 8B ?? C3"), 0, 8)).toEqual([1]);
  });
});
