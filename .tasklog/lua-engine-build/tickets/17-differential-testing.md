---
kind: ticket
title: Differential testing
status: resolved
blocked_by: [2, 10]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:56Z
---

## Question

### What to build

The three-part differential comparison between the engine and the Reference binary, run in CI and wired into `ci-gate`.

### Acceptance criteria

- [ ] All three differential parts run in CI against the reference
- [ ] A behavioral difference fails `ci-gate` unless covered by a reason-coded entry
- [ ] Reference binary is built on Linux only

## Answer

Differential testing against reference Lua 5.4.9 (corpus, seeded generator, suite-chunk runs), nightly workflow; divergences fixed in 35, extended in 32. Runs against a WSL-built reference locally; GitHub reference build and nightly never executed on a runner.
