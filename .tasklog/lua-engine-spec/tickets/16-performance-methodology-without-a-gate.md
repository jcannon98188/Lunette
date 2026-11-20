---
kind: ticket
title: Performance methodology without a gate
status: open
type: grilling
created: 2026-11-20
updated: 2026-11-20
---

## Question

With no numeric pass/fail performance gate, how is performance still steered: benchmark suite choice (classic Lua benchmarks, suite-derived, microbenchmarks per opcode/stdlib), harness (BenchmarkDotNet or custom), baselines against reference PUC Lua 5.4 and prior-art .NET engines, profiling tooling (EventPipe, allocation tracking, JIT disasm), how results are recorded and trended (non-blocking regressions report), and how benchmark findings feed back into design decisions?
