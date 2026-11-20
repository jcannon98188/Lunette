---
kind: ticket
title: "Perf: arithmetic, comparison and numeric-for fast paths in the VM loop"
status: open
blocked_by: [40]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Target mandelbrot (8.8x), fannkuch (7.3x), spectral-norm, n-body (see ticket 40 baseline and bench/PROFILE.md). Optimize the RunLoop's arithmetic/compare/FORLOOP/FORPREP/MOVE/LOADK/ constant-operand (ADDK etc., if present or worth adding), integer and float fast paths: type-test ordering, avoiding double dispatch through helper calls, keeping LuaValue 16-byte reads/writes minimal, avoiding bounds checks via spans/refs where analyzers (LD001-LD006) allow, ensuring hot helpers are inlined (jit-budgets.json). Use the `tdd` skill: add benchmark-guarding tests where feasible (alloc budgets must stay at 0 for hot loops); verify semantic equivalence with the full suite and differential tests (reference in WSL Ubuntu /root/lref/lua). Report before/after on the macro set via the ticket 40 runner.
