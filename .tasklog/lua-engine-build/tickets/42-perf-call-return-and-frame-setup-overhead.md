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
