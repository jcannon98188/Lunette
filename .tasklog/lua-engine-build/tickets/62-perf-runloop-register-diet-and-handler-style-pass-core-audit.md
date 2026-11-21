---
kind: ticket
title: "Perf: RunLoop register diet and handler style pass (core audit 2+3)"
status: open
blocked_by: [61]
tags: [perf, ready-for-agent]
created: 2026-11-21
updated: 2026-11-21
---

## Question

Implement candidates 2 and 3 from the core perf audit (scratchpad audit-core.md; prototype p13 diffs). Keep one frame base ref `rb`; delete the r, bs, cl, proto locals in RunLoop; handlers use one ref plus constant offsets with nothing live across a cold call; merge Ticks/GcTick poll and self-jump ForLoop only if measured to help. Biggest lever per audit (fannkuch -28%, spectral-norm -26%, n-body -24%, mandelbrot -20%). Layout-sensitive: re-measure all 13 macros, read disassembly of touched handlers, keep RunLoop fast/traced twins consistent. Full suite, differential, safe twin, budgets, analyzers, suppression ratchet green.
