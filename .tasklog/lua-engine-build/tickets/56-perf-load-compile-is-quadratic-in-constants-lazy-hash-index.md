---
kind: ticket
title: "Perf: load() compile is quadratic in constants (lazy hash index for constants and names)"
status: open
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Can LuaCompiler.NumberConst/StringConst/AddName stop scanning linearly (lazy hash index, allocated only past a small threshold) so a 5.8 MB data-table chunk compiles in about 0.2 s instead of 80 s, without slowing tiny loads or degrading under LUNETTE_CONSTANT_HASH=1? Also make a long load() respect the instruction budget/interrupt if feasible. Source: audit-libs.md candidate 1.
