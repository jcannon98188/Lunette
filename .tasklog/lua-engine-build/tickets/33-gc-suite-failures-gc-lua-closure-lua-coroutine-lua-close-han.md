---
kind: ticket
title: "GC suite failures: gc.lua, closure.lua, coroutine.lua close-handler frames"
status: resolved
blocked_by: [13]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:53:18Z
---

## Question

Suite failures reported by ticket 10 (run before ticket 13 lands io/os, so re-check first):
- `gc.lua` lines ~194/201: `collectgarbage "step"` and `gcinfo`/`collectgarbage("count")` semantics do not match what the test expects; the pace tests (~198-229) and string-key weak block (~466-488) may need reason-coded overlays (`gc-accounting`) only where .NET GC accounting genuinely cannot match.
- `closure.lua` times out: its allocation-only loop never triggers a collection that clears the weak table. Likely tied to when automatic collections run (ADR 0006); make automatic collection trigger on allocation pressure so the test completes.
- `coroutine.lua:238`: `debug.getinfo(2)` inside a `__close` handler during pcall unwinding should report the pcall as a C level; frames are currently popped after handlers run so it reports the Lua function.
- `gengc.lua` is waived whole (gc-accounting; generational mode behaves as incremental): reassess whether real generational behaviour is cheaply achievable, else keep the waiver with a written justification.

Acceptance criteria:
- [ ] gc.lua, closure.lua and coroutine.lua pass or carry minimal reason-coded overlays, and are in required-files.txt
- [ ] gengc.lua waiver re-justified or removed
- [ ] Tests for each engine change

## Answer

gc.lua, closure.lua, gengc.lua pass and are required; collections now run outside RunLoop with Proto.LiveRegisters, weak-table/finalizer fixes. Waivers: closure.lua:262 host-C-API (debug.upvalueid on builtin), gc.lua 559-569 gc-accounting. gc.lua timeout in full runs fixed in 39.
