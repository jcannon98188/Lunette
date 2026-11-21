---
kind: ticket
title: "Perf: frozen type tags and barrier-free scalar stores (core audit 1)"
status: open
tags: [perf, ready-for-agent]
created: 2026-11-21
updated: 2026-11-21
---

## Question

Implement candidate 1 from the core perf audit: C:\Users\Jason\AppData\Local\Temp\claude\0ef875f1ff5d9149\cbe55384-ee0b-4cfc-a3e4-0be94a5c7495\scratchpad\audit-core.md (prototype diffs proto-*.diff in the same folder). Make the four type tags string literals so the JIT treats them as constants and drops write barriers on tag stores; rewrite Regs.SetInteger/SetFloat/Copy (and RegsSafe twin) to write payload first; add a source-typed Regs.Store for table slots and upvalues. MUST also fix src/Lunette/Lib/CoroutineLib.cs:100 (`res[0].Ref is string` is never true since Lua strings are byte[]; coroutine.wrap string-error position rule; add a test, compare against PUC) because string-literal tags would make it wrongly fire for numbers. Verify under Native AOT too, not only JIT. Re-measure all 13 macros vs PUC (layout sensitivity: read disassembly of touched handlers); full suite, differential, safe twin (-p:LuaSafeAccess=true), budgets, analyzers and suppression ratchet must stay green.
