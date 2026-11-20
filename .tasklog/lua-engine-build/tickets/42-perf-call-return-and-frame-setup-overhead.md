---
kind: ticket
title: "Perf: call/return and frame setup overhead"
status: open
blocked_by: [41]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Target fib (6.1x), spectral-norm (function calls), binary-trees. Reduce per-call cost: Frame allocation/reuse (pool or struct-of-arrays on the coroutine frame stack), stack growth checks, argument/vararg copying, return value placement, CALL/RETURN/TAILCALL fast paths for Lua-to-Lua calls without leaving RunLoop, avoiding Rets/array use for Lua functions, hook/poll checks only in traced variant. Respect ADR 0002 (heap frames, no recursive .NET stack), ADR 0003 (pending-state unwinding) and the alloc budgets (calls must stay 0 B/iter). Use `tdd`; guard with alloc/JIT budget tests; full suite + differential must stay green. Report before/after via the ticket 40 runner. Runs after ticket 41 (shared Interpreter.cs).
Scope additions from ticket 40 (see `bench/PROFILE.md`): ~33 ns per Lua call vs 7 ns in PUC; builtin calls 26-45 ns more than PUC. A scalar copy instead of Array.Copy for one-result returns measured ~10% on fib. Also look at builtin-call overhead (arg array handling, Rets, CallBuiltin path) and the `gsub` callback path re-entering RunLoop.
Ticket 41 landed (macro ratios vs PUC after: mandelbrot 2.07x, nsieve 1.95x, fannkuch 2.83x, n-body 3.04x, spectral-norm 3.35x, fib 3.83x). Remaining 41 ideas not tried (yours if they help calls): tags in locals, prologue zeroing in RunLoop entry, backward-jump poll. New unsafe helpers live in `src/Lunette/Unsafe/Regs.cs`; any further unsafe access must go through that family (ticket 46 builds the LUA_SAFE_ACCESS safe twin, so keep helper signatures stable and add safe counterparts when you add helpers).
