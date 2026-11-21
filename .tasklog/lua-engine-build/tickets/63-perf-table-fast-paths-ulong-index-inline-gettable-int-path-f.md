---
kind: ticket
title: "Perf: table fast paths (ulong index, inline GetTable int path, first string probe) (core audit 4)"
status: resolved
blocked_by: [62]
tags: [perf, ready-for-agent]
created: 2026-11-21
updated: 2026-11-21T03:42:36Z
---

## Question

Implement candidate 4 from the core perf audit (scratchpad audit-core.md). Fix the ulong array-index check (5 compares per access), inline the integer path in GetTable, inline the first string-key probe. Measured: t.k 8.5→4.7 ns, t[3]=a 10.8→7.0 ns. Re-measure all 13 macros; full suite, differential, safe twin, budgets, analyzers, suppression ratchet green.

## Answer

Implemented (single-compare array index, direct GetIntRef in GetTable, inlined first-probe GetStrRef + Regs.HeaderHash) on branch worktree-agent-ae6fd86748c70114a commit 36607ee, but measured neutral: t.k ~21ns→21ns, t[3] 11.5→11.3, macros only nsieve/n-body -5-8%, others noise; RunLoop grew 14277→14542B. The audit's table-path gains were evidently delivered by tickets 61/62. NOT merged; the commit is retained in the answer for reference only.
