---
kind: ticket
title: "Code-discipline enforcement: analyzers and hot-path tests"
status: open
type: grilling
blocked_by: [14]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Which performance and discipline rules from earlier decisions (no intermediate string allocations in the string library, header-aware string access, no reflection or dynamic code in core, allocation budgets on hot paths, no exceptions on common error paths) are enforced, and by what mechanism: custom Roslyn analyzers, built-in analyzer rulesets, allocation-budget tests, or review checklist? Include the interpreter-loop JIT budget from the JIT MinOpts research (instruction/local-ref/block/locals ceilings near 70% of the JIT limits, `MinOptJitted` check, leftover-call check for the 921-locals inlining cliff, whether the loop carries `AggressiveOptimization`). For each rule, name the rule, its scope, the enforcement tool, and the failure mode.
