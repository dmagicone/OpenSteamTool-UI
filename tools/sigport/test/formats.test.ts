import { describe, expect, test } from "bun:test";
import { fnv1a, formatIpcToml, formatPatternToml, parseIpcToml, parsePatternToml } from "../src/formats";

describe("pattern TOML", () => {
  test("fnv1a matches the static_asserts in PatternLoader.cpp", () => {
    expect(fnv1a("BBuildAndAsyncSendFrame")).toBe(0x82428e37);
    expect(fnv1a("BuildDepotDependency")).toBe(0xc37f2d8e);
  });

  test("round-trips entries and keeps comments out of the data", () => {
    const entries = [
      { key: 0xc37f2d8e, name: "BuildDepotDependency", rva: 0x4d75c0, sig: "48 8B C4 ?? 89", note: "strings: 42/43" },
      { key: 0x82428e37, name: "BBuildAndAsyncSendFrame", rva: 0xd44d10, sig: "48 8B C4 55" },
    ];
    const text = formatPatternToml(entries, ["header line"]);
    expect(text).toContain("[0xC37F2D8E]");
    expect(text).toContain('rva = "0x4D75C0"');
    const back = parsePatternToml(text);
    expect(back.map((e) => e.name)).toEqual(["BBuildAndAsyncSendFrame", "BuildDepotDependency"]);
    expect(back.find((e) => e.key === 0xc37f2d8e)?.rva).toBe(0x4d75c0);
  });
});

describe("IPC TOML", () => {
  const sample = `
[IClientUser]
interface_id = 1
vtable_rva = "0x1329288"

[IClientUser.GetSteamID]
method_index = 10
funcHash = "0xD6FC3200"
wrapper_rva = "0x7A2300"
fencepost = "0xD7058CA5"
argc = 0
`;

  test("parses interfaces and methods", () => {
    const [iface] = parseIpcToml(sample);
    expect(iface?.name).toBe("IClientUser");
    expect(iface?.vtableRva).toBe(0x1329288);
    expect(iface?.methods[0]).toEqual({
      name: "GetSteamID",
      methodIndex: 10,
      funcHash: 0xd6fc3200,
      wrapperRva: 0x7a2300,
      fencepost: 0xd7058ca5,
      argc: 0,
    });
  });

  test("format output parses back to the same data", () => {
    const ifaces = parseIpcToml(sample);
    expect(parseIpcToml(formatIpcToml(ifaces))).toEqual(ifaces);
  });
});
