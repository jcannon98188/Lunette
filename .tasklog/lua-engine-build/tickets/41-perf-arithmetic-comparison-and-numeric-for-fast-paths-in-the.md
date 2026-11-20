---
kind: ticket
title: "Perf: arithmetic, comparison and numeric-for fast paths in the VM loop"
status: resolved
blocked_by: [40]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T19:45:20Z
---

## Question

Target mandelbrot (8.8x), fannkuch (7.3x), spectral-norm, n-body (see ticket 40 baseline and bench/PROFILE.md). Optimize the RunLoop's arithmetic/compare/FORLOOP/FORPREP/MOVE/LOADK/ constant-operand (ADDK etc., if present or worth adding), integer and float fast paths: type-test ordering, avoiding double dispatch through helper calls, keeping LuaValue 16-byte reads/writes minimal, avoiding bounds checks via spans/refs where analyzers (LD001-LD006) allow, ensuring hot helpers are inlined (jit-budgets.json). Use the `tdd` skill: add benchmark-guarding tests where feasible (alloc budgets must stay at 0 for hot loops); verify semantic equivalence with the full suite and differential tests (reference in WSL Ubuntu /root/lref/lua). Report before/after on the macro set via the ticket 40 runner.
Scope additions from ticket 40 (read `bench/PROFILE.md` sections 3-5 first): (1) compiler emits no operand forms: Move/LoadK are 30-65% of dispatched instructions in n-body/mandelbrot/spectral-norm/fannkuch — add K/immediate operand forms (ADDK-style, EQK/LTI etc.), fuse compare+jump (today Lt+Test+Jmp), and avoid redundant MOVEs by targeting destination registers directly; (2) per-dispatch cost ~5 ns vs PUC 0.6-1 ns: a write-barrier call on every register store forces spills — avoid the barrier when the stored ref is unchanged/null (type-tag-unchanged trick measured 5.7→4.2 ns on Move), load type tags from constants not statics, cut the ~2.6 KB prologue zeroing in RunLoop entry (SkipLocalsInit/fewer locals); let ForLoop jump back itself. Bench target: mandelbrot 7.3x→<3x, fannkuch 6x→<3x, n-body, spectral-norm.

## Answer

Operand-form opcodes (K arithmetic, fused compare-jumps, in-place locals) with Regs/RegsSafe helpers. mandelbrot 7.3x to about 2x, nsieve 2.9x to about 1.6x. Details in bench/PROFILE.md.
