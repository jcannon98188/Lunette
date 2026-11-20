---
kind: ticket
title: CI shards, timeouts and unrun-CI verification
status: resolved
blocked_by: [10, 17, 18]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:53:16Z
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

Additional gaps from ticket 17: add a `CODEOWNERS` (or equivalent) so changes to `tests/differential/waivers.toml`, `tests/suite/waivers.toml` and `tests/suite/required-files.txt` need review; make `nightly-differential.yml` open or update a tracking issue on failure instead of only uploading failing programs; add a timing check against the ~15 minute PR-workflow budget; make `ci/build-reference.sh` robust to CRLF checkouts (it currently breaks under bash with CRLF line endings; add `.gitattributes` `eol=lf` for `*.sh`).

## Answer

4-shard suite job, timeouts, ci-gate needs check (ci/ci-lib.mjs), PR time-budget job, nightly failure issue, CODEOWNERS (owner handle taken from origin remote; confirm). actionlint clean, scripts parse. No GitHub Actions job has ever run; ci/build-reference.sh and run-aot-subset.mjs only syntax-checked in CI form (reference build verified via WSL).
