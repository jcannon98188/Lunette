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
