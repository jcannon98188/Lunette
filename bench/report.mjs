// Report step: reads the macro and micro result records, compares each with the trailing median of the history file,
// writes a markdown summary (for $GITHUB_STEP_SUMMARY) and appends the records to the history file.
// Always exits 0: flags are advisory and benchmarks never block ci-gate.
//
// usage: node bench/report.mjs --history <history.jsonl> --summary <summary.md> <result.json>...
import { appendFileSync, existsSync, readFileSync, writeFileSync } from "node:fs";
import { flagRegressions, parseHistory, renderSummary, serializeRecord } from "./lib/perf.mjs";

const files = [];
const opts = {};
for (let i = 2; i < process.argv.length; i++) {
  if (process.argv[i].startsWith("--")) opts[process.argv[i].slice(2)] = process.argv[++i];
  else files.push(process.argv[i]);
}

const history = opts.history && existsSync(opts.history) ? parseHistory(readFileSync(opts.history, "utf8")) : [];
const records = files.map((f) => JSON.parse(readFileSync(f, "utf8")));
const flagsByKind = {};
for (const rec of records) flagsByKind[rec.kind] = flagRegressions(rec.kind, rec.results, history);

const summary = renderSummary(records, flagsByKind);
if (opts.summary) writeFileSync(opts.summary, summary + "\n");
else console.log(summary);
if (opts.history) for (const rec of records) appendFileSync(opts.history, serializeRecord(rec));
