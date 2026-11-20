---
kind: ticket
title: CI shards, timeouts and unrun-CI verification
status: open
blocked_by: [10, 17, 18]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Known gaps from tickets 02, 15 and 16: none of the CI workflow has actually run on a runner.
- Add the 4-shard suite matrix and per-job timeouts from spec ticket 18 (suite currently runs inside the single `test` job with a 15-minute timeout).
- Lint the workflow locally (actionlint via a pinned download or equivalent) and dry-run every `ci/*.js` and `ci/*.sh` script locally where the platform allows; fix defects found.
- Confirm `ci-gate` aggregates every required job (build, test, reference, ratchet, fast-checks, alloc-budget, jit-budget, plus later aot/diff/benchmarks jobs) and fails on failed, cancelled or skipped needs.
- Document in the ticket answer which jobs could not be exercised locally and need a first real run.

Acceptance criteria:
- [ ] Suite sharded as spec'd, with timeouts
- [ ] Workflow lints clean
- [ ] ci-gate needs list equals the full job list
- [ ] Unexercised-locally jobs listed explicitly
