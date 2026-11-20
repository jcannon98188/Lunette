// Micro benchmark runner: BenchmarkDotNet in-process timings of every bench/micro/*.lua snippet on Lunette,
// joined with the same snippets timed inside PUC Lua 5.4.9. Report-only; exits 0 whenever the runs worked.
//
// usage: node bench/run-micro.mjs [--reference <path>] [--filter <glob>] [--out file.json]
import { spawnSync } from "node:child_process";
import { existsSync, mkdtempSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import os from "node:os";
import { joinMicro } from "./lib/perf.mjs";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const opts = {};
for (let i = 2; i < process.argv.length; i += 2) opts[process.argv[i].replace(/^--/, "")] = process.argv[i + 1];

const exe = process.platform === "win32" ? ".exe" : "";
const referencePath = opts.reference ?? join(root, ".reference", "lua" + exe);
const microDir = join(root, "bench", "micro");
const names = readdirSync(microDir).filter((f) => f.endsWith(".lua")).map((f) => f.replace(/\.lua$/, "")).sort();

const artifacts = mkdtempSync(join(os.tmpdir(), "lunette-bdn-"));
const bdn = spawnSync(
  "dotnet",
  ["run", "-c", "Release", "--project", join(root, "bench", "Lunette.Benchmarks"), "--", "--filter", opts.filter ?? "*", "--artifacts", artifacts],
  { cwd: root, stdio: ["ignore", "inherit", "inherit"] },
);
if (bdn.status !== 0) {
  console.error("BenchmarkDotNet run failed");
  process.exit(1);
}
const reportFile = readdirSync(join(artifacts, "results")).find((f) => f.endsWith("-report-full.json"));
const report = JSON.parse(readFileSync(join(artifacts, "results", reportFile), "utf8"));

const puc = {};
if (existsSync(referencePath)) {
  const r = spawnSync(referencePath, [join(root, "bench", "puc-micro-harness.lua"), microDir, ...names], { encoding: "utf8" });
  if (r.status !== 0) console.error("PUC harness failed: " + r.stderr);
  for (const line of r.stdout.split("\n").filter(Boolean)) {
    const [name, ms] = line.split("\t");
    puc[name] = Number(ms);
  }
} else {
  console.error(`no reference binary at ${referencePath}; ratios will be n/a`);
}

const results = joinMicro(report, puc);
for (const r of results) {
  console.error(`${r.name.padEnd(20)} lunette ${r.engineMs.toFixed(3).padStart(9)} ms  puc ${String(r.referenceMs?.toFixed?.(3) ?? "n/a").padStart(9)} ms  ratio ${r.ratio.toFixed(2)}  alloc ${r.allocatedBytes ?? "?"} B`);
}

const record = {
  schema: 1,
  kind: "micro",
  timestamp: new Date().toISOString(),
  commit: spawnSync("git", ["rev-parse", "HEAD"], { cwd: root, encoding: "utf8" }).stdout.trim(),
  machine: { os: `${os.platform()} ${os.release()}`, cpu: os.cpus()[0]?.model, cores: os.cpus().length },
  results,
};
if (opts.out) writeFileSync(opts.out, JSON.stringify(record, null, 2) + "\n");
else console.log(JSON.stringify(record, null, 2));
