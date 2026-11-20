// Ratchet guard: tests/suite/required-files.txt may only grow relative to a base ref.
// Usage: node ci/check-required-files.mjs <base-ref>   (e.g. origin/main)
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";

const file = "tests/suite/required-files.txt";
const parse = (text) =>
  new Set(text.split("\n").map((l) => l.replace(/#.*/, "").trim()).filter(Boolean));

const base = process.argv[2];
if (!base) {
  console.error("usage: node ci/check-required-files.mjs <base-ref>");
  process.exit(2);
}

let before;
try {
  before = parse(execFileSync("git", ["show", `${base}:${file}`], { encoding: "utf8" }));
} catch {
  console.log(`${file} does not exist on ${base}; nothing to ratchet against`);
  process.exit(0);
}
const after = parse(readFileSync(file, "utf8"));
const removed = [...before].filter((f) => !after.has(f));
if (removed.length > 0) {
  console.error(`required-files shrank; removed: ${removed.join(", ")}`);
  process.exit(1);
}
console.log(`required-files ok (${before.size} -> ${after.size})`);
