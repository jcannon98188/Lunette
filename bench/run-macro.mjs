// Macro benchmark runner: runs each bench/macro/*.lua as a whole process through the lua-compatible CLI,
// once for Lunette and once for the Reference binary (PUC Lua 5.4.9) with the same command line, wall time
// including startup. Report-only: the exit code is 0 whenever the runs themselves worked.
//
// usage: node bench/run-macro.mjs [--engine "<cmd>"] [--reference <path>] [--runs N] [--only a,b] [--out file.json]
//   --engine     command prefix for Lunette, split on spaces (default: dotnet src/Lunette.Cli/bin/Release/net11.0/Lunette.Cli.dll)
//   --reference  PUC lua binary (default: .reference/lua when present; without it only Lunette is timed)
import { spawnSync } from "node:child_process";
import { existsSync, readdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import os from "node:os";
import { median, ratio } from "./lib/perf.mjs";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const opts = { runs: "5" };
for (let i = 2; i < process.argv.length; i += 2) opts[process.argv[i].replace(/^--/, "")] = process.argv[i + 1];

const exe = process.platform === "win32" ? ".exe" : "";
const engine = (opts.engine ?? `dotnet ${join(root, "src/Lunette.Cli/bin/Release/net11.0/Lunette.Cli.dll")}`).split(" ");
const referencePath = opts.reference ?? join(root, ".reference", "lua" + exe);
const reference = existsSync(referencePath) ? [referencePath] : null;
const runs = Number(opts.runs);
const only = opts.only?.split(",");

function time(cmd, script) {
  const t0 = process.hrtime.bigint();
  const r = spawnSync(cmd[0], [...cmd.slice(1), script], { encoding: "utf8", maxBuffer: 1 << 26 });
  const ms = Number(process.hrtime.bigint() - t0) / 1e6;
  return { ms, ok: r.status === 0, stdout: r.stdout, stderr: r.stderr };
}

function bench(cmd, script) {
  time(cmd, script); // warm-up: file cache, runtime start
  const samples = [];
  let last;
  for (let i = 0; i < runs; i++) {
    last = time(cmd, script);
    if (!last.ok) return { ok: false, error: last.stderr.trim().split("\n")[0], stdout: last.stdout };
    samples.push(last.ms);
  }
  return { ok: true, ms: median(samples), samples, stdout: last.stdout };
}

const dir = join(root, "bench", "macro");
const scripts = readdirSync(dir).filter((f) => f.endsWith(".lua")).sort().filter((f) => !only || only.includes(f.replace(/\.lua$/, "")));
const results = [];
for (const file of scripts) {
  const name = file.replace(/\.lua$/, "");
  const e = bench(engine, join(dir, file));
  const p = reference ? bench(reference, join(dir, file)) : null;
  const row = {
    name,
    engineMs: e.ok ? e.ms : NaN,
    referenceMs: p?.ok ? p.ms : NaN,
    ratio: e.ok && p?.ok ? ratio(e.ms, p.ms) : NaN,
    outputMatches: e.ok && p?.ok ? e.stdout === p.stdout : null,
  };
  if (!e.ok) row.engineError = e.error;
  if (p && !p.ok) row.referenceError = p.error;
  results.push(row);
  console.error(`${name.padEnd(16)} lunette ${row.engineMs.toFixed(0).padStart(7)} ms  puc ${row.referenceMs.toFixed(0).padStart(7)} ms  ratio ${row.ratio.toFixed(2)}${row.outputMatches === false ? "  (output differs)" : ""}${row.engineError ? "  ERROR " + row.engineError : ""}`);
}

const record = {
  schema: 1,
  kind: "macro",
  timestamp: new Date().toISOString(),
  commit: spawnSync("git", ["rev-parse", "HEAD"], { cwd: root, encoding: "utf8" }).stdout.trim(),
  machine: { os: `${os.platform()} ${os.release()}`, cpu: os.cpus()[0]?.model, cores: os.cpus().length },
  runs,
  results,
};
if (opts.out) writeFileSync(opts.out, JSON.stringify(record, null, 2) + "\n");
else console.log(JSON.stringify(record, null, 2));
