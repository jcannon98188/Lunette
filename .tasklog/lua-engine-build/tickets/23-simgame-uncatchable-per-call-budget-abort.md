---
kind: ticket
title: "SimGame: uncatchable per-call budget abort"
status: open
blocked_by: [12, 14]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Implement requirements of `simgame-embedding/01` (read it via get_item; it is the source of truth). Supersedes the catchable "fails with a Lua error" wording in ticket 14: instruction and memory budget aborts must be uncatchable by pcall/xpcall/coroutine.wrap/resume/__close/__gc and unwind straight to the host call. Per-host-call instruction budget plus a cumulative cap across calls; memory cap counting every allocation the state makes (tables, strings, closures, frames, stacks, userdata) as an upper bound; documented discard-after-abort with cheap, complete disposal. Must not regress hot-path allocation/JIT budgets (tickets 16/22): budget checks via the existing hook/countdown mechanism, nothing new on the fast path beyond a decrement.

Acceptance criteria:
- [ ] Budget and memory aborts cannot be caught by pcall, xpcall, coroutine.wrap/resume, __close or __gc
- [ ] Per-call and cumulative instruction budgets settable from the host API
- [ ] Memory cap counts all state-owned allocations
- [ ] State documented unusable after abort; disposal releases everything
- [ ] Tests for each path above, including aborts inside coroutines and __close handlers
- [ ] alloc-budget and jit-budget tests still pass
