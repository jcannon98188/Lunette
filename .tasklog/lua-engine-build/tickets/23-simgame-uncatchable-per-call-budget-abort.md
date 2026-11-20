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

### What to build

Requirements from the SimGame embedder, which runs untrusted mod scripts and needs the Host to regain control within a bound. They supersede ticket 14's catchable "a limit breach fails with a Lua error" wording.

1. **Uncatchable abort.** An instruction-budget or memory-budget abort must not be catchable by `pcall`, `xpcall`, `coroutine.wrap`/`resume` or `__close`/`__gc` handlers; it unwinds straight to the host call that started the invocation.
2. **Per-call budget.** The instruction budget is settable per host-initiated call (one hook or event invocation), not only per state, plus a cumulative cap the host can apply across several calls (SimGame uses a per-tick cap per mod pack).
3. **Full memory count.** The memory cap counts every allocation Lunette makes for the state (tables, strings, closures, frames, stacks, userdata), as an upper bound, not tables and strings only.
4. **Discard after abort.** After any budget or memory abort the state is documented as unusable and must be discarded; the host never reuses it. The API makes the discard cheap and complete (no leaked handles or pinned memory).

Notes from ticket 14's implementation: the memory limit currently counts bytes allocated by the running thread since the top-level call began (garbage included), checked at instruction-budget poll points, so a single huge library call can overshoot it; the instruction limit counts backward jumps and Lua calls, not individual instructions; `LuaState.Dispose` does not yet run pending `__close` handlers. Must not regress hot-path allocation/JIT budgets (tickets 16/22): budget checks go through the existing poll/countdown mechanism, nothing new on the fast path beyond a decrement.

### Acceptance criteria

- [ ] Budget and memory aborts cannot be caught by pcall, xpcall, coroutine.wrap/resume, __close or __gc
- [ ] Per-call and cumulative instruction budgets settable from the host API
- [ ] Memory cap counts all state-owned allocations
- [ ] State documented unusable after abort; disposal releases everything
- [ ] Tests for each path above, including aborts inside coroutines and __close handlers
- [ ] alloc-budget and jit-budget tests still pass
