---
kind: ticket
title: "Perf: table get/set, array part and GETFIELD/SETFIELD fast paths"
status: open
blocked_by: [42]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Target nsieve (2.9x), fannkuch, n-body (field access), binary-trees (table construction), k-nucleotide. Optimize LuaTable array-part and hash-part access: GETTABLE/SETTABLE with integer keys straight into the array part, GETFIELD/SETFIELD with interned short-string constants (precomputed hash, reference-equality probe), NEWTABLE size hints, table growth/rehash cost, metatable-absent fast checks (flags cache for absent metamethods), avoiding boxing/allocation. Respect ADR 0001/0005/0006, analyzers and alloc budgets. Use `tdd`; full suite + differential green. Report before/after via the ticket 40 runner. Runs after ticket 42 (shared Interpreter.cs).
