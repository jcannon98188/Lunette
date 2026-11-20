---
kind: ticket
title: "Perf: reproducible Linux macro-benchmark baseline vs PUC and profile"
status: open
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Baseline (WSL Ubuntu, same machine, median of 3, Lunette includes ~90 ms .NET startup; ratio Lunette/PUC 5.4.9): binary-trees 990/443 ms (2.2x), nsieve 2038/706 (2.9x), string-heavy 410/74 (5.5x), k-nucleotide 672/119 (5.6x), n-body 1961/342 (5.7x), fib 692/113 (6.1x), spectral-norm 806/130 (6.2x), fannkuch 896/123 (7.3x), mandelbrot 869/99 (8.8x). Goal of the perf effort (tickets 40-45): close these gaps (target: every macro benchmark under 3x PUC net of startup; stretch under 2x) without breaking ADRs, analyzers, alloc/JIT budgets or the suite.

This ticket: (1) commit a node-free runner usable in WSL/Linux (`bench/run-macro-linux.sh` or make run-macro.mjs documented for WSL where node exists) that reports Lunette vs PUC with startup measured separately and subtracted (add `--startup` baseline using a `print(1)` script); (2) record the baseline in bench/README.md; (3) profile each macro benchmark (dotnet-trace cpu-sampling, JIT disasm of the RunLoop per README runbook; note ReadyToRun/TieredPGO/TieredCompilation settings and test whether `TieredPGO`, `TieredCompilationQuickJitForLoops`/OSR or ReadyToRun of the core assembly change startup/steady-state — the CLI runtimeconfig is a legitimate lever) and write findings to `bench/PROFILE.md`: top hot opcodes/paths per benchmark and a ranked list of causes. Do not change the engine here except for runtime config knobs justified by measurement. Subsequent tickets 41-44 are driven by these findings; adjust their scope by appending to them.
