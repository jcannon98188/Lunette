---
kind: ticket
title: Close allocation and JIT-budget gaps
status: open
blocked_by: [10]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
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
