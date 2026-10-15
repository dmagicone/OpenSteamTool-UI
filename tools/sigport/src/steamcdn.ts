// Steam client distribution: the public client manifest (a Valve KeyValues
// text file), the package zips it points at, and extraction of the two DLLs
// OpenSteamTool hooks. Everything is verified against the manifest's SHA-256.

import { createHash } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { inflateRawSync } from "node:zlib";

export const CDN = "https://client-update.steamstatic.com";

/** Package that carries steamclient64.dll and steamui.dll on win64. */
export const BINS_PACKAGE = "bins_win64";

export const COMPONENT_DLLS = { steamclient: "steamclient64.dll", steamui: "steamui.dll" } as const;
export type Component = keyof typeof COMPONENT_DLLS;

export interface PackageRef {
  file: string;
  sha2: string;
  size: number;
}

export interface ClientManifest {
  name: string;
  version: string;
  packages: Record<string, PackageRef>;
}

type Vdf = { [k: string]: string | Vdf };

/** Parses Valve's text KeyValues ("key" "value" / "key" { ... }). */
export function parseVdf(text: string): Vdf {
  const tokens: string[] = [];
  const re = /"((?:[^"\\]|\\.)*)"|([{}])|\/\/[^\n]*|\s+/g;
  for (let m: RegExpExecArray | null; (m = re.exec(text)); ) {
    if (m[1] !== undefined) tokens.push(m[1]);
    else if (m[2]) tokens.push(m[2]);
  }
  let i = 0;
  const block = (): Vdf => {
    const out: Vdf = {};
    while (i < tokens.length && tokens[i] !== "}") {
      const key = tokens[i++]!;
      const next = tokens[i++];
      if (next === "{") {
        out[key] = block();
        i++; // closing brace
      } else if (next !== undefined) {
        out[key] = next;
      }
    }
    return out;
  };
  return block();
}

export function parseClientManifest(name: string, text: string): ClientManifest {
  const root = parseVdf(text);
  const body = Object.values(root)[0];
  if (!body || typeof body !== "object") throw new Error(`${name}: unexpected manifest layout`);
  const version = body.version;
  if (typeof version !== "string") throw new Error(`${name}: manifest has no version`);
  const packages: Record<string, PackageRef> = {};
  for (const [k, v] of Object.entries(body)) {
    if (typeof v !== "object" || typeof v.file !== "string" || typeof v.sha2 !== "string") continue;
    packages[k] = { file: v.file, sha2: v.sha2.toLowerCase(), size: Number(v.size ?? 0) };
  }
  return { name, version, packages };
}

export async function fetchClientManifest(name: string): Promise<ClientManifest> {
  const res = await fetch(`${CDN}/${name}`);
  if (!res.ok) throw new Error(`GET ${CDN}/${name}: HTTP ${res.status}`);
  return parseClientManifest(name, await res.text());
}

export const sha256 = (data: Uint8Array) => createHash("sha256").update(data).digest("hex");

/** Downloads a package into cacheDir (once) and returns its verified bytes. */
export async function fetchPackage(pkg: PackageRef, cacheDir: string): Promise<Buffer> {
  mkdirSync(cacheDir, { recursive: true });
  const path = join(cacheDir, pkg.file);
  if (existsSync(path)) {
    const cached = readFileSync(path);
    if (sha256(cached) === pkg.sha2) return cached;
  }
  const res = await fetch(`${CDN}/${pkg.file}`);
  if (!res.ok) throw new Error(`GET ${CDN}/${pkg.file}: HTTP ${res.status}`);
  const data = Buffer.from(await res.arrayBuffer());
  const got = sha256(data);
  if (got !== pkg.sha2) throw new Error(`${pkg.file}: sha256 ${got} does not match manifest ${pkg.sha2}`);
  writeFileSync(path + ".part", data);
  renameSync(path + ".part", path);
  return data;
}

/**
 * Extracts entries by base name (case-insensitive) from a zip held in memory.
 * Supports stored and deflated entries, which is all Valve's packages use.
 */
export function extractFromZip(zip: Buffer, names: string[]): Map<string, Buffer> {
  const wanted = new Map(names.map((n) => [n.toLowerCase(), n]));
  const out = new Map<string, Buffer>();

  let eocd = -1;
  for (let p = zip.length - 22; p >= Math.max(0, zip.length - 22 - 0xffff); p--) {
    if (zip.readUInt32LE(p) === 0x06054b50) {
      eocd = p;
      break;
    }
  }
  if (eocd < 0) throw new Error("zip: end of central directory not found");
  const count = zip.readUInt16LE(eocd + 10);
  let p = zip.readUInt32LE(eocd + 16);
  if (p === 0xffffffff) throw new Error("zip: zip64 archives are not supported");

  for (let n = 0; n < count; n++) {
    if (zip.readUInt32LE(p) !== 0x02014b50) throw new Error("zip: bad central directory entry");
    const method = zip.readUInt16LE(p + 10);
    const compSize = zip.readUInt32LE(p + 20);
    const nameLen = zip.readUInt16LE(p + 28);
    const extraLen = zip.readUInt16LE(p + 30);
    const commentLen = zip.readUInt16LE(p + 32);
    const local = zip.readUInt32LE(p + 42);
    const fullName = zip.toString("utf8", p + 46, p + 46 + nameLen);
    p += 46 + nameLen + extraLen + commentLen;

    const base = fullName.split(/[\\/]/).pop()!.toLowerCase();
    const key = wanted.get(base);
    if (!key) continue;
    if (zip.readUInt32LE(local) !== 0x04034b50) throw new Error(`zip: bad local header for ${fullName}`);
    const dataStart = local + 30 + zip.readUInt16LE(local + 26) + zip.readUInt16LE(local + 28);
    const raw = zip.subarray(dataStart, dataStart + compSize);
    if (method === 0) out.set(key, Buffer.from(raw));
    else if (method === 8) out.set(key, inflateRawSync(raw));
    else throw new Error(`zip: ${fullName} uses unsupported method ${method}`);
  }
  for (const n of names) if (!out.has(n)) throw new Error(`zip: ${n} not found in package`);
  return out;
}

export interface ClientBinaries {
  version: string;
  manifest: string;
  package: PackageRef;
  dlls: Record<Component, Buffer>;
  sha256: Record<Component, string>;
}

export async function fetchClientBinaries(manifest: ClientManifest, cacheDir: string): Promise<ClientBinaries> {
  const pkg = manifest.packages[BINS_PACKAGE];
  if (!pkg) throw new Error(`${manifest.name}: no ${BINS_PACKAGE} package`);
  return loadBinaries(manifest.name, manifest.version, pkg, cacheDir);
}

export async function loadBinaries(manifest: string, version: string, pkg: PackageRef, cacheDir: string): Promise<ClientBinaries> {
  const zip = await fetchPackage(pkg, cacheDir);
  const files = extractFromZip(zip, Object.values(COMPONENT_DLLS));
  const dlls = {} as Record<Component, Buffer>;
  const sums = {} as Record<Component, string>;
  for (const [c, file] of Object.entries(COMPONENT_DLLS) as [Component, string][]) {
    dlls[c] = files.get(file)!;
    sums[c] = sha256(dlls[c]);
  }
  return { version, manifest, package: pkg, dlls, sha256: sums };
}
