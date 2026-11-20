---
kind: ticket
title: Full green and nightly variants
status: open
blocked_by: [11, 12, 13, 16, 17, 18, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

### What to build

Integrate and verify: the complete `all.lua` run green on Linux x64 with only reason-coded waivers, plus nightly arm64, preview-SDK, capacity-1 cache, constant-hash and safe-twin variant runs.

### Acceptance criteria

- [ ] `all.lua` passes on Linux x64 with only manifest waivers
- [ ] Nightly arm64, preview-SDK and variant jobs run and report
- [ ] `ci-gate` is the only required check and is green
