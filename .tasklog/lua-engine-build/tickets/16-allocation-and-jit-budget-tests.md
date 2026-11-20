---
kind: ticket
title: Allocation and JIT-budget tests
status: open
blocked_by: [6, 15]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

### What to build

Exact-byte allocation tests on hot paths and a per-PR JIT check holding the VM loop to 70% of the retail MinOpts limits (60000 IL bytes, 20000 IL instructions, 2000 blocks, 2000 locals, 8000 local refs), including `MinOptJitted` and inlining-cliff checks. `AggressiveOptimization` on the loop only.

### Acceptance criteria

- [ ] An allocation regression on a covered hot path fails CI
- [ ] A loop over the budget or a MinOpts downgrade fails CI
- [ ] Both checks are part of `ci-gate`
