---
kind: ticket
title: Benchmarks
status: resolved
blocked_by: [10]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:58Z
---

## Question

### What to build

A classic macro benchmark runner (custom CLI) plus BenchmarkDotNet per-opcode and stdlib micro-benchmarks, reported as a ratio to PUC 5.4.9, on scheduled report-only runs with a `perf-history` branch and non-blocking 10% flags.

### Acceptance criteria

- [ ] Scheduled run publishes macro and micro results as a ratio to the Reference binary
- [ ] Results are appended to `perf-history`
- [ ] Benchmarks never block `ci-gate`

## Answer

Benchmarks (macro and BenchmarkDotNet micro) and bench workflow implemented; micro findings fed ticket 22. e.g. fib 804 ms, n-body(200k) 3014 ms on the dev machine; no PUC ratios (no local reference timing).
