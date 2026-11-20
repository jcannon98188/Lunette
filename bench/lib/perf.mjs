// Pure helpers for the report-only benchmark pipeline. No benchmark ever fails a build: flags are advisory.

export const REGRESSION_THRESHOLD = 0.10; // flag a ratio more than 10% worse than its trailing median
export const TRAILING_WINDOW = 7; // history entries considered for the trailing median
export const TRIAGE_RATIO = 3; // a benchmark about >3x slower than PUC suggests a triage item (never a failure)

export function median(values) {
  const xs = values.filter((v) => Number.isFinite(v)).sort((a, b) => a - b);
  if (xs.length === 0) return NaN;
  const mid = xs.length >> 1;
  return xs.length % 2 ? xs[mid] : (xs[mid - 1] + xs[mid]) / 2;
}

/** engine time divided by PUC time on the same machine in the same run; above 1 means slower than PUC. */
export function ratio(engineMs, referenceMs) {
  if (!(engineMs > 0) || !(referenceMs > 0)) return NaN;
  return engineMs / referenceMs;
}

/**
 * Compares the current rows against the trailing median of earlier history records of the same kind.
 * Rows are { name, ratio }; history records are { kind, results: [{ name, ratio }] }, oldest first.
 * Returns [{ name, ratio, median, worse }] for every benchmark whose ratio is worse than the median by more than the threshold.
 */
export function flagRegressions(kind, rows, history, { threshold = REGRESSION_THRESHOLD, window = TRAILING_WINDOW } = {}) {
  const earlier = history.filter((r) => r.kind === kind).slice(-window);
  const flags = [];
  for (const row of rows) {
    if (!Number.isFinite(row.ratio)) continue;
    const past = earlier.flatMap((r) => r.results.filter((x) => x.name === row.name).map((x) => x.ratio));
    const base = median(past);
    if (!Number.isFinite(base)) continue;
    const worse = row.ratio / base - 1;
    if (worse > threshold) flags.push({ name: row.name, ratio: row.ratio, median: base, worse });
  }
  return flags;
}

/** Benchmarks far out of line with PUC; these open a triage item, they never fail anything. */
export function triageCandidates(rows, limit = TRIAGE_RATIO) {
  return rows.filter((r) => Number.isFinite(r.ratio) && r.ratio > limit).map((r) => r.name);
}

export function parseHistory(text) {
  return text
    .split("\n")
    .map((l) => l.trim())
    .filter(Boolean)
    .map((l) => JSON.parse(l));
}

export function serializeRecord(record) {
  return JSON.stringify(record) + "\n";
}

const fmt = (x) => (Number.isFinite(x) ? x.toFixed(2) : "n/a");

/** Markdown summary for the CI job summary. Report-only: never mentions a failure. */
export function renderSummary(records, flagsByKind) {
  const out = ["## Lunette benchmarks (report only)", ""];
  for (const rec of records) {
    out.push(`### ${rec.kind}`, "", "| benchmark | Lunette ms | PUC 5.4.9 ms | ratio (Lunette / PUC) |", "|---|---:|---:|---:|");
    for (const r of rec.results) out.push(`| ${r.name} | ${fmt(r.engineMs)} | ${fmt(r.referenceMs)} | ${fmt(r.ratio)} |`);
    out.push("");
    const flags = flagsByKind[rec.kind] ?? [];
    if (flags.length > 0) {
      out.push(`Flags (more than ${REGRESSION_THRESHOLD * 100}% worse than trailing median; non-blocking):`, "");
      for (const f of flags) out.push(`- ${f.name}: ratio ${fmt(f.ratio)} vs median ${fmt(f.median)} (+${(f.worse * 100).toFixed(0)}%)`);
      out.push("");
    }
    const triage = triageCandidates(rec.results);
    if (triage.length > 0) out.push(`Over ${TRIAGE_RATIO}x slower than PUC (consider a triage item): ${triage.join(", ")}`, "");
  }
  return out.join("\n");
}

/**
 * Joins a BenchmarkDotNet JSON export with PUC per-snippet times.
 * bdn: parsed *-report-full-compressed.json (or -report.json); puc: { name: ms }.
 * BenchmarkDotNet benchmark methods are named <Method>(Snippet: "<name>"); the snippet name is extracted from Parameters.
 */
export function joinMicro(bdn, puc) {
  const rows = [];
  for (const b of bdn.Benchmarks ?? []) {
    const m = /Snippet=([^\s,]+)/.exec(b.Parameters ?? "") ?? /Snippet: "?([^"\s,)]+)/.exec(b.FullName ?? "");
    if (!m) continue;
    const name = m[1];
    const engineMs = b.Statistics?.Mean / 1e6; // nanoseconds
    const referenceMs = puc[name];
    rows.push({
      name,
      engineMs,
      referenceMs,
      ratio: ratio(engineMs, referenceMs),
      allocatedBytes: b.Memory?.BytesAllocatedPerOperation,
    });
  }
  return rows.sort((a, b) => a.name.localeCompare(b.name));
}
