# Lunette vs Lua-in-WASM (report-only)

Question: how does Lunette stack up against another VM-hosted way to embed Lua, namely real PUC 5.4.9 compiled to WebAssembly and
run in a WASM runtime? Nothing here gates CI.

## Measured

- Lunette: `lua-engine-build` tip `6e9d918` (before perf tickets 41-44 land; the string-library fast paths of `38c1227` are in), plain
  `dotnet build -c Release` of the CLI (not ReadyToRun), .NET 11 SDK, built in a Linux copy of the repo.
- Host: WSL2 Ubuntu on a 16-CPU Windows machine, kernel 6.18.33.2-microsoft-standard-WSL2. All engines in the same session.
  Other agents were using the same WSL instance concurrently, so every number is noisy (see Noise).
- Method (`bench/run-macro-wasm.sh --runs 9`): per engine and program, one warm-up run (which also checks that stdout equals native
  PUC's; every engine matched on every program), then 9 timed runs of the whole process, interleaved (each round runs all engines once
  in rotating order), median wall time. Startup is measured the same way with `bench/startup.lua` (`print(1)`) and subtracted,
  per engine. The table is the mean of two complete sessions (run 1 at 17:56 and run 2 at 18:00 UTC, raw values below), rounded.

| engine | what it is | version |
|---|---|---|
| Lunette | this repo, .NET 11 JIT | `6e9d918` |
| PUC native | `ci/build-reference.sh` (`make linux`, gcc -O2) | Lua 5.4.9 |
| PUC-WASM node | Lua 5.4.9 (same tarball, sha256 `2335b6c5...b8e6`) built by `bench/wasm/build-lua-wasm.sh` with wasi-sdk 34.0 (clang 23.1.0) at -O2 to WASI; run by `bench/wasm/run-node-wasi.mjs` via `node:wasi` | Node v26.13.1 (V8 15.x, Liftoff then TurboFan) |
| PUC-WASM wasmtime | the identical `lua.wasm` | wasmtime 51.0.0 (Cranelift AOT-style compile at load) |
| wasmoon | npm `wasmoon@1.16.0`, Lua compiled with emscripten, run in the same Node | `_VERSION` reports only "Lua 5.4"; the patch release is not exposed and the pinned upstream commit could not be resolved, so it may not be 5.4.9 |

Pinned downloads (all sha256-verified by `bench/wasm/setup.sh` before extraction): node-v26.13.1-linux-x64.tar.xz
`5c063e46bab0fbc1c17cc445d7ff129cbdcd0471be6ed83b6ab864f8143bc864` (from nodejs.org SHASUMS256.txt), wasi-sdk-34.0-x86_64-linux.tar.gz
`b761e3a0721dbae9c09a0059e5fdb2bf917d1b4a8a7b430fb3b5aafb0984b2c4`, wasmtime-v51.0.0-x86_64-linux.tar.xz
`a18020674fac4107f329bc9ed720ea9c88e4b30a61e8a527787bcce051123d32` (GitHub release asset digests), wasmoon 1.16.0 via npm
(exact version; npm checks the registry integrity hash).

## Results (net of startup, ms; lower is better)

| benchmark | Lunette | PUC | WASM node | WASM wasmtime | wasmoon | L/PUC | L/node | L/wasmtime | L/wasmoon | node/PUC | wasmtime/PUC | wasmoon/PUC |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| binary-trees | 755 | 448 | 609 | 623 | 703 | 1.7x | 1.2x | 1.2x | 1.1x | 1.4x | 1.4x | 1.6x |
| fannkuch | 723 | 121 | 306 | 200 | 310 | 6.0x | 2.4x | 3.6x | 2.3x | 2.5x | 1.7x | 2.6x |
| fib | 527 | 110 | 281 | 224 | 289 | 4.8x | 1.9x | 2.4x | 1.8x | 2.6x | 2.0x | 2.6x |
| k-nucleotide | 404 | 119 | 224 | 208 | 238 | 3.4x | 1.8x | 1.9x | 1.7x | 1.9x | 1.7x | 2.0x |
| mandelbrot | 743 | 96 | 386 | 197 | 350 | 7.7x | 1.9x | 3.8x | 2.1x | 4.0x | 2.1x | 3.6x |
| n-body | 1799 | 351 | 993 | 727 | 990 | 5.1x | 1.8x | 2.5x | 1.8x | 2.8x | 2.1x | 2.8x |
| nsieve | 2068 | 774 | 1285 | 1170 | 1289 | 2.7x | 1.6x | 1.8x | 1.6x | 1.7x | 1.5x | 1.7x |
| spectral-norm | 646 | 127 | 389 | 257 | 401 | 5.1x | 1.7x | 2.5x | 1.6x | 3.1x | 2.0x | 3.2x |
| string-heavy | 125 | 72 | 125 | 110 | 122 | 1.7x | 1.0x | 1.1x | 1.0x | 1.7x | 1.5x | 1.7x |

Ratios above 1 in the `L/x` columns mean Lunette is slower than that engine.

Startup (`print(1)`, wall ms, median): Lunette 90 to 124, native PUC 4 to 6, WASM node 37 to 52, wasmtime 18 to 25, wasmoon 49 to 64
(first and second session).

Reading: Lunette is roughly 1.6x to 2.4x slower than PUC-WASM(node) and wasmoon on the loop-heavy programs (fib, fannkuch, n-body,
spectral-norm, mandelbrot, k-nucleotide, nsieve), and about 3.6x to 3.8x slower than wasmtime on the two float-heavy ones (fannkuch,
mandelbrot). On allocation-heavy binary-trees and on string-heavy it is within about 20% of every WASM engine. WASM costs about
1.4x to 4x over native PUC. Lunette's startup (about 90 ms) is the largest of all engines, which matters for short scripts but is
subtracted above.

Raw sessions (net ms, order Lunette / PUC / node / wasmtime / wasmoon), for judging noise:

| benchmark | run 1 | run 2 |
|---|---|---|
| binary-trees | 763 447 611 625 704 | 747 449 607 621 701 |
| fannkuch | 745 122 314 202 317 | 700 119 298 197 303 |
| fib | 546 111 284 225 294 | 507 109 278 223 283 |
| k-nucleotide | 422 119 233 211 246 | 386 118 215 204 229 |
| mandelbrot | 790 97 430 207 363 | 696 95 341 186 337 |
| n-body | 1790 343 999 749 1000 | 1808 358 986 705 979 |
| nsieve | 2034 736 1265 1144 1247 | 2102 812 1304 1196 1330 |
| spectral-norm | 652 128 392 259 405 | 639 126 386 255 396 |
| string-heavy | 136 70 124 110 123 | 113 74 125 109 120 |

## Caveats

- Noise: other agents shared the WSL instance. Sessions agree within about 10% for most cells; the bigger differences are mandelbrot
  node/Lunette (up to 20%) and Lunette's startup baseline (90 vs 124 ms), which moves every Lunette net figure by that much. nsieve and
  mandelbrot show wide min-max spreads in the raw wall times (see the script output). Treat ratios as good to about 15%.
- Startup is subtracted as a constant. For V8 this misses JIT warmup that belongs to the program (Liftoff baseline code runs first and
  hot functions tier up to TurboFan during the run), so node's and wasmoon's net figures include their warmup; the same holds for
  Lunette's tiered JIT. wasmtime compiles the whole module before running, which lands in its startup baseline.
- The WASM runs are not a pure-compute measurement: process spawn, module compile, and WASI/stdio setup are in the wall time, then
  subtracted via `print(1)`; any startup that scales with work (none expected) would remain.
- Memory limits: wasmoon and node:wasi use the default (growable, 4 GiB cap for 32-bit linear memory) memory; no benchmark here came near
  it. Programs needing more than 4 GiB, or relying on 64-bit address space, cannot run in wasm32 at all.
- WASM lacks io/os parity (no `system`, `tmpfile`, `os.tmpname`; stubbed in `bench/wasm/wasi-stubs.c`).
  So only programs that print are comparable. The `macro/*.lua` programs only print and read
  the optional `arg`; wasmoon does not set `arg`, so all programs use their default sizes, which are the ones the other engines get too
  (no arguments are passed to any engine).
- The WASM build uses WebAssembly exception handling (exnref) for `setjmp`/`longjmp`, which is how Lua raises errors; none of the
  macro programs raise errors in the hot path, so this does not affect the timing, but error-heavy Lua would behave differently.
- Different Lua builds: native PUC is built by gcc; the WASM module by clang at -O2 with `LUA_USE_C89` (no POSIX `mkstemp`/`popen`
  paths, `l_signalT` via emulated signals). wasmoon's embedded Lua release could not be confirmed as 5.4.9.
- wasmoon output goes through a JS callback that the runner buffers and writes once, so its timing excludes per-line console cost.
- Lunette numbers are from before tickets 41-44 (the VM-loop perf work); re-run `bench/run-macro-wasm.sh` after those land.
- `node:wasi` is flagged experimental by Node; its stdout/stdio path is direct file-descriptor writes and is not on the hot path here.
