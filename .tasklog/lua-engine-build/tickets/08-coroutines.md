---
kind: ticket
title: Coroutines
status: open
blocked_by: [7]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

### What to build

Coroutines whose resume/yield works across metamethods, iterators and `pcall`; `sort`, `gsub` and `load` readers re-entrant but non-yieldable; nested resume with PUC limits (1M stack slots, 200 re-entrancy levels).

### Acceptance criteria

- [ ] `coroutine.lua` passes or carries reason-coded waivers
- [ ] Added to the `required-files` ratchet
