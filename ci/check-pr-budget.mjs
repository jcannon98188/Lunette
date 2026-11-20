#!/usr/bin/env node
// Fails when the PR workflow's wall-clock time exceeds the budget (spec ticket 18: about 15 minutes).
// Usage: node ci/check-pr-budget.mjs [budgetMinutes]   (needs GITHUB_REPOSITORY, GITHUB_RUN_ID, GH_TOKEN; uses `gh`)
import { execFileSync } from "node:child_process";
import { wallMinutes } from "./ci-lib.mjs";

const budget = Number(process.argv[2] ?? 15);
const out = execFileSync(
  "gh",
  ["api", "--paginate", `repos/${process.env.GITHUB_REPOSITORY}/actions/runs/${process.env.GITHUB_RUN_ID}/jobs?per_page=100`,
    "--jq", ".jobs[] | select(.name != \"time-budget\") | {started_at, completed_at}"],
  { encoding: "utf8" },
);
const jobs = out.split("\n").filter(Boolean).map((l) => JSON.parse(l));
const minutes = wallMinutes(jobs);
console.log(`PR workflow wall time: ${minutes.toFixed(1)} min (budget ${budget})`);
if (minutes > budget) {
  console.log(`::error::PR workflow took ${minutes.toFixed(1)} min, over the ${budget} min budget`);
  process.exit(1);
}
