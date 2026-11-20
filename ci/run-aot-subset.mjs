#!/usr/bin/env node
// Runs the pinned AOT subset (tests/suite/aot-subset.txt) plus ci/aot-smoke.lua through a Native AOT CLI binary.
// usage: node ci/run-aot-subset.mjs <path-to-native-cli> [--out <dir for failure logs>]
// Each file runs in its own process in a temp copy of the vendored suite (which is never edited).
import { spawnSync } from "node:child_process";
import { cpSync, mkdtempSync, readFileSync, rmSync, writeFileSync, mkdirSync, existsSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const args = process.argv.slice(2);
const bin = args[0] ? resolve(args[0]) : null;
if (!bin || !existsSync(bin)) { console.error(`native CLI not found: ${args[0]}`); process.exit(2); }
const outIdx = args.indexOf("--out");
const outDir = outIdx >= 0 ? resolve(args[outIdx + 1]) : null;

export function parseList(text) {
  return text.split(/\r?\n/).map((l) => l.trim()).filter((l) => l && !l.startsWith("#"));
}

const files = parseList(readFileSync(join(root, "tests/suite/aot-subset.txt"), "utf8"));
const temp = mkdtempSync(join(tmpdir(), "lunette-aot-"));
cpSync(join(root, "tests/lua-suite"), temp, { recursive: true });
const smoke = join(temp, "aot-smoke.lua");
cpSync(join(root, "ci/aot-smoke.lua"), smoke);

// big.lua yields 'b' and returns 'a'; upstream's all.lua drives it as a coroutine body.
const bigDriver = "local f = coroutine.wrap(assert(loadfile('big.lua'))); assert(f() == 'b'); assert(f() == 'a')";
const jobs = [
  ...files.map((f) => ({ name: f, argv: f === "big.lua" ? ["-e", bigDriver] : [f] })),
  { name: "aot-smoke.lua", argv: ["aot-smoke.lua"] },
];

let failed = 0;
for (const job of jobs) {
  const t0 = Date.now();
  const r = spawnSync(bin, job.argv, { cwd: temp, encoding: "latin1", timeout: 10 * 60 * 1000, maxBuffer: 1 << 28 });
  const secs = ((Date.now() - t0) / 1000).toFixed(1);
  const ok = r.status === 0 && !r.error;
  console.log(`${ok ? "PASS" : "FAIL"} ${job.name} (${secs}s)`);
  if (!ok) {
    failed++;
    console.log(`  exit=${r.status} signal=${r.signal ?? ""} ${r.error ?? ""}\n  ${(r.stderr ?? "").slice(0, 2000)}`);
    if (outDir) {
      mkdirSync(outDir, { recursive: true });
      writeFileSync(join(outDir, `${job.name}.stdout.txt`), r.stdout ?? "", "latin1");
      writeFileSync(join(outDir, `${job.name}.stderr.txt`), r.stderr ?? "", "latin1");
    }
  }
}
rmSync(temp, { recursive: true, force: true });
console.log(`${jobs.length - failed}/${jobs.length} passed under Native AOT`);
process.exit(failed ? 1 : 0);
