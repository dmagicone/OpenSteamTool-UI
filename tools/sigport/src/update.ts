// End-to-end update against a Smokestack checkout:
//
//   <data>/pattern/                 (pattern branch)
//     builds.json                   ledger of every client build seen
//     steamclient/<sha256>.toml
//     steamui/<sha256>.toml
//   <data>/ipc/                     (ipc branch)
//     steamclient/<sha256>.toml
//
// For each tracked manifest (stable, beta) we fetch the current client
// package; any component whose files don't exist yet is ported from the most
// recent earlier build that has them.

import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { formatIpcToml, formatPatternToml, readIpcToml, readPatternToml } from "./formats";
import { portIpc } from "./ipc";
import { hex, PEImage } from "./pe";
import { needsReview, portPatterns, type PortResult } from "./port";
import {
  BINS_PACKAGE,
  COMPONENT_DLLS,
  fetchClientManifest,
  loadBinaries,
  type ClientBinaries,
  type Component,
  type PackageRef,
} from "./steamcdn";

export interface BuildRecord {
  version: string;
  manifests: string[];
  package: PackageRef;
  sha256: Record<Component, string>;
  firstSeen: string;
}

interface Ledger {
  builds: BuildRecord[];
}

export interface UpdateOptions {
  data: string;
  cache: string;
  manifests: string[];
  log?: (line: string) => void;
}

export interface ComponentReport {
  version: string;
  component: Component;
  sha256: string;
  base?: string;
  patterns: PortResult[];
  ipcIssues: string[];
  error?: string;
}

export interface UpdateSummary {
  changed: boolean;
  needsReview: boolean;
  reports: ComponentReport[];
  newVersions: string[];
}

const ledgerPath = (data: string) => join(data, "pattern", "builds.json");
const patternPath = (data: string, c: Component, sha: string) => join(data, "pattern", c, `${sha}.toml`);
const ipcPath = (data: string, sha: string) => join(data, "ipc", "steamclient", `${sha}.toml`);

function readLedger(data: string): Ledger {
  const p = ledgerPath(data);
  return existsSync(p) ? (JSON.parse(readFileSync(p, "utf8")) as Ledger) : { builds: [] };
}

function writeFile(path: string, text: string) {
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, text.endsWith("\n") ? text : text + "\n");
}

const byVersionDesc = (a: BuildRecord, b: BuildRecord) => Number(b.version) - Number(a.version);

export async function update(opts: UpdateOptions): Promise<UpdateSummary> {
  const log = opts.log ?? (() => {});
  const ledger = readLedger(opts.data);
  const summary: UpdateSummary = { changed: false, needsReview: false, reports: [], newVersions: [] };
  const binCache = new Map<string, Promise<ClientBinaries>>();
  const binaries = (rec: Pick<BuildRecord, "version" | "package">, manifest: string) => {
    let p = binCache.get(rec.package.sha2);
    if (!p) binCache.set(rec.package.sha2, (p = loadBinaries(manifest, rec.version, rec.package, opts.cache)));
    return p;
  };

  for (const name of opts.manifests) {
    const manifest = await fetchClientManifest(name);
    const pkg = manifest.packages[BINS_PACKAGE];
    if (!pkg) throw new Error(`${name}: no ${BINS_PACKAGE} package in manifest ${manifest.version}`);
    log(`${name}: version ${manifest.version}, ${pkg.file}`);

    let rec = ledger.builds.find((b) => b.package.sha2 === pkg.sha2);
    const complete = (r: BuildRecord) =>
      existsSync(patternPath(opts.data, "steamclient", r.sha256.steamclient)) &&
      existsSync(patternPath(opts.data, "steamui", r.sha256.steamui)) &&
      existsSync(ipcPath(opts.data, r.sha256.steamclient));
    if (rec && complete(rec)) {
      // Known build with every file published: no download needed.
      if (!rec.manifests.includes(name)) {
        rec.manifests.push(name);
        summary.changed = true;
      }
      continue;
    }

    const bins = await binaries({ version: manifest.version, package: pkg }, name);
    if (!rec) {
      rec = { version: manifest.version, manifests: [name], package: pkg, sha256: bins.sha256, firstSeen: new Date().toISOString() };
      ledger.builds.push(rec);
      summary.newVersions.push(rec.version);
      summary.changed = true;
    } else if (!rec.manifests.includes(name)) {
      rec.manifests.push(name);
      summary.changed = true;
    }

    for (const component of Object.keys(COMPONENT_DLLS) as Component[]) {
      const sha = rec.sha256[component];
      const wantIpc = component === "steamclient";
      const havePattern = existsSync(patternPath(opts.data, component, sha));
      const haveIpc = !wantIpc || existsSync(ipcPath(opts.data, sha));
      if (havePattern && haveIpc) continue;

      const report: ComponentReport = { version: rec.version, component, sha256: sha, patterns: [], ipcIssues: [] };
      summary.reports.push(report);

      const base = [...ledger.builds]
        .sort(byVersionDesc)
        .find((b) => b !== rec && b.sha256[component] !== sha && existsSync(patternPath(opts.data, component, b.sha256[component])));
      if (!base) {
        report.error = "no earlier build with signature files to port from";
        summary.needsReview = true;
        continue;
      }
      report.base = base.version;
      log(`  ${component}: porting ${base.version} -> ${rec.version}`);

      const oldBins = await binaries(base, base.manifests[0] ?? name);
      const oldImg = new PEImage(oldBins.dlls[component]);
      const newImg = new PEImage(bins.dlls[component]);
      const header = [
        `Generated by sigport for Steam client ${rec.version} (${component} sha256 ${sha})`,
        `Ported from client ${base.version}. Comments are ignored by the loader.`,
      ];

      if (!havePattern) {
        const oldEntries = readPatternToml(patternPath(opts.data, component, base.sha256[component]));
        report.patterns = portPatterns(oldImg, newImg, oldEntries);
        const entries = report.patterns.flatMap((r) => (r.entry ? [r.entry] : []));
        writeFile(patternPath(opts.data, component, sha), formatPatternToml(entries, header));
        summary.changed = true;
        if (report.patterns.some((r) => needsReview(r.confidence))) summary.needsReview = true;
      }

      if (!haveIpc) {
        const basePath = ipcPath(opts.data, base.sha256.steamclient);
        if (!existsSync(basePath)) {
          report.ipcIssues.push(`no IPC spec for base build ${base.version}`);
          summary.needsReview = true;
        } else {
          const res = portIpc(newImg, readIpcToml(basePath));
          report.ipcIssues = res.issues;
          writeFile(ipcPath(opts.data, sha), formatIpcToml(res.ifaces, header));
          summary.changed = true;
          if (res.issues.length) summary.needsReview = true;
        }
      }
    }
  }

  if (summary.changed) writeFile(ledgerPath(opts.data), JSON.stringify({ builds: ledger.builds.sort(byVersionDesc) }, null, 2));
  return summary;
}

export function renderReport(summary: UpdateSummary): string {
  const lines: string[] = [];
  for (const r of summary.reports) {
    lines.push(`### ${r.component} @ client ${r.version}`, "", `sha256 \`${r.sha256}\`${r.base ? `, ported from ${r.base}` : ""}`, "");
    if (r.error) lines.push(`**Error:** ${r.error}`, "");
    if (r.patterns.length) {
      lines.push("| function | confidence | old rva | new rva | sig | detail |", "|---|---|---|---|---|---|");
      for (const p of r.patterns) {
        const flag = needsReview(p.confidence) ? " ⚠️" : "";
        lines.push(
          `| ${p.old.name} | ${p.confidence}${flag} | ${hex(p.old.rva)} | ${p.entry ? hex(p.entry.rva) : "-"} | ${p.sigChanged ? "new" : "kept"} | ${p.detail} |`,
        );
      }
      lines.push("");
    }
    if (r.ipcIssues.length) lines.push("IPC:", ...r.ipcIssues.map((i) => `- ${i}`), "");
  }
  return lines.join("\n");
}
