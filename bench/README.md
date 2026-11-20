# Benchmarks

Report-only. Nothing here fails a build or blocks `ci-gate`; performance is steered by measurement and reporting.

- `macro/*.lua`: classic whole-program benchmarks (licenses in `macro/LICENSES.md`).
  `node bench/run-macro.mjs [--runs N] [--only fib,n-body] [--out macro.json]` runs each through the `lua`-compatible CLI for
  Lunette and for PUC Lua 5.4.9 with the same command line, wall time including startup (median of N after one warm-up).
  Needs `dotnet build -c Release src/Lunette.Cli` first, and `ci/build-reference.sh .reference` (Linux) for the PUC column.
- `run-macro-linux.sh` (bash only, no node; for Linux/WSL): same comparison with the startup cost measured separately
  (`startup.lua` = `print(1)`) and subtracted from both engines; prints wall, net and net ratio.
  `bench/run-macro-linux.sh [--runs N] [--only a,b] [--reference PATH] [--no-build] [--r2r] [--startup] [--engine-env "K=V"]`.
  Under WSL with the checkout on `/mnt/c`, set `BENCH_WORK=/root/lw` to build in a Linux copy instead of clobbering the Windows `obj/`.
  `--r2r` times a ReadyToRun publish; `--engine-env` sets runtime knobs for Lunette only (`DOTNET_TieredPGO=0`, note `DOTNET_*` values are hex).
- `profile-probes.lua`: ns-per-construct probes (Move, arithmetic, table access, calls, string ops); run under both engines.
- `PROFILE.md`: findings of the perf-baseline ticket: runtime-config measurements, per-benchmark ranked causes and the scope for the follow-up perf tickets.
- `micro/*.lua` + `Lunette.Benchmarks/`: per-opcode and per-stdlib-family snippets, one BenchmarkDotNet benchmark each
  (in-process toolchain, MemoryDiagnoser). `node bench/run-micro.mjs [--filter "*call*"] [--out micro.json]` joins the means with
  the same snippets timed inside PUC by `puc-micro-harness.lua`. Add a snippet by dropping a `.lua` file in `micro/`
  that does a fixed amount of work and returns.
- `Lunette.Benchmarks/HostCallBenchmarks.cs` + `embed-puc.c`: host-to-Lua call cost (C# `LuaState.Call` vs C `lua_call`); results in `PROFILE.md` (ticket 49).
- Headline number: ratio = Lunette time / PUC time, same machine, same run (above 1 means slower than PUC).
  Without a Reference binary (Windows, no C compiler) the runners still report absolute Lunette times.
- `report.mjs` + `.github/workflows/bench.yml`: weekly (and on-demand) run on a pinned runner class; appends JSON lines to
  `history.jsonl` on the `perf-history` branch, writes a job summary and flags any benchmark more than 10% worse than its
  trailing median (non-blocking).

## Baseline (WSL Ubuntu, 16 CPUs, median of 5, net of startup; before the perf tickets 41-44)

Startup (`print(1)`): Lunette about 90 ms (plain build) or 58 ms (ReadyToRun), PUC 5 ms. The CLI runtimeconfig sets
`System.Runtime.TieredCompilation.CallCountingDelayMs=0` (measured win, see `PROFILE.md`).

| benchmark | PUC ms | Lunette ms | ratio (net) | | benchmark | PUC ms | Lunette ms | ratio (net) |
|---|---:|---:|---:|---|---|---:|---:|---:|
| binary-trees | 445 | 847 | 1.7x | | n-body | 341 | 1854 | 5.3x |
| nsieve | 695 | 2126 | 2.9x | | spectral-norm | 134 | 738 | 5.0x |
| string-heavy | 77 | 246 | 2.2x | | fib | 117 | 641 | 4.9x |
| k-nucleotide | 125 | 503 | 3.5x | | fannkuch | 125 | 812 | 6.0x |
| mandelbrot | 104 | 812 | 7.3x | | | | | |

## Comparison with Lua-in-WASM (report-only)

`run-macro-wasm.sh` compares Lunette with native PUC 5.4.9, PUC 5.4.9 compiled to WebAssembly (run under Node's V8 via `node:wasi`
and under wasmtime) and wasmoon, on the same `macro/*.lua` programs, in one session, interleaved. It never gates CI. Results and
caveats are in `WASM.md`. Setup (WSL/Linux; everything is installed under `~/lunette-wasm-tools`, every download checksum-verified):

```
bench/wasm/setup.sh                   # node, wasi-sdk, wasmtime, wasmoon at pinned versions
bench/wasm/build-lua-wasm.sh          # lua-5.4.9.tar.gz (same checksum as ci/build-reference.sh) -> lua.wasm
LUA_REF=<native lua> BENCH_WORK=$HOME/lunette-work bench/run-macro-wasm.sh --runs 9 [--only fib,nsieve] [--no-wasmtime]
```

## Feeding results back

A performance-motivated change cites a before/after benchmark in its description. A benchmark about more than 3x slower than PUC
opens a triage item. A design-level finding (for example a representation change) is recorded as a decision, not a silent patch.

## Profiling runbook (not a mandate)

- CPU: `dotnet-trace collect --profile cpu-sampling -- dotnet src/Lunette.Cli/bin/Release/net11.0/Lunette.Cli.dll bench/macro/fib.lua`.
- Allocations: BenchmarkDotNet MemoryDiagnoser (the `Allocated` column in `run-micro.mjs` output), or EventPipe GC allocation-tick
  events via `dotnet-trace collect --providers Microsoft-Windows-DotNETRuntime:0x1:5`.
- JIT output of the interpreter loop: `DOTNET_JitDisasm="Execute*" DOTNET_JitStdOutFile=jit.txt`, reusing the MinOpts detection
  from `tests/Lunette.Tests/JitBudgetTests.cs`.
- Zero-allocation hot-path assertions belong to `AllocationTests`, not here.
