# Benchmarks

Report-only. Nothing here fails a build or blocks `ci-gate`; performance is steered by measurement and reporting.

- `macro/*.lua`: classic whole-program benchmarks (licenses in `macro/LICENSES.md`).
  `node bench/run-macro.mjs [--runs N] [--only fib,n-body] [--out macro.json]` runs each through the `lua`-compatible CLI for
  Lunette and for PUC Lua 5.4.9 with the same command line, wall time including startup (median of N after one warm-up).
  Needs `dotnet build -c Release src/Lunette.Cli` first, and `ci/build-reference.sh .reference` (Linux) for the PUC column.
- `micro/*.lua` + `Lunette.Benchmarks/`: per-opcode and per-stdlib-family snippets, one BenchmarkDotNet benchmark each
  (in-process toolchain, MemoryDiagnoser). `node bench/run-micro.mjs [--filter "*call*"] [--out micro.json]` joins the means with
  the same snippets timed inside PUC by `puc-micro-harness.lua`. Add a snippet by dropping a `.lua` file in `micro/`
  that does a fixed amount of work and returns.
- Headline number: ratio = Lunette time / PUC time, same machine, same run (above 1 means slower than PUC).
  Without a Reference binary (Windows, no C compiler) the runners still report absolute Lunette times.
- `report.mjs` + `.github/workflows/bench.yml`: weekly (and on-demand) run on a pinned runner class; appends JSON lines to
  `history.jsonl` on the `perf-history` branch, writes a job summary and flags any benchmark more than 10% worse than its
  trailing median (non-blocking).

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
