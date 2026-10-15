import { describe, expect, test } from "bun:test";
import { deflateRawSync } from "node:zlib";
import { extractFromZip, parseClientManifest, parseVdf } from "../src/steamcdn";

describe("client manifest", () => {
  const text = `"win64"
{
	"version"		"1791918634"
	// comment lines are ignored
	"bins_win64"
	{
		"file"		"bins_win64.zip.62fef8f5"
		"size"		"35474054"
		"sha2"		"ABCDEF"
	}
	"flag"		"1"
}`;

  test("parseVdf handles nesting and comments", () => {
    expect(parseVdf(text)).toEqual({
      win64: { version: "1791918634", bins_win64: { file: "bins_win64.zip.62fef8f5", size: "35474054", sha2: "ABCDEF" }, flag: "1" },
    });
  });

  test("parseClientManifest extracts version and packages", () => {
    const m = parseClientManifest("steam_client_win64", text);
    expect(m.version).toBe("1791918634");
    expect(m.packages.bins_win64).toEqual({ file: "bins_win64.zip.62fef8f5", sha2: "abcdef", size: 35474054 });
  });
});

// Builds a minimal zip with one deflated and one stored entry.
function makeZip(entries: { name: string; data: Buffer; deflate: boolean }[]): Buffer {
  const locals: Buffer[] = [];
  const centrals: Buffer[] = [];
  let offset = 0;
  for (const e of entries) {
    const body = e.deflate ? deflateRawSync(e.data) : e.data;
    const name = Buffer.from(e.name);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(e.deflate ? 8 : 0, 8);
    local.writeUInt32LE(body.length, 18);
    local.writeUInt32LE(e.data.length, 22);
    local.writeUInt16LE(name.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(e.deflate ? 8 : 0, 10);
    central.writeUInt32LE(body.length, 20);
    central.writeUInt32LE(e.data.length, 24);
    central.writeUInt16LE(name.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, name, body);
    centrals.push(central, name);
    offset += 30 + name.length + body.length;
  }
  const cd = Buffer.concat(centrals);
  const eocd = Buffer.alloc(22);
  eocd.writeUInt32LE(0x06054b50, 0);
  eocd.writeUInt16LE(entries.length, 8);
  eocd.writeUInt16LE(entries.length, 10);
  eocd.writeUInt32LE(cd.length, 12);
  eocd.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, cd, eocd]);
}

describe("zip extraction", () => {
  const a = Buffer.from("steamclient payload ".repeat(50));
  const b = Buffer.from("ui");
  const zip = makeZip([
    { name: "bin/steamclient64.dll", data: a, deflate: true },
    { name: "SteamUI.dll", data: b, deflate: false },
  ]);

  test("finds entries by case-insensitive base name", () => {
    const out = extractFromZip(zip, ["steamclient64.dll", "steamui.dll"]);
    expect(out.get("steamclient64.dll")?.equals(a)).toBe(true);
    expect(out.get("steamui.dll")?.equals(b)).toBe(true);
  });

  test("throws when a requested file is missing", () => {
    expect(() => extractFromZip(zip, ["missing.dll"])).toThrow(/missing\.dll/);
  });
});
