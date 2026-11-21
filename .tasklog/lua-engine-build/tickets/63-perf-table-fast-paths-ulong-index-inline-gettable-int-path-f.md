---
kind: ticket
title: "Perf: table fast paths (ulong index, inline GetTable int path, first string probe) (core audit 4)"
status: open
blocked_by: [62]
tags: [perf, ready-for-agent]
created: 2026-11-21
updated: 2026-11-21
---

## Question

Implement candidate 4 from the core perf audit (scratchpad audit-core.md). Fix the ulong array-index check (5 compares per access), inline the integer path in GetTable, inline the first string-key probe. Measured: t.k 8.5→4.7 ns, t[3]=a 10.8→7.0 ns. Re-measure all 13 macros; full suite, differential, safe twin, budgets, analyzers, suppression ratchet green.
