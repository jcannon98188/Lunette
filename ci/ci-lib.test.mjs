import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { jobIds, jobNeeds, gateProblems, wallMinutes } from "./ci-lib.mjs";

const sample = `name: x
jobs:
  a:
    runs-on: x
  b:
    needs: [a]
  # comment
  ci-gate:
    needs: [a, b]
  time-budget:
    needs: [ci-gate]
`;

test("jobIds and jobNeeds", () => {
  assert.deepEqual(jobIds(sample), ["a", "b", "ci-gate", "time-budget"]);
  assert.deepEqual(jobNeeds(sample, "b"), ["a"]);
  assert.equal(jobNeeds(sample, "a"), null);
});

test("gate complete passes; missing and unknown fail; CRLF tolerated", () => {
  assert.deepEqual(gateProblems(sample), []);
  assert.deepEqual(gateProblems(sample.replace("[a, b]", "[a]")), ["ci-gate.needs is missing job 'b'"]);
  assert.deepEqual(gateProblems(sample.replace("[a, b]", "[a, b, zz]")), ["ci-gate.needs lists unknown job 'zz'"]);
  assert.deepEqual(gateProblems(sample.replace(/\n/g, "\r\n")), []);
});

test("wallMinutes spans earliest start to latest end", () => {
  const jobs = [
    { started_at: "2026-01-01T00:00:00Z", completed_at: "2026-01-01T00:05:00Z" },
    { started_at: "2026-01-01T00:02:00Z", completed_at: "2026-01-01T00:12:30Z" },
  ];
  assert.equal(wallMinutes(jobs), 12.5);
  assert.equal(wallMinutes([]), 0);
});

test("the real ci.yml: ci-gate aggregates every job and fails on failed/cancelled/skipped", () => {
  const yaml = readFileSync(new URL("../.github/workflows/ci.yml", import.meta.url), "utf8");
  assert.deepEqual(gateProblems(yaml), []);
  for (const r of ["failure", "cancelled", "skipped"]) assert.ok(yaml.includes(`contains(needs.*.result, '${r}')`));
});
