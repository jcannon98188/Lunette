---
kind: ticket
title: "Perf: warm-up and GC pause tail (first-chunk JIT cost, gen0 p99)"
status: resolved
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T20:45:15Z
---

## Question

Can the first ~5 chunks (37-57 ms vs 6 ms PUC, 6-9x) be cut via tiering/OSR settings or startup work that stays compatible with the AOT gate, and can gen0 p99 pause (4.2 ms vs 0.23 ms PUC in gc-latency.lua, suspected card-marked old-table scanning) be reduced, without hurting throughput or max pause where Lunette already wins? See bench/COVERAGE.md, bench/PROFILE.md.

## Answer

CLI now sets System.GC.Concurrent=false (and CallCountingDelayMs=0) through RuntimeHostConfigurationOption in Lunette.Cli.csproj (runtimeconfig.template.json removed) (de7fc97, merge 4cd7e40); bench/run-macro-linux.sh gained --aot. gc-latency p99 4.2 ms to 0.17-0.19 ms (PUC 0.23-0.29), p99.9 17-24 to 0.7-0.9 ms, throughput 2100-2900 to 1300-1400 ns/iter; the cost is a larger worst single pause (19-30 to 76-93 ms, PUC 124-144) and 20-45% higher peak memory on allocation-heavy scripts. Cause was concurrent-mode .NET GC, not Lunette's collection machinery. Warm-up: first 5 chunks 41-45 ms JIT, 32 ms ReadyToRun, 12.3 ms Native AOT; recommendation is to ship the CLI as Native AOT (startup 4 ms vs 98 ms; about 20% slower on metamethod-heavy oop.lua). ReadyToRun conflicts with the AOT gate and stays a publish-time choice. Rejected with measurements: TieredPGO=0, TC_QuickJitForLoops=0, OSR_HitLimit=1, TieredCompilation=0, dropping AggressiveOptimization on RunLoop, gen0/gen1/conserve-memory GC knobs, server GC, state-creation changes. Embedders must set their own runtime knobs; guidance in bench/COVERAGE.md. Differential, intern and safe-twin runs not repeated (no engine code changed).
