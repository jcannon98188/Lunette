# Benchmark coverage set (ticket lua-engine-build/50)

Report-only. Question: where does Lunette lag native PUC Lua 5.4.9 (and Lua-in-WASM) on common scripting patterns the original macro set
does not cover? Nine new scripts in `macro/` (original code, see `macro/LICENSES.md`), picked up automatically by
`run-macro-linux.sh` and `run-macro-wasm.sh`, plus a many-small-states harness.

Measured on WSL2 Ubuntu (16 CPUs, shared with other work, so single-shot numbers move +-30%), Lunette at `36185c5` (plain `dotnet`
Release build, startup about 105 ms, PUC 6 ms), median of 5 runs net of each engine's own startup. Ratio = Lunette net / PUC net.

## Results (whole scripts, net of startup)

| script | PUC ms | Lunette ms | ratio | WASM node ms | WASM wasmtime ms | wasmoon ms | what it covers |
|---|---:|---:|---:|---:|---:|---:|---|
| coroutines | 316 | 550 | 1.74x | 2217 | 2956 | 3418 | resume/yield ping-pong, wrap generators, 3-stage pipeline, create+finish |
| closures | 427 | 792 | 1.85x | 767 | 709 | 825 | creation in loops, open/closed upvalues, closure iterators |
| oop | 630 | 1370 | 2.17x | 900 | 1012 | 1040 | 4-level `__index` chain, override+parent call, `__newindex`, `__call`, vector `__add/__mul/__eq` |
| varargs | 472 | 893 | 1.89x | 1043 | 911 | 1001 | `select('#')`/`select(n)`, pack/unpack, forwarding, multiple returns |
| pcall-error | 309 | 628 | 2.03x | 904 | 1231 | 1345 | pcall success, string/table/VM errors, nested, deep unwind, xpcall |
| gc-latency | 1428 | 1148 | 0.80x | 1479 | 1483 | 1259 | churn with a 100k-object live heap (pauses and footprint below) |
| string-build | 540 | 659 | 1.22x | 809 | 809 | 1111 | `..`, concat, format, tostring, tonumber separately |
| table-lib | 853 | 1150 | 1.35x | 1400 | 1430 | 1468 | sort (int/comparator/string/record), insert/remove, move, unpack |
| warmup | 45 | 104 | 2.31x | 121 | 87 | 114 | 40 chunks of identical work, per-chunk time on stderr |

WASM columns come from `run-macro-wasm.sh --runs 3` (same machine, same session). The "!" output-mismatch flags it prints for
wasm are expected: `build-lua-wasm.sh` compiles with `-DLUA_USE_C89`, which gives wasm Lua 32-bit integers, so checksums that exceed
2^31 wrap. The work done is the same, only the integer width differs. Lunette beats every WASM engine on coroutines (3.6-6x faster)
pcall-error and gc-latency, is level with WASM on closures/varargs/string-build/warmup, and is slower than WASM only on oop (1.4x
slower than node:wasi) and table-lib (about 0.85x of node:wasi time, i.e. faster; listed for completeness).

## Per-section ratios (single run, stderr `lap` output; section 1 of each script includes JIT warm-up)

The whole-script ratios hide which constructs lag. Each script prints per-section milliseconds to stderr; two runs agreed to within
the ranges shown.

| script / section | ratio | | script / section | ratio |
|---|---:|---|---|---:|
| oop: 4-level inherited method call | 4.0-4.2x | | pcall: VM runtime error (index nil) | 6.3-8.0x |
| oop: override + parent call | 3.6-4.5x | | pcall: `error("str")` caught | 2.5-5.6x |
| oop: `__newindex` function proxy | 3.1-4.4x | | pcall: success path | 2.1-2.3x |
| oop: `__call` object | 3.4-5.7x | | pcall: table error, nested, deep unwind | 0.9-1.0x |
| oop: vector `__add/__mul/__eq` (allocating) | 1.7-2.6x | | pcall: xpcall + handler | 1.4x |
| closures: closure-built iterator | 2.9-3.2x | | coroutines: ping-pong resume/yield | 2.5x |
| closures: closed upvalue read/write | 2.4-2.6x | | coroutines: create + finish short coroutines | 2.5x |
| closures: creating closures that outlive loop | 2.2-2.5x | | coroutines: wrap generator / 3-stage pipeline | 1.4x / 1.9-2.0x |
| closures: open upvalue, shared upvalue | 2.1-2.3x | | table: `table.unpack` fixed/varying | 2.7-3.0x |
| closures: creation in loop | 1.4x | | table: sort ints / comparator / strings / records | 2.4 / 2.3 / 1.9 / 1.7x |
| varargs: `select('#')` and `select(n)` | 2.6-3.0x | | table: insert/remove, move, removal churn | 1.3 / 1.3 / 0.9x |
| varargs: forwarding through 3 layers | 2.5-2.8x | | string: `tonumber` | 2.2-2.6x |
| varargs: multiple returns | 1.8-2.5x | | string: `string.rep/sub/byte/char` | 2.5x |
| varargs: pack/unpack, `{...}` | 0.9-1.1x | | string: `..`, format, tostring | 1.9 / 1.6-2.4 / 1.0-1.2x |
| | | | string: table.concat, number round trip | 0.4-0.6x |

## GC latency and footprint (`gc-latency.lua`, stderr)

Churn: 400000 iterations replacing a random object in a 100000-object live heap (30 MB PUC, 41 MB Lunette) plus short-lived garbage.
Pause = time of each batch of 100 iterations (`os.clock`, CPU time, so Lunette background GC/JIT threads count too), so a collector stall is the tail.

| | PUC | Lunette |
|---|---:|---:|
| throughput | 2450-2650 ns/iter | 2100-2260 ns/iter |
| batch p50 | 0.12 ms | 0.075 ms |
| batch p99 | 0.23 ms | 4.2 ms (about 18x PUC) |
| batch p99.9 | 28-33 ms | 17-18 ms |
| batch max | 124-131 ms | 21-22 ms |

Memory per object (after a full collect, `collectgarbage("count")`; Lunette's is the managed heap size, so it carries a few percent of noise):

| object | PUC bytes | Lunette bytes | ratio |
|---|---:|---:|---:|
| empty table | 61 | 61 | 1.0x |
| table with 4 array items | 125 | 149 | 1.2x |
| table with 4 hash fields | 157 | 245 | 1.6x |
| 16-byte string | 56 | 53 | 0.9x |
| closure with 1 upvalue | 82 | 117 | 1.4x |

Lunette wins on throughput, median and worst-case pause (PUC's stop-the-world-like steps at table growth and full-cycle atomic phase
show as the 100+ ms max), but the typical gen0 pause (p99) is an order of magnitude above PUC's incremental step.

## Warm-up (`warmup.lua`, stderr)

Chunks of identical work (about 1.1 ms each in warm PUC). PUC's first 5 chunks take 6.2 ms and are flat. Lunette's first 5 take
37-57 ms (6-9x), the first chunk alone 14-24 ms; the curve settles to about 2.2 ms per chunk (2x PUC) only after chunk 20 or so, so the
warm-up penalty is about 25-45 ms on top of the 100 ms process startup. Whole-script ratio 2.3-2.65x net.

## Many small states (`StateBenchmarks.cs` vs `states-puc.c`)

One state per request or test: create (standard libraries opened), optionally run a chunk, dispose. Lunette is a BenchmarkDotNet
in-process run after warm-up (a cold process pays the JIT first); PUC is best of 7 of 2000 states.

| shape | PUC us/state | Lunette us/state | ratio | Lunette allocated |
|---|---:|---:|---:|---:|
| create + dispose | 29.7 | 27.7 | 0.93x | 101 KB |
| create + run tiny chunk + dispose | 36.1 | 33.0 | 0.91x | 106 KB |
| create + fill (1000 ints, 200 strings) + dispose | 140 | 118 | 0.84x | 320 KB |

No gap once warm. Each state allocates about 100 KB of managed memory, so thousands of live states are 100s of MB.

## Gaps over 2.5x and diagnosis hypotheses

None of the whole-script ratios exceeds 2.5x (warmup is 2.3-2.65x), so the gaps are per-construct:

1. **Metamethod function calls (`__index` chains, `__newindex`, `__call`): 3-5.7x.** `Meta.CallMeta` runs a metamethod function on a
   nested interpreter loop (`Interpreter.Call1`) instead of pushing a frame in the running loop, and inherited method lookups go
   through `PrepareIndex` (a generic out-parameter helper) at every level without an absent-metamethod cache (PUC keeps per-table flag bits
   for `__index`/`__newindex`). Pure-table `__index` chains (no function) are probably the cheaper half; the `__newindex`/`__call` cases
   pay the nested-loop entry. Check by profiling `oop.lua` sections 1 and 3 separately.
2. **Errors built by the VM or `error("string")`: 2.5-8x**, while table errors, nested pcall and deep unwinding are at parity. So
   .NET exception throw/catch is not the cost; building the message is: `Where`/`At` position lookup, `VarInfo.Describe` (symbolic
   scan for "local 't'", "field 'x'") and string formatting through .NET strings to bytes. Hypothesis: line-info decode or
   `VarInfo.Describe` allocates or scans from the function start per error.
3. **Warm-up: first 5 chunks 6-9x, 25-45 ms penalty.** Tier-0 JIT of the interpreter and library methods, then OSR/tier-1 promotion
   of `RunLoop`. The CLI already sets `TieredCompilation.CallCountingDelayMs=0` and `--r2r` helps startup but not
   `AggressiveOptimization` methods (see `PROFILE.md`). Candidates: TieredPGO off, `TieredCompilation.BackgroundWorkerTimeoutMs`,
   a quick-jit-for-loops setting, or partial R2R of the loop with a profile.
4. **GC p99 pause 4.2 ms vs 0.23 ms.** Generational .NET GC: every gen0 collection must scan card-marked old tables because the churn
   mutates a large old array (`live[slot] = {...}`). That is the generational write-barrier cost of a large mutated live heap, not a
   collector bug. Check with `DOTNET_gcServer=0`, `GCgen0size` (larger gen0 = fewer, not shorter, pauses) and `GCgen0MaxBudget`
   experiments; concurrent GC does not affect gen0.
5. **Closure-built iterators in `for ... in` (2.9-3.2x) and closed upvalue access (2.4-2.6x).** The iterator is a generic-for call of a
   closure each step (being addressed by the generic-for fusion work); closed upvalue read/write goes through an `UpVal` object
   indirection that PUC does too, so the remaining difference is the general per-instruction cost (1.5-2x everywhere).
6. **Vararg intrinsics (`select`, forwarding): 2.5-3x.** `select` is a builtin call with a variable-length argument array; PUC copies
   in place on the stack. The builtin call protocol (ticket 47) did not special-case `select('#', ...)` / `select(n, ...)` on the
   frame's varargs. Candidate: compile-time recognition of `select(n, ...)` and `select('#', ...)`.
7. **`table.unpack` with a range (2.7-3.0x), `tonumber` (2.2-2.6x), `string.rep/sub/byte` mixes (2.5x), sort of integers (2.3-4.9x
   depending on size; the small-size runs are dominated by warm-up).** Per-call builtin overhead plus result-array handling; sort
   without comparator may not use a specialised integer path.
8. **Borderline (2.5x): coroutine ping-pong and creation.** Each resume/yield is a thread hand-off in the coroutine implementation;
   Lunette is still 3.6-6x faster than the WASM builds here, so no action is needed except to watch it.

Footprint is under the threshold everywhere (worst: table with 4 hash fields, 1.6x; closure, 1.4x).

## After ticket 52 (metamethod function calls)

Hypotheses checked first. (a) "`Meta.CallMeta` uses a nested loop": not for Lua functions; `StartMeta` already pushed a Frame with a
`Continuation`, only builtin metamethods use the nested call. (b) "no absent-metamethod cache": `LuaTable.Flags` already caches the
absence of six events; only a *present* `__index` costs a hash lookup per level. (c) `__index` chain walk and (d) `__call` argument
reshuffle were real costs. Measured with per-construct probes (2M iterations, min of 7 interleaved rounds, WSL, ms; noise about 5%) and
`oop.lua` (min of 9, whole script):

| Probe | PUC | before | after |
|---|---:|---:|---:|
| `d:get` 4-level inheritance | 90 | 283 | 253 |
| `a:get` 1-level | 58 | 169 | 155 |
| field via chain `d.get` | 59 | 234 | 210 |
| `__index` function | 47 | 196 | 143 |
| `__newindex` function | 52 | 177 | 166 |
| `__newindex` table | 27 | 116 | 113 (no change) |
| `__call` object | 50 | 151 | 141 |
| `__add` function | 44 | 158 | 103 |
| `__eq` function | 49 | 149 | 105 |
| plain function call (control) | 33 | 77 | 76 |
| `oop.lua` whole script | 562 | 1357 | 1275 |

Kept (each measured):
- `Meta.IndexChainStr` / `Meta.NewIndexChain`: walk table-valued `__index`/`__newindex` handlers for `GetField`/`SelfK`/`SetField`
  without building a `MetaCall`; a Lua function handler gets its Frame directly (`StartMetaLua`, which writes the arguments in place).
- `ArithSlow` and `CondSlow` (`__eq`) push the Lua metamethod Frame directly; `Return` completes `Continuation.Value` and `Cond` frames inline
  (nested inside the single `f.Cont != None` test, so plain returns pay nothing extra; an earlier form with two extra compares cost 4-8% on call-heavy closures sections).
- `ResolveCallable` fast path for a table with a closure/builtin `__call` (shift arguments up once, no loop over handler chains).
- `setmetatable(t, mt)` as a `CallFast` builtin (112 to 90 ns per call for the unprotected, unmetatabled-table case; every other case falls back to the builtin so errors are unchanged).

Tried and reverted: inlining a one-level `__index` lookup into `SelfK` (1-level 148 to 135, but 4-level +10% from repeated lookups); caching
`__index` presence through a ref accessor on `LuaTable` (no measurable gain over the Flags path).

Not attempted (gaps): a per-table cached `__index` slot (+8 bytes per table) and a global method inline cache with epoch invalidation
(correctness and GC-retention risk). What remains is mostly the general Lua call cost: a plain call is already about 2.3x PUC, which bounds every
metamethod-call ratio (now about 2.8x for 4-level methods, 2.7x for 1-level, 3.0x `__index` function, 2.1-2.4x `__add`/`__eq`/`__call`);
`oop.lua` section 5 (vector class) is allocation bound at about 1.5x.

## After ticket 53 (VM and `error("str")` raising)

Diagnosis: the hypothesis in gap 2 was only half right. Position lookup and `VarInfo.Describe` are cheap on small functions; the 6-8x
VM-error cost was the **.NET exception throw/catch** (about 2.3 us each), because `error(tbl)` is pending state in the loop but a
VM-detected error (index/call/arithmetic on a bad value) was thrown out of a helper and caught in `Run`. `error("str")` paid for
message assembly: `ChunkId` substring + interpolation per call, decode of the message to UTF-16, concat, re-encode to UTF-8.

Kept (all measured, WSL, interleaved vs base, per-section `lap` of `pcall-error.lua`, 5 runs, ms; PUC = reference):

| section | before | after | PUC |
|---|---|---|---|
| 2. error with a string message (100k) | 131-142 | 75-83 | 38-44 |
| 4. runtime error raised by the VM (100k) | 295-360 | 138-147 | 48-69 |
| steady state, 300k `pcall(vm_error)` | 790-880 | 110 | n/a |
| steady state, 300k `pcall(error"boom")` | 80-100 | 44 | n/a |

- Index get/set, arithmetic and "attempt to call" errors found by `IndexSlow`/`SetSlow`/`ArithSlow`/`Call` are handed back
  (`PrepareIndex`/`PrepareSetIndex`/`PrepareArith` error overloads, `ResolveCallable(deferError)`) and unwound by
  `RaiseInLoop`/`RaiseDeferred` exactly as `Run`'s catch does (fault line noted, `Unwind`, handler run on intact frames). With no
  protected frame at this level the same exception is thrown, so `Traceback`/`TraceEntries` are captured as before.
- `Proto.ChunkDisplay` / `Proto.WhereBytes` cache the chunk id; `Interpreter.Where` and `NoteFault` no longer allocate for it;
  `ErrorValue` builds the positioned message as bytes in one allocation (invalid UTF-8 keeps the old decode/re-encode path).

Reverted: replacing interpolation with `string.Concat` in the message builders (no measurable change).

Remaining gaps: the first ~40-60k errors run at tier-0 speed (section 4 is 100k iterations, so ~90 ms of its 140 ms is warm-up; steady
state is at PUC parity). `AggressiveOptimization` on the helpers is forbidden by `JitBudgetTests`; tiering knobs belong to the
startup ticket. Compare/concat/bitwise/length errors and tail-call "attempt to call" still throw.

## Reproducing

```
# WSL (default distro may not be the Ubuntu one: wsl -d Ubuntu -u root), dotnet SDK on PATH:
BENCH_WORK=/root/lw bench/run-macro-linux.sh --reference /root/lref/lua --runs 5 \
    --only coroutines,closures,oop,varargs,pcall-error,gc-latency,string-build,table-lib,warmup
BENCH_WORK=/root/lw bench/run-macro-wasm.sh --reference /root/lref/lua --runs 3 --only coroutines,...   # needs bench/wasm/setup.sh
# per-section times, pause percentiles, bytes per object, warm-up curve: run a script directly and read stderr
lua bench/macro/gc-latency.lua >/dev/null;  dotnet src/Lunette.Cli/bin/Release/net11.0/Lunette.Cli.dll bench/macro/oop.lua >/dev/null
# many small states
cc -O2 -I<lua-src> bench/states-puc.c <lua-src>/liblua.a -lm -ldl -o states-puc && ./states-puc
dotnet run -c Release --project bench/Lunette.Benchmarks -- --filter '*StateBenchmarks*'
```

Caveats: stdout of every script is a checksum identical between PUC and Lunette (verified); stderr carries the timings, so runners
that discard stderr are unaffected. `os.clock` is process CPU time under both engines (Lunette includes JIT and GC threads). Footprint figures are approximate
(subtract an assumed 16-byte array slot). Section 1 of each script absorbs Lunette's JIT warm-up.

## After ticket 54 (warm-up and GC pause tail)

All numbers: WSL Ubuntu, 16 CPUs shared with other jobs, interleaved runs, medians (7-11 runs for warm-up, 5-9 for gc-latency), PUC 5.4.9.
Scripts: `warmup.lua` (first 5 chunks, whole wall), `gc-latency.lua`, `bench/run-macro-linux.sh [--aot|--r2r]`. Absolute numbers drift
with machine load; compare within a table.

### What the warm-up is, and the lever that removes it

`-v` costs the same as `print(1)` (95-99 ms JIT build, 57-61 ms ReadyToRun): the startup is the .NET runtime plus tier-0 JIT of the
code the CLI touches (about 880 methods, 92 KB of IL for a `-v` run; 185 methods with R2R), not state creation (stdlib registration is
about 2 ms; only 26 `LuaState` methods are jitted and none of the `Register` bodies dominate), so lazy library tables or avoiding eager
string interning would save well under 5 ms. No engine change was made for startup.

The warm-up curve is JIT work spread over the first ~20 chunks (instrumented tier-1, then tier-1, on a background thread whose CPU
`os.clock` also counts), not a single stall. The measured levers on the JIT CLI (`dotnet Lunette.Cli.dll`), first 5 chunks / whole
`warmup.lua` wall, baseline 40-45 ms / 223-244 ms:

| setting | first 5 chunks | wall | verdict |
|---|---:|---:|---|
| baseline (CallCountingDelayMs=0) | 41-45 | 223-244 | |
| `TieredPGO=0` | 38 | 237 | within noise on this script; PROFILE.md found n-body +18-25% slower, stays rejected |
| `TC_QuickJitForLoops=0` | 45 | 307 | worse (+60 ms startup) |
| `OSR_HitLimit=1` | 57 | 242 | worse |
| `TC_BackgroundWorkerTimeoutMs=0` | 40 | 251 | no gain |
| `TieredCompilation=0` | 27 | 328 | +85 ms startup, loses tier-1 PGO |
| `ReadyToRun=0` (no framework R2R) | 57 | 329 | shows how much the framework R2R already helps |
| remove `AggressiveOptimization` from `RunLoop` (experiment, not committed) | 80 | 233 | worse: the loop runs in tier-0 until OSR; also worse on top of R2R (43 vs 32) |
| ReadyToRun publish of the CLI | 32 | 183-193 | -45 ms wall, startup 98 -> 61 ms; needs `-r <rid> -p:PublishAot=false` (see below) |
| R2R + `TieredPGO=0` | 25 | 159-164 | best JIT-hosted number, but PGO off loses throughput on loops; not applied |
| **Native AOT publish of the CLI** | **12.3** | **94-108** | startup 4 ms; no warm-up (penalty 0.1-1.7 ms) |

Native AOT is the answer for the shipped CLI: whole-process wall time, JIT build vs R2R vs AOT, median of 3 (ms):

| | JIT | R2R | AOT |
|---|---:|---:|---:|
| fib | 388 | 353 | 304 |
| n-body | 891 | 833 | 867 |
| binary-trees | 630 | 603 | 504 |
| string-heavy | 233 | 204 | 85 |
| spectral-norm | 416 | 371 | 331 |
| warmup | 221 | 182 | 104 |
| gc-latency | 1324 | 1328 | 1216 |

Caveat: AOT code has no PGO, so `oop.lua` (metamethod-heavy, benefits from guarded devirtualization in tier-1) is slower under AOT (1781 ms vs
1434 ms JIT) and `n-body`, `fannkuch` are level; everything else is equal or faster including startup (4 ms vs 98 ms). Per-benchmark
ratios against PUC (net of startup), Native AOT CLI with the settings below, `run-macro-linux.sh --aot --runs 5`:
binary-trees 1.12x, closures 1.63x, coroutines 1.88x, fannkuch 2.55x, fib 2.63x, gc-latency 0.54x, k-nucleotide 2.04x, mandelbrot 2.11x,
n-body 2.48x, nsieve 1.42x, oop 3.13x, pcall-error 2.09x, spectral-norm 2.13x, string-build 1.26x, string-heavy 0.96x, table-lib 1.31x,
varargs 2.00x, warmup 2.09x. (JIT CLI, same run: warmup 2.41x, string-heavy 1.65x, spectral-norm 2.39x, oop 2.28x, the rest within noise.)

ReadyToRun versus the AOT gate, confirmed: `PublishReadyToRun` is a `publish` feature and needs a RID, while the project sets
`PublishAot=true` for the gate (which publishes the same project with no ILC or trim warnings). R2R therefore stays a publish-time
choice (`dotnet publish -r <rid> --self-contained false -p:PublishAot=false -p:PublishReadyToRun=true`), as `run-macro-linux.sh --r2r` does.
Composite R2R of the framework was not tried (needs a self-contained publish; the CLI is framework-dependent). Embedders running on
the JIT get R2R only by publishing their own app with `PublishReadyToRun`; Lunette.dll is then precompiled with it, except `RunLoop`
(`AggressiveOptimization` methods are never R2R) which JITs at FullOpts on first use (the cheaper alternative, tier-0 plus OSR, was slower: see the table).

### GC pause tail (`gc-latency.lua`)

Lunette's own collection machinery (`GcState`: weak-table windows, finalizer drain) never runs here (no weak tables, no `__gc`), and
`Safepoint` only clears a dead stack slice. The 4.2 ms p99 is the .NET collector in concurrent mode. A run shows about 77 batches over
1 ms: a 4.1-4.2 ms event repeated about every 20-40 batches and 6-18 ms events about every 135 batches (background and gen1 work),
versus 7 in non-concurrent mode. By the runtime's GC sources (not measured here) concurrent GC caps gen0 at 6 MB (more frequent collections, each scanning the card-marked old heap, and a background GC
thread whose CPU also lands in `os.clock`); non-concurrent workstation GC lifts the gen0 cap to up to 200 MB.

JIT CLI, interleaved, median of 7-9 (batch of 100 iterations, ms):

| setting | ns/iter | p99 | p99.9 | max | wall (s) | peak RSS |
|---|---:|---:|---:|---:|---:|---:|
| before (concurrent GC) | 2112-2940 | 4.2 | 17-24 | 19-30 | 1.32-1.71 | 155 MB |
| **`System.GC.Concurrent=false`** | 1297-1388 | 0.17-0.18 | 0.7-0.9 | 76-93 | 0.91-0.96 | 223 MB |
| concurrent, `GCgen0size=64MB` (env only) | 1468 | 0.16 | 5.2 | 66 | 1.00 | |
| concurrent, `GCgen0size=256MB` (env only) | 1289 | 0.18 | 0.85 | 76 | 0.93 | |
| non-concurrent + `GCgen0MaxBudget` 1/16/64 MB, `GCgen1size=32MB`, `GCConserveMemory=5` | 1294-1448 | 0.16-0.20 | 0.7-0.9 | 80-84 | 0.95-1.0 | no effect on max |
| concurrent, `GCgen0MaxBudget=16MB` or `GCConserveMemory=5` | 2139 / 1927 | 4.2 | 17 | 19-20 | | no effect |
| Server GC, 4 heaps (non-concurrent) | 1138 | 0.19 | 0.56 | 26 | 0.82 | not applied (see below) |
| Native AOT CLI, non-concurrent | 1368-1559 | 0.18-0.20 | 0.28-0.30 | 78-86 | 0.81-0.91 | |
| PUC 5.4.9 | 2656-3108 | 0.23-0.29 | 30-35 | 124-144 | 1.4-1.66 | |

Applied: the **CLI csproj sets `ConcurrentGarbageCollection=false`** (written to `Lunette.Cli.runtimeconfig.json` as `System.GC.Concurrent`; baked into
the Native AOT binary too). p99 goes from 18x PUC to 0.7-0.8x PUC, p99.9 from 17-24 ms to under 1 ms (PUC 30-35), throughput of this test improves by
40-50%, and the other macro benchmarks are within noise (fib, n-body, binary-trees, string-heavy, k-nucleotide, spectral-norm, fannkuch, nsieve, oop, closures,
coroutines, table-lib: all within about 5% in a 5-run interleaved comparison). Costs: **the worst single batch grows from about 20-30 ms to 76-93 ms**
(two batches in the run: a blocking gen1/gen2 collection of the 40 MB, 400k-object live heap that concurrent mode used to do in the background
and in small gen0 steps; PUC's max is 124-144 ms, so Lunette still wins max), and peak RSS rises 20-45% on allocation-heavy scripts
(binary-trees 65 -> 95 MB, string-heavy 65 -> 83 MB, gc-latency 155 -> 223 MB; startup and fib unchanged at 41-43 MB).
Not applied: a smaller gen0 budget does not shorten the 70+ ms collection; Server GC removes it (26 ms) but costs heaps and threads
for a short-lived CLI process.

### Embedder guidance (what the CLI sets and what an embedder must set)

Lunette is a library: it does not set any runtime knob. The CLI sets two, in `src/Lunette.Cli/Lunette.Cli.csproj` (moved there from
`runtimeconfig.template.json`, which `PublishAot` rejected with a warning; the AOT publish is now warning-free):

- `System.Runtime.TieredCompilation.CallCountingDelayMs=0` (ticket 40; JIT hosts only, ignored by Native AOT).
- `System.GC.Concurrent=false` (this ticket).

A host that wants the same behavior sets them itself: `<ConcurrentGarbageCollection>false</ConcurrentGarbageCollection>` and a
`RuntimeHostConfigurationOption` for the tiering delay, or `DOTNET_gcConcurrent=0` / `DOTNET_TC_CallCountingDelayMs=0` in the environment.
Disable concurrent GC when the host runs Lua with a large live heap and cares about p99 or throughput more than the single worst pause or
memory (it is a process-wide setting: it also changes the host's own allocations). Keep the default when a host cares most about the
worst-case pause or about memory. `GCgen0size` is environment-only (`DOTNET_GCgen0size`, hex bytes), so it is not a recommended embedder
knob. `TieredPGO` stays on (PROFILE.md: n-body +18-25% when off). Startup-sensitive hosts should publish with ReadyToRun or Native AOT.

### Kept and reverted

Kept: CLI non-concurrent GC; CLI knobs moved into the csproj (no AOT warning); `run-macro-linux.sh --aot`. Reverted (measured, no win or a
loss): TieredPGO=0, QuickJitForLoops=0, OSR_HitLimit, BackgroundWorkerTimeoutMs, TieredCompilation=0, `RunLoop` without
`AggressiveOptimization`, `GCgen0MaxBudget`/`GCgen1size`/`GCConserveMemory`, Server GC, any change to state creation or stdlib
registration (no measurable startup share). Recommendation, not a code change: ship the CLI as a Native AOT binary (`dotnet publish src/Lunette.Cli -c Release`).

Gaps: the max single pause with non-concurrent GC (76-93 ms) is the remaining GC outlier; the JIT-hosted first-chunk cost (the first chunk takes 14-24 ms: `RunLoop`'s FullOpts
compile plus tier-0 of the helpers) has no lever that does not cost throughput; AOT is 20% slower than JIT on metamethod-heavy code (oop).

## After ticket 55 (varargs/select, closure iterators, builtin per-call overhead)

Method: WSL Ubuntu, interleaved base / new / PUC 5.4.9 runs, 9 rounds, medians of the per-section times the macro scripts print (ms; ratio = new / PUC). Probes are `bench` style loops (3M iterations, second pass
of two, so JIT warm-up is excluded) with the loop cost included in every row. Run-to-run noise on this machine is about 5-10% per row, so only rows that moved in every round pair are claimed.

Root cause found: `return select('#', ...)`, `return select(n, ...)` and any other builtin in tail position went through `CallSpecial` -> `CallBuiltin` (argument span, pooled result array), because the
`TailCall` instruction never used the in-place `CallFast` protocol of ticket 47 that `Call` uses. That is the shape of every `count(...)`/`nth(...)` helper.

Kept (each is a measured win):

| change | before | after |
|---|---:|---:|
| `TailCall` of an in-place builtin uses `CallFast` (`select`, `type`, `math.*`, `string.byte`, ...) | varargs 1 (`select`) 245 ms, 2.78x | 204 ms, 2.7x; `return select('#', ...)` probe 287 -> 202 ms / 3M |
| `table.unpack(t [, i [, j]])` in place (plain table, up to 64 values, written straight to the stack; metatables, bad arguments and huge ranges still take the builtin) | table-lib 7: 251 ms, 3.2x | 135 ms, 1.7x |
| `tonumber(x)` in place (one argument; `StringConv.TryToNumber`, base form unchanged) | `tonumber('12345')` 168, int 105, `'0x1f'` 159 ms | 142, 80, 134 ms (1.9-2.1x PUC, was 1.9-2.4x) |
| `math.max` / `math.min` with two numeric arguments in place | 115 ms | 103 ms |
| `Vararg` instruction copies up to 4 values with scalar `Regs.Copy` instead of `Array.Copy` | varargs 4 (forwarding) 145 ms | 133 ms (2.8x -> 2.6x) |

Net: varargs 1 2.78x -> 2.7x, varargs 4 2.8x -> 2.6x, varargs 5 2.3x -> 2.2x, table-lib 7 3.2x -> 1.7x; the other rows are unchanged within noise.

Measured and reverted (no win, code removed):

- **Fusing the Lua-closure iterator call into `TForMove`** (running the three Moves in place and entering the `Call` case directly, saving three dispatches): closure-built iterator 152 vs 167 ms (no gain, slightly worse),
  so the cost of a closure iterator step is the call itself (frame push, upvalue get/set, `Return`), not instruction dispatch. The call path is the same one a plain fixed-arity call takes: an
  empty 4-argument Lua call is ~42 ns vs 16 ns in PUC (2.7x), and a vararg callee adds only ~0-10 ns on top of that.
- **A dedicated `PrecallVararg`** (inline stack check, scalar parameter copies, no `reuse` write-barrier when the closure repeats): forwarding 139.0 vs 139.7 ms, `select` 199 vs 213 ms; neutral.
- Not attempted after probing: `string.sub/rep/char`, `table.insert/remove`, `tostring(int)` are already 1.1-1.6x PUC (tostring is 0.55x) and `tonumber('3.25')` is at parity, so the
  "string.rep/sub/byte 2.5x" gap in the earlier survey was warm-up (first-section JIT), not per-call overhead; ticket 47's finding that in-place `string.sub` is neutral still holds.

Gaps: closure-built iterator stays at 2.7-2.9x, `select` / forwarding / multiple returns at 2.2-2.7x. All of them are bounded by the general Lua call + return cost (2.7x on an empty call, as in `fib`),
which is outside builtin/vararg handling; getting below 2.5x there needs work on `PushFrame`/`Return`/`newframe` reload, not on varargs. `tests/Lunette.Tests/Ticket55FastPathTests.cs` pins the new in-place paths
against PUC-derived expectations. `gc.lua:473` (string-key weak table memory growth) fails in the differential chunk run on this machine for the base build as well; unrelated to this change.
