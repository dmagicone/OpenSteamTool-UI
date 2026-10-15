// Reader/writer for the two TOML schemas the runtime consumes:
//   pattern/<component>/<sha256>.toml -> PatternLoader (keys = FNV-1a of name)
//   ipc/steamclient/<sha256>.toml     -> IPCLoader (interface tables + methods)
// The writer reproduces the upstream steam-monitor layout so files stay
// diffable against older ones.

import { readFileSync } from "node:fs";

export interface PatternEntry {
  key: number;
  name: string;
  rva: number;
  sig: string;
  /** Written as a comment; ignored by the runtime. */
  note?: string;
}

export interface IpcMethod {
  name: string;
  methodIndex: number;
  funcHash: number;
  wrapperRva: number;
  fencepost: number;
  argc: number;
}

export interface IpcInterface {
  name: string;
  interfaceId: number;
  vtableRva: number;
  methods: IpcMethod[];
}

const h32 = (n: number) => "0x" + (n >>> 0).toString(16).toUpperCase().padStart(8, "0");
const hx = (n: number) => "0x" + n.toString(16).toUpperCase();
const num = (v: unknown, what: string): number => {
  if (typeof v === "number") return v;
  if (typeof v === "string" && /^0x[0-9a-f]+$/i.test(v)) return parseInt(v, 16);
  throw new Error(`expected hex string or number for ${what}, got ${JSON.stringify(v)}`);
};

export function fnv1a(name: string): number {
  let h = 0x811c9dc5;
  for (const c of Buffer.from(name, "utf8")) {
    h ^= c;
    h = Math.imul(h, 0x01000193) >>> 0;
  }
  return h;
}

export function parsePatternToml(text: string): PatternEntry[] {
  const doc = Bun.TOML.parse(text) as Record<string, Record<string, unknown>>;
  const out: PatternEntry[] = [];
  for (const [rawKey, v] of Object.entries(doc)) {
    if (typeof v !== "object" || v === null || !/^0x[0-9a-f]{1,8}$/i.test(rawKey)) continue;
    out.push({
      key: parseInt(rawKey, 16),
      name: String(v.name ?? ""),
      rva: v.rva === undefined ? 0 : num(v.rva, `${rawKey}.rva`),
      sig: String(v.sig ?? ""),
    });
  }
  return out;
}

export function formatPatternToml(entries: PatternEntry[], header: string[] = []): string {
  const lines = header.map((l) => `# ${l}`);
  if (lines.length) lines.push("");
  for (const e of [...entries].sort((a, b) => a.name.localeCompare(b.name))) {
    if (e.note) lines.push(`# ${e.note}`);
    lines.push(`[${h32(e.key)}]`, `name = "${e.name}"`, `rva = "${hx(e.rva)}"`, `sig = "${e.sig}"`, "");
  }
  return lines.join("\n");
}

export function readPatternToml(path: string): PatternEntry[] {
  return parsePatternToml(readFileSync(path, "utf8"));
}

export function parseIpcToml(text: string): IpcInterface[] {
  const doc = Bun.TOML.parse(text) as Record<string, Record<string, unknown>>;
  const out: IpcInterface[] = [];
  for (const [name, tbl] of Object.entries(doc)) {
    if (typeof tbl !== "object" || tbl === null) continue;
    const iface: IpcInterface = {
      name,
      interfaceId: Number(tbl.interface_id ?? -1),
      vtableRva: tbl.vtable_rva === undefined ? 0 : num(tbl.vtable_rva, `${name}.vtable_rva`),
      methods: [],
    };
    for (const [mname, m] of Object.entries(tbl)) {
      if (typeof m !== "object" || m === null) continue;
      const mt = m as Record<string, unknown>;
      iface.methods.push({
        name: mname,
        methodIndex: Number(mt.method_index),
        funcHash: num(mt.funcHash, `${name}.${mname}.funcHash`),
        wrapperRva: mt.wrapper_rva === undefined ? 0 : num(mt.wrapper_rva, `${name}.${mname}.wrapper_rva`),
        fencepost: num(mt.fencepost, `${name}.${mname}.fencepost`),
        argc: Number(mt.argc ?? 0),
      });
    }
    out.push(iface);
  }
  return out;
}

export function formatIpcToml(ifaces: IpcInterface[], header: string[] = []): string {
  const lines = header.map((l) => `# ${l}`);
  if (lines.length) lines.push("");
  for (const i of ifaces) {
    lines.push(`[${i.name}]`, `interface_id = ${i.interfaceId}`, `vtable_rva = "${hx(i.vtableRva)}"`, "");
    for (const m of i.methods) {
      lines.push(
        `[${i.name}.${m.name}]`,
        `method_index = ${m.methodIndex}`,
        `funcHash = "${h32(m.funcHash)}"`,
        `wrapper_rva = "${hx(m.wrapperRva)}"`,
        `fencepost = "${h32(m.fencepost)}"`,
        `argc = ${m.argc}`,
        "",
      );
    }
  }
  return lines.join("\n");
}

export function readIpcToml(path: string): IpcInterface[] {
  return parseIpcToml(readFileSync(path, "utf8"));
}
