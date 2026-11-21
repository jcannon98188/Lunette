---
kind: ticket
title: "Perf: load() compile is quadratic in constants (lazy hash index for constants and names)"
status: resolved
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-21T02:07:59Z
---

## Question

Can LuaCompiler.NumberConst/StringConst/AddName stop scanning linearly (lazy hash index, allocated only past a small threshold) so a 5.8 MB data-table chunk compiles in about 0.2 s instead of 80 s, without slowing tiny loads or degrading under LUNETTE_CONSTANT_HASH=1? Also make a long load() respect the instruction budget/interrupt if feasible. Source: audit-libs.md candidate 1.

## Answer

Lazy per-function constant/name hash index (>32 entries) in LuaCompiler, output pinned identical by SHA corpus tests; load() compiles now metered (CompileTick every 256 tokens: budget/memory/interrupt). 60000-row data table load ~80s → 285ms (PUC 164ms); 8000-function chunk 676→151ms. Caveat: big legitimate load() now spends instruction budget.
