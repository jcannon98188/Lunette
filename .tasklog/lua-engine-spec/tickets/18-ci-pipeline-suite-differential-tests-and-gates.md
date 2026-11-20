---
kind: ticket
title: "CI pipeline: suite, differential tests and gates"
status: open
type: grilling
blocked_by: [14, 17]
created: 2026-11-20
updated: 2026-11-20
---

## Question

How does CI run end to end: platform/OS matrix, building the pinned reference 5.4.9, running the unmodified official suite (per-file tests plus CLI `all.lua`), the waiver-manifest and ratchet (`required-files`) checks, differential testing against the reference binary (corpus, generators, comparison rules), the Native AOT CLI subset run, the JIT budget gate, analyzer/allocation-budget tests, and non-blocking benchmark reporting? Define job structure, runtimes, caching, and what blocks a merge.
