---
kind: ticket
title: "Perf: smaller RunLoop frame, cached closure state, builtin call protocol"
status: open
blocked_by: [43]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Follow-up from ticket 42 (fib 2.9x, spectral-norm 3.1x after it; empty Lua call 27 ns vs PUC 10; builtin call 26 ns over loop vs ~13). Findings: the JIT keeps `cl`, `proto`, `code`, `k`, `bs` in stack slots in a 0x958-byte frame RunLoop; every Call/Return pair pays ~8 reloads in `newframe` plus ~8 Frame stores; native-to-Lua callbacks pay ~2.3 KB prologue zeroing. Work: (1) shrink the RunLoop frame (split cold cases out of line, fewer live locals, SkipLocalsInit, move rarely used state into a struct/ref), re-measure JIT disasm; (2) cached upvalue slots / struct-of-arrays frame stack if it pays; (3) builtin protocol: let hot builtins write results straight onto the stack (signature change across libs — start with the hottest: math.*, string.sub/byte/len, select, ipairs/pairs/next, type, tostring) instead of span-in / LuaValue[]-out + Rets; (4) the remaining 41 gaps: `x * -2` negative-literal operands, fused compares through `and`/`or`, left-constant compares, inline int fast path for IDiv/Mod, mixed int/float arithmetic. Keep suite, differential, analyzers, alloc/JIT budgets, ADRs 0001-0006 and the LUA_SAFE_ACCESS twin (ticket 46: add a safe counterpart for any new Lunette.Unsafe helper) green; measure with bench/run-macro-linux.sh interleaved vs the base commit, keep only wins. Runs after ticket 43 (shared Interpreter.cs). Target: every macro benchmark under 3x PUC net of startup, ideally under 2.5x.
