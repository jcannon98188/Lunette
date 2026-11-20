// Pure helpers for the CI self-checks (no YAML dependency: ci.yml uses a fixed two-space layout).

/** Top-level job ids under `jobs:` in a workflow file. */
export function jobIds(yaml) {
  const lines = yaml.replace(/\r\n/g, "\n").split("\n");
  const start = lines.findIndex((l) => /^jobs:\s*$/.test(l));
  if (start < 0) return [];
  const ids = [];
  for (const l of lines.slice(start + 1)) {
    if (/^\S/.test(l) && !l.startsWith("#")) break;
    const m = /^ {2}([A-Za-z0-9_-]+):\s*$/.exec(l);
    if (m) ids.push(m[1]);
  }
  return ids;
}

/** The `needs: [a, b]` (inline-list) of one job, or null if the job or key is missing. */
export function jobNeeds(yaml, id) {
  const lines = yaml.replace(/\r\n/g, "\n").split("\n");
  const start = lines.findIndex((l) => l === `  ${id}:`);
  if (start < 0) return null;
  for (const l of lines.slice(start + 1)) {
    if (/^ {2}\S/.test(l) && !l.startsWith("  #")) break;
    const m = /^ {4}needs:\s*\[(.*)\]\s*$/.exec(l);
    if (m) return m[1].split(",").map((s) => s.trim()).filter(Boolean);
  }
  return null;
}

/** Jobs that must be in ci-gate.needs but are not: every job except the gate itself and non-blocking jobs. */
export function gateProblems(yaml, nonBlocking = ["ci-gate", "time-budget"]) {
  const needs = jobNeeds(yaml, "ci-gate");
  if (needs === null) return ["ci-gate has no inline `needs: [...]` list"];
  const ids = jobIds(yaml);
  const problems = [];
  for (const id of ids) if (!nonBlocking.includes(id) && !needs.includes(id)) problems.push(`ci-gate.needs is missing job '${id}'`);
  for (const n of needs) if (!ids.includes(n)) problems.push(`ci-gate.needs lists unknown job '${n}'`);
  return problems;
}

/** Wall-clock minutes of a run from its jobs (earliest start to latest completion). */
export function wallMinutes(jobs) {
  const starts = jobs.map((j) => Date.parse(j.started_at)).filter(Number.isFinite);
  const ends = jobs.map((j) => Date.parse(j.completed_at)).filter(Number.isFinite);
  if (!starts.length || !ends.length) return 0;
  return (Math.max(...ends) - Math.min(...starts)) / 60000;
}
