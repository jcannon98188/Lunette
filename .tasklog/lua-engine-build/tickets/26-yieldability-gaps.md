---
kind: ticket
title: Yieldability gaps
status: resolved
blocked_by: [10]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:53:08Z
---

## Question

Known gaps from tickets 08, 09 and 14: these paths are non-yieldable or mishandle errors across yields. Make them match PUC 5.4 behaviour.
- `__concat` runs on a nested non-yieldable call; `__pairs`, `xpcall(coroutine.yield, ...)` likewise.
- `__close` handlers run through a nested CallValues and cannot yield.
- `pcall(hostYieldingFn)` cannot yield.
- An error thrown inside a host-function continuation kills the coroutine instead of reaching a Lua `pcall` inside it.
- `__call` on userdata is unsupported.

Acceptance criteria:
- [ ] __concat, __pairs, __close handlers and xpcall(coroutine.yield) can yield inside coroutines
- [ ] pcall around a yielding host function can yield
- [ ] Errors in host continuations reach an enclosing Lua pcall in the same coroutine
- [ ] __call works on userdata
- [ ] Tests for each, including under hooks; coroutine.lua and related suite files still pass

## Answer

Yieldability gaps closed: __concat and __close run as VM frames, pcall/xpcall around host functions via trampoline, host continuations that throw, __call on userdata; coroutine.lua passes and is in required-files. Not yieldable: pcall of a standard-library function that yields (e.g. dofile) and pcall(pairs, obj) with a yielding __pairs.
