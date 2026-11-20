---
kind: ticket
title: "Perf: table get/set, array part and GETFIELD/SETFIELD fast paths"
status: resolved
blocked_by: [42]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T19:45:22Z
---

## Question

Target nsieve (2.9x), fannkuch, n-body (field access), binary-trees (table construction), k-nucleotide. Optimize LuaTable array-part and hash-part access: GETTABLE/SETTABLE with integer keys straight into the array part, GETFIELD/SETFIELD with interned short-string constants (precomputed hash, reference-equality probe), NEWTABLE size hints, table growth/rehash cost, metatable-absent fast checks (flags cache for absent metamethods), avoiding boxing/allocation. Respect ADR 0001/0005/0006, analyzers and alloc budgets. Use `tdd`; full suite + differential green. Report before/after via the ticket 40 runner. Runs after ticket 42 (shared Interpreter.cs).
Scope additions from ticket 40 (see `bench/PROFILE.md`): string-key hash uses a `%` division (use mask/power-of-two sizing); `pairs`/`ipairs` ~2.5x slower than PUC (next-iteration cost); `s:sub` does a string-metatable lookup on every method call (cache the string metatable's __index table / fast path for string receivers in SELF); Interpreter.cs edits from tickets 41/42 land first.
From ticket 44: k-nucleotide (3.5x) is VM/table-bound, not string-bound. `Self` on a string goes through `Meta.PrepareIndex` on every `s:sub` call (~13% of the run); builtin calls pay the `CallBuiltin`/`Rets` protocol; `Interpreter.CallValues` copies results with `.ToArray()` and re-enters RunLoop (gsub callbacks allocate per match).

## Answer

GetField/SetField/SelfK opcodes, inline table get/set, mask hashing, in-place next/ipairs/pairs (83f7748). n-body 3.2x to 2.1x, nsieve 1.87x to 1.53x, k-nucleotide 2.6x to 2.0x. Not done: generic-for fusion, GetI/SetI, CallValues ToArray.
