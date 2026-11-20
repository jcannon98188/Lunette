---
kind: ticket
title: "Bench: host-to-Lua call benchmark and CallValues ToArray cost"
status: resolved
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T19:55:38Z
---

## Question

What does a host calling a Lua function repeatedly (embedder per-frame pattern, args and results via CallValues) cost versus PUC called from C, and does removing the CallValues .ToArray() allocation measurably help, within the alloc budgets?

## Answer

Added bench/Lunette.Benchmarks/HostCallBenchmarks.cs and bench/embed-puc.c (e75cf62). Host-call cost is about 2.8-3.4x PUC lua_call (41-74 ns per call vs 14-27 ns; PUC in WSL, Lunette on Windows, so indicative only). The CallValues .ToArray() costs about 9-12 ns per call and 40-72 B; no engine change, because removing it needs a new public API and the allocation-free Call(f, a, b, c, d) overloads already exist. The remaining gap is the fixed host-call entry path (CallAt, Precall, Run try, RunLoop prologue); no ticket opened for it.
