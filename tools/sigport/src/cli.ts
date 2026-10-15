#!/usr/bin/env bun
// sigport — keeps OpenSteamTool's per-build signature files current.
//
//   update        --data DIR [--cache DIR] [--manifest NAME]... [--report FILE] [--summary FILE]
//   port-pattern  --old-dll F --old-toml F --new-dll F [--out F]
//   port-ipc      --new-dll F --old-toml F [--out F]
//   verify        --dll F --toml F
//   fetch         [--manifest NAME] [--cache DIR] --out DIR

import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { parseArgs } from "node:util";
import { formatIpcToml, formatPatternToml, readIpcToml, readPatternToml } from "./formats";
import { portIpc } from "./ipc";
import { hex, PEImage } from "./pe";
import { needsReview, portPatterns } from "./port";
import { parseSig, scanImage } from "./sig";
import { COMPONENT_DLLS, fetchClientBinaries, fetchClientManifest, type Component } from "./steamcdn";
import { renderReport, update } from "./update";

const DEFAULT_MANIFESTS = ["steam_client_win64", "steam_client_publicbeta_win64"];
const DEFAULT_CACHE = ".cache";

const [command, ...rest] = process.argv.slice(2);
const { values: args } = parseArgs({
  args: rest,
  options: {
    data: { type: "string" },
    cache: { type: "string", default: DEFAULT_CACHE },
    manifest: { type: "string", multiple: true },
    report: { type: "string" },
    summary: { type: "string" },
    "old-dll": { type: "string" },
    "new-dll": { type: "string" },
    "old-toml": { type: "string" },
    dll: { type: "string" },
    toml: { type: "string" },
    out: { type: "string" },
  },
});

function need(name: keyof typeof args): string {
  const v = args[name];
  if (typeof v !== "string" || !v) {
    console.error(`missing --${name}`);
    process.exit(64);
  }
  return v;
}

function emit(text: string) {
  if (args.out) writeFileSync(args.out, text + "\n");
  else console.log(text);
}

switch (command) {
  case "update": {
    const summary = await update({
      data: need("data"),
      cache: args.cache!,
      manifests: args.manifest?.length ? args.manifest : DEFAULT_MANIFESTS,
      log: (l) => console.error(l),
    });
    const report = renderReport(summary);
    if (args.report) writeFileSync(args.report, report + "\n");
    if (args.summary) {
      const { changed, needsReview: review, newVersions } = summary;
      writeFileSync(args.summary, JSON.stringify({ changed, needsReview: review, newVersions }, null, 2) + "\n");
    }
    console.log(report || "Nothing new.");
    process.exit(summary.needsReview ? 2 : 0);
  }

  case "port-pattern": {
    const results = portPatterns(PEImage.load(need("old-dll")), PEImage.load(need("new-dll")), readPatternToml(need("old-toml")));
    for (const r of results) {
      console.error(`${r.old.name.padEnd(32)} ${r.confidence.padEnd(8)} ${hex(r.old.rva)} -> ${r.entry ? hex(r.entry.rva) : "-"}  ${r.detail}`);
    }
    emit(formatPatternToml(results.flatMap((r) => (r.entry ? [r.entry] : []))));
    process.exit(results.some((r) => needsReview(r.confidence)) ? 2 : 0);
  }

  case "port-ipc": {
    const res = portIpc(PEImage.load(need("new-dll")), readIpcToml(need("old-toml")));
    for (const i of res.issues) console.error(`issue: ${i}`);
    emit(formatIpcToml(res.ifaces));
    process.exit(res.issues.length ? 2 : 0);
  }

  case "verify": {
    // Checks each entry the way the runtime resolves it: rva first, sig as fallback.
    const img = PEImage.load(need("dll"));
    let bad = 0;
    for (const e of readPatternToml(need("toml"))) {
      const hits = e.sig ? scanImage(img, parseSig(e.sig), 64) : [];
      const ok = hits[0] === e.rva;
      if (!ok) bad++;
      console.log(`${ok ? "ok  " : "BAD "} ${e.name.padEnd(32)} rva=${hex(e.rva)} sigHits=${hits.length} first=${hits[0] !== undefined ? hex(hits[0]) : "-"}`);
    }
    process.exit(bad ? 1 : 0);
  }

  case "fetch": {
    const manifest = await fetchClientManifest(args.manifest?.[0] ?? DEFAULT_MANIFESTS[0]!);
    const bins = await fetchClientBinaries(manifest, args.cache!);
    const out = need("out");
    mkdirSync(out, { recursive: true });
    for (const [c, file] of Object.entries(COMPONENT_DLLS) as [Component, string][]) {
      writeFileSync(join(out, file), bins.dlls[c]);
      console.log(`${file}  ${bins.sha256[c]}`);
    }
    console.log(`client ${bins.version} (${bins.package.file})`);
    break;
  }

  default:
    console.error("usage: sigport <update|port-pattern|port-ipc|verify|fetch> [options]  (see header of src/cli.ts)");
    process.exit(64);
}
