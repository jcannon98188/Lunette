---
kind: ticket
title: Differential testing
status: open
blocked_by: [2, 10]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

### What to build

The three-part differential comparison between the engine and the Reference binary, run in CI and wired into `ci-gate`.

### Acceptance criteria

- [ ] All three differential parts run in CI against the reference
- [ ] A behavioral difference fails `ci-gate` unless covered by a reason-coded entry
- [ ] Reference binary is built on Linux only
