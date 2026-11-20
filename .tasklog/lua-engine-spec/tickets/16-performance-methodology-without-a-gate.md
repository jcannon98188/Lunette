---
kind: ticket
title: Performance methodology without a gate
status: resolved
type: grilling
created: 2026-11-20
updated: 2026-11-20T03:15:38Z
---

## Question

With no numeric pass/fail performance gate, how is performance still steered: benchmark suite choice (classic Lua benchmarks, suite-derived, microbenchmarks per opcode/stdlib), harness (BenchmarkDotNet or custom), baselines against reference PUC Lua 5.4 and prior-art .NET engines, profiling tooling (EventPipe, allocation tracking, JIT disasm), how results are recorded and trended (non-blocking regressions report), and how benchmark findings feed back into design decisions?

## Answer

Performance is steered by measurement and reporting, never by a pass/fail gate.

- **Suites:** two layers. Macro: classic whole-program Lua benchmarks (fib, binary-trees, n-body, spectral-norm, fannkuch, mandelbrot, nsieve, k-nucleotide, string-heavy concat/gsub/format workloads), vendored under `bench/macro/` with each script's license recorded. Micro: per-opcode and per-stdlib-family microbenchmarks (table get/set, calls, metamethods, coroutine switch, string ops). The official test-suite files are not used as benchmarks.
- **Harness:** both. BenchmarkDotNet for in-process micro-benchmarks (warmup/tiering handling, MemoryDiagnoser, disassembly). A custom CLI runner for macro benchmarks, running whole processes through the `lua`-compatible CLI with the same command line for this engine and for PUC, measuring wall time including startup.
- **Baselines:** PUC Lua 5.4.9 (the binary CI already builds for differential testing) is the only primary baseline; results are reported as a ratio to PUC on the same machine in the same run. Prior-art .NET engines (e.g. Lua-CSharp, MoonSharp) are optional informational columns in a periodic report, never in routine runs.
- **Profiling toolkit (a runbook, not a mandate):** dotnet-trace/EventPipe sampling for CPU; BenchmarkDotNet MemoryDiagnoser plus EventPipe GC allocation-tick events for allocations; DOTNET_JitDisasm / JitStdOutFile for JIT output of the interpreter loop, reusing the MinOpts detection from the JIT ticket. Zero-allocation hot-path assertions belong to the code-discipline ticket (17), not here.
- **Recording and trending:** report-only, never failing. A scheduled (nightly or weekly) CI job runs macro and micro suites on a pinned runner class, writes JSON with ratio-to-PUC as the headline number, publishes it as CI artifacts plus an append-only history file on a dedicated `perf-history` branch, and posts a non-blocking summary flagging any benchmark more than 10% worse than its trailing median. Pull requests may request an on-demand comparison run, which also never blocks.
- **Feedback into design:** a light rule. A change justified as a performance win cites a before/after benchmark in its description; a perf-motivated discipline rule (ticket 17) names the benchmark it protects; a benchmark far out of line with PUC (about >3x slower) opens a triage item, not a build failure; a design-level finding (e.g. a representation change) gets a recorded decision in the spec's decision log rather than a silent patch.
