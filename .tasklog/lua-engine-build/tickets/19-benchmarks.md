---
kind: ticket
title: Benchmarks
status: open
blocked_by: [10]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

### What to build

A classic macro benchmark runner (custom CLI) plus BenchmarkDotNet per-opcode and stdlib micro-benchmarks, reported as a ratio to PUC 5.4.9, on scheduled report-only runs with a `perf-history` branch and non-blocking 10% flags.

### Acceptance criteria

- [ ] Scheduled run publishes macro and micro results as a ratio to the Reference binary
- [ ] Results are appended to `perf-history`
- [ ] Benchmarks never block `ci-gate`
