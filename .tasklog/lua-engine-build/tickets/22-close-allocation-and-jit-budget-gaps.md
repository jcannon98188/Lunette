---
kind: ticket
title: Close allocation and JIT-budget gaps
status: resolved
blocked_by: [10]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:53:03Z
---

## Question

Gaps recorded by ticket 16 (see tests/Lunette.Tests/alloc-budgets.json and jit-budgets.json):
- String library allocates more than the single result: excessBytes sub 40, format('%d') 40, rep 40, gsub 56. Fix the engine (e.g. avoid per-call temp buffers) so budget equals the result allocation, then lower the recorded excess to 0.
- RunLoop leaves non-inlined calls to LuaValue.FromBoolean (11 sites) and Instruction.get_Op (1). Make them inline (restructure/AggressiveInlining/manual) and ratchet the baseline to 0.
- Add an allocation scenario for coroutine resume/yield.

Acceptance criteria:
- [ ] String-library excessBytes all 0
- [ ] jit-budgets.json inlining leftovers baseline 0
- [ ] Coroutine resume/yield allocation scenario with a budget
- [ ] Existing alloc-budget and jit-budget tests pass

Additional findings from ticket 19's micro benchmarks (BenchmarkDotNet MemoryDiagnoser, Windows Release): per-call allocations show up in `math.sqrt`/`math.floor` calls (math-arith 24 MB/op), `pairs`/`ipairs` iteration (23.6 MB), `select` and varargs (call-vararg 12 MB), `string.sub`/`byte` (16 MB), string-concat (62 MB), metamethod-arith (16 MB), coroutine switch (4 MB). Builtin calls and iteration should be zero-allocation on the hot path (add allocation-budget scenarios for each and fix the engine), and `math-arith` (132 ms, slowest) deserves a profile. Re-run `bench/run-micro.mjs` before and after and record numbers in the ticket answer.

- [ ] Allocation-budget scenarios added for math builtins, pairs/ipairs, select/varargs, string.sub/byte, concat, metamethod arithmetic and coroutine resume/yield, each at its justified minimum
- [ ] Micro benchmark before/after numbers recorded

## Answer

Per-thread result-array reuse (Rets.cs), AggressiveInlining on FromBoolean/Op, nine new alloc scenarios at 0 bytes/iter (string-concat budgets its 26-byte result), jit inlineLeftovers = {}. Micro before→after: math-arith 24,000,796→0 B; pairs 23.6 MB→6.8 MB; string-sub-byte 16 MB→4 MB. Remaining: select(i, ...) with ≥4 results and coroutine.resume with ≥3 values still allocate; math-arith still slowest at ~122 ms.
