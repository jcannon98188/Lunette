import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { jobIds, gateProblems } from "./ci-lib.mjs";

const read = (p) => readFileSync(new URL(p, import.meta.url), "utf8");
const nightly = read("../.github/workflows/nightly-variants.yml");
const ci = read("../.github/workflows/ci.yml");

test("nightly-variants declares every variant job and a reporting job", () => {
  const ids = jobIds(nightly);
  for (const id of ["arm64", "preview-sdk", "capacity-1-cache", "constant-hash", "report"]) assert.ok(ids.includes(id), `missing job ${id}`);
});

test("nightly-variants is scheduled, never a PR trigger, so it cannot be a required check", () => {
  assert.match(nightly, /^ {2}schedule:/m);
  assert.match(nightly, /^ {2}workflow_dispatch:/m);
  assert.doesNotMatch(nightly, /^ {2}(pull_request|push):/m);
});

test("variant jobs run the whole suite under the variant", () => {
  assert.match(nightly, /LUNETTE_CONSTANT_HASH: "1"/);
  assert.match(nightly, /LUNETTE_INTERN_CAPACITY: "1"/);
  assert.match(nightly, /ubuntu-24\.04-arm/);
  assert.match(nightly, /LUA_SAFE_ACCESS/); // documented waiver (comment) for the safe-twin variant
});

test("ci.yml: ci-gate covers every job (including all-lua, which is blocking)", () => {
  assert.deepEqual(gateProblems(ci), []);
  assert.match(ci, /needs: \[[^\]]*\ball-lua\b[^\]]*\]/);
  assert.doesNotMatch(ci, /until then it runs and reports/);
});

test("ci.yml keeps the explicit waivers for LUA_SAFE_ACCESS and multi-name constant folding", () => {
  assert.match(ci, /LUA_SAFE_ACCESS scheduled variant[^\n]*waived/);
  assert.match(ci, /multi-name constant folding[^\n]*waived/);
});
