import test from "node:test";
import assert from "node:assert/strict";
import { median, ratio, flagRegressions, triageCandidates, parseHistory, serializeRecord, renderSummary, joinMicro } from "./perf.mjs";

test("median handles odd, even and empty input", () => {
  assert.equal(median([3, 1, 2]), 2);
  assert.equal(median([4, 1, 2, 3]), 2.5);
  assert.ok(Number.isNaN(median([])));
});

test("ratio is engine over reference and NaN for unusable times", () => {
  assert.equal(ratio(30, 10), 3);
  assert.ok(Number.isNaN(ratio(0, 10)));
  assert.ok(Number.isNaN(ratio(10, undefined)));
});

const rec = (kind, ...pairs) => ({ kind, results: pairs.map(([name, r]) => ({ name, ratio: r })) });

test("flagRegressions flags only ratios more than 10% worse than the trailing median", () => {
  const history = [rec("macro", ["fib", 2.0], ["nbody", 1.0]), rec("macro", ["fib", 2.0], ["nbody", 1.0]), rec("macro", ["fib", 2.0], ["nbody", 1.0])];
  const rows = [{ name: "fib", ratio: 2.3 }, { name: "nbody", ratio: 1.09 }, { name: "new", ratio: 9 }];
  const flags = flagRegressions("macro", rows, history);
  assert.deepEqual(flags.map((f) => f.name), ["fib"]);
  assert.ok(Math.abs(flags[0].worse - 0.15) < 1e-9);
});

test("flagRegressions ignores other kinds, uses only the trailing window, and improvements never flag", () => {
  const history = [rec("macro", ["fib", 10]), rec("micro", ["fib", 1]), rec("macro", ["fib", 2]), rec("macro", ["fib", 2])];
  assert.equal(flagRegressions("macro", [{ name: "fib", ratio: 2.1 }], history, { window: 2 }).length, 0);
  assert.equal(flagRegressions("macro", [{ name: "fib", ratio: 1.0 }], history).length, 0);
});

test("triageCandidates lists benchmarks more than 3x slower than PUC", () => {
  assert.deepEqual(triageCandidates([{ name: "a", ratio: 3.5 }, { name: "b", ratio: 2.9 }, { name: "c", ratio: NaN }]), ["a"]);
});

test("history round-trips as JSON lines", () => {
  const a = rec("macro", ["fib", 1]);
  const b = rec("micro", ["x", 2]);
  assert.deepEqual(parseHistory(serializeRecord(a) + serializeRecord(b)), [a, b]);
  assert.deepEqual(parseHistory(""), []);
});

test("renderSummary shows ratios, flags and triage candidates without failure wording", () => {
  const record = { kind: "macro", results: [{ name: "fib", engineMs: 400, referenceMs: 100, ratio: 4 }] };
  const md = renderSummary([record], { macro: [{ name: "fib", ratio: 4, median: 3, worse: 0.33 }] });
  assert.match(md, /\| fib \| 400.00 \| 100.00 \| 4.00 \|/);
  assert.match(md, /non-blocking/);
  assert.match(md, /Over 3x slower/);
});

test("joinMicro pairs BenchmarkDotNet means (ns) with PUC times (ms)", () => {
  const bdn = { Benchmarks: [{ Parameters: "Snippet=call-lua", Statistics: { Mean: 5e6 }, Memory: { BytesAllocatedPerOperation: 24 } }, { Parameters: "Snippet=other", Statistics: { Mean: 1e6 } }] };
  const rows = joinMicro(bdn, { "call-lua": 2.5 });
  assert.equal(rows[0].name, "call-lua");
  assert.equal(rows[0].engineMs, 5);
  assert.equal(rows[0].ratio, 2);
  assert.equal(rows[0].allocatedBytes, 24);
  assert.ok(Number.isNaN(rows[1].ratio));
});
