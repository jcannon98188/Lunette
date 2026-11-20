# Performance profile and findings (ticket lua-engine-build/40)

Measured on WSL2 Ubuntu, 16 logical CPUs, .NET 11.0.100 (`FullOpts`, x64), PUC Lua 5.4.9 built by `ci/build-reference.sh`.
Everything below is reproducible with the committed tools:

- `bench/run-macro-linux.sh` (whole-process wall time, median of N after a warm-up, startup measured with `bench/startup.lua` = `print(1)` and subtracted).
- `bench/profile-probes.lua` (ns per construct, net of the empty loop, same file under both engines).
- `DOTNET_JitDisasm=RunLoop DOTNET_JitStdOutFile=...` for the interpreter loop; `dotnet-trace collect --profile dotnet-sampled-thread-time --format Speedscope`;
  opcode and opcode-pair counts from a throw-away counter patch of the loop (not committed; one `Prof.Op[(int)i.Op]++` after the fetch).

## 1. Baseline (median of 5, ms; ratio = (Lunette - its startup) / (PUC - its startup))

Startup: Lunette 88-99 ms, PUC 5-6 ms. Config A is the tree as it was (plain `dotnet build`, runtime defaults); B and C are after this ticket.

| benchmark | PUC | A: Lunette before | ratio A | B: + CallCountingDelayMs=0 | ratio B | C: B + ReadyToRun | ratio C | dispatches (M) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| binary-trees | 443 | 968 | 2.0x | 847 | 1.7x | 869 | 1.8x | 78.9 |
| fannkuch | 126 | 960 | 7.2x | 812 | 6.0x | 761 | 5.9x | 145.8 |
| fib | 118 | 698 | 5.4x | 641 | 4.9x | 613 | 5.0x | 81.1 |
| k-nucleotide | 122 | 680 | 5.0x | 503 | 3.5x | 483 | 3.6x | 38.5 |
| mandelbrot | 102 | 871 | 8.0x | 812 | 7.3x | 773 | 7.4x | 126.6 |
| n-body | 364 | 2038 | 5.4x | 1854 | 5.3x | 1851 | 5.3x | 263.6 |
| nsieve | 737 | 2004 | 2.6x | 2126 | 2.9x | 2015 | 2.8x | 276.5 |
| spectral-norm | 136 | 804 | 5.4x | 738 | 5.0x | 719 | 5.2x | 108.1 |
| string-heavy | 76 | 422 | 4.6x | 246 | 2.2x | 217 | 2.2x | 2.6 |

(Run-to-run noise is about 3-5%; nsieve B is inside it, earlier runs of the same config gave 1.9-2.0 s.)
Target for the effort (every benchmark under 3x net of startup): met today by binary-trees, string-heavy (after config), nsieve; missing for the
six VM-bound ones (fib, spectral-norm, n-body, fannkuch, mandelbrot at 5-7x, k-nucleotide at 3.5x).

## 2. Runtime-config levers (measured, wall ms; `--engine-env` runs, 3 runs each)

| lever | startup | effect on steady state / whole run | verdict |
|---|---|---|---|
| `TC_CallCountingDelayMs=0` (runtimeconfig `System.Runtime.TieredCompilation.CallCountingDelayMs`) | none | JIT build: k-nucleotide 680 -> 503, string-heavy 422 -> 246, binary-trees -12%, fannkuch -15%, n-body -9%, fib -8%; nsieve flat within noise | **applied** (`src/Lunette.Cli/runtimeconfig.template.json`) |
| ReadyToRun publish of the CLI (`--r2r`) | 90 -> 58 ms | string-heavy 416 -> 201, fib -10%, binary-trees -13%; steady state unchanged | **recommended for the shipped CLI**, not a csproj change: it needs `-r <rid> -p:PublishAot=false`, so it conflicts with the AOT gate's `PublishAot=true` default. `bench/run-macro-linux.sh --r2r` shows the effect |
| `TieredPGO=0` | none | n-body +18-25% slower, mandelbrot +3-4%; string-heavy -23% (no instrumented tier) | rejected, net loss on the worst benchmark; with R2R the string-heavy win disappears anyway |
| `TieredCompilation=0` | +76 ms | n-body +23% slower (+64% on the R2R build); string-heavy -100 ms | rejected (loses tier-1 PGO; R2R code is never re-jitted) |
| `TC_QuickJitForLoops=0` | +42 ms | no gain | rejected |
| `OSR_HitLimit=1`, `TC_CallCountThreshold=5`, `TieredPGO_InstrumentOnlyHotCode=0` | none | within noise, or worse (n-body +20% for the last) | rejected |

Why OSR/PGO levers do little for the loop itself: `RunLoop` is `[MethodImpl(AggressiveOptimization)]`, so it is compiled once, straight to FullOpts, with
synthesized PGO only and never as ReadyToRun. The runtime levers therefore only act on the helpers it calls (`LuaTable.Get`, `Arith.Binary`, the string library),
which is where the PGO sensitivity of n-body (struct-heavy helpers) comes from.

## 3. What the VM is doing: dispatch counts and per-construct cost

### 3.1 The bytecode executes 1.5-2.3x more instructions than PUC's would

Dynamic opcode mix (share of all dispatches), from the counter patch:

| benchmark | Move | LoadK | other top opcodes |
|---|---:|---:|---|
| mandelbrot | 50.5% | 8.0% | Mul 12.5, Add 9.4, Jmp 6.3 |
| n-body | 47.7% | 13.5% | Mul 10.2, GetTable 9.1, Add 5.3, SetTable 4.6 |
| fannkuch | 46.8% | 11.6% | GetTable 7.2, SetTable 6.7, Test 6.4, Jmp 6.1 |
| spectral-norm | 36.7% | 13.4% | Mul 10.0, Add 10.0, Sub 6.7 |
| nsieve | 33.0% | 0.2% | Jmp 18.8, ForLoop 15.8, LoadBool 12.5, SetTable 12.5 |
| k-nucleotide | 31.2% | 15.6% | Add 9.1, Jmp 8.4, GetTable 4.5 |
| fib | 21.7% | 17.4% | GetUpval 8.7, Test 8.7, Call 8.7, Return 8.7, Lt 8.7, Sub 8.7 |
| binary-trees | 20.5% | 18.3% | Call/Test/Return/GetTable/GetUpval 8% each |

Cause (compiler, `LuaCompiler.ExprCore`/`EmitBinary`): expression code generation is target-driven with no operand forms. `a + b` on locals is
`Move t,a; Move u,b; Add t,t,u` (3 dispatches where PUC emits 1); `x + 1` adds a `LoadK`; `t.k` is `Move t; LoadK key; GetTable`; `if a < b` is
`Lt` (materialises a boolean) then `Test` then `Jmp` (3 dispatches where PUC fuses to 1-2); a numeric `for` iteration is `ForLoop` + `Jmp` (2 where PUC has 1);
`isPrime[k] = false` is `LoadBool; SetTable`. Common pairs: `Move>Move` 26% (fannkuch), 25% (mandelbrot), 21% (n-body); `Move>LoadK`, `LoadK>GetTable` (8% of n-body).

### 3.2 Every dispatch costs about 5 ns, PUC about 0.6-1 ns

`profile-probes.lua`, net ns per iteration (best of 2 rounds; PUC in the same run):

| construct | PUC | Lunette | | construct | PUC | Lunette |
|---|---:|---:|---|---|---:|---:|
| `x = a` (Move) | 0.6 | 5.7 | | call empty Lua fn | 7.4 | 32.7 |
| `x = 1` (LoadK) | 0.6 | 6.0 | | call Lua fn, 1 arg, 1 ret | 10.2 | 49.2 |
| `x = a + b` | 0.8 | 16.6 | | call builtin `math.abs` | 13.4 | 40.5 |
| `x = f * g` (float) | 1.9 | 21.2 | | call builtin `select('#', ...)` | 19.4 | 65.7 |
| `if a < b then` | 2.0 | 20.2 | | method call `obj:m()` | 15.5 | 65.3 |
| `x = t[3]` | 1.4 | 18.8 | | `t[#t+1] = a` | 12.8 | 54.1 |
| `x = t.k` | 2.7 | 20.8 | | `pairs` over 8 entries | 221 | 540 |
| `t[3] = a` | 2.1 | 16.8 | | `ipairs` over 8 entries | 195 | 546 |
| global get (real `_G`) | 3.0 | 17.3 | | numeric `for`, 8 iterations | 29.9 | 135 |
| `#s` | 1.4 | 20.2 | | `s:sub` / `string.byte` | 32 / 25 | 60 / 46 |
| `{}` / `{1,2,3}` / closure | 38 / 78 / 40 | 17 / 59 / 28 | | `tostring(int)`, `string.format('%d')` | 90 / 107 | 64 / 104 |

(Allocation-bound constructs are already at or below PUC; the gap is dispatch, call and table-access machinery.)

JIT disassembly of the loop (`FullOpts`, 16.8 KB code, frame 0xA78 bytes) explains the dispatch cost:

1. A `Move` case is ~35 instructions: two array bounds checks, 16-byte struct copied through stack temps, and `call CORINFO_HELP_ASSIGN_REF` (the
   write barrier for the object half of `LuaValue`). The loop's hot state (`code`, `r`, `bs`, `pc`, `i`) lives in caller-saved registers, so every barrier or helper
   call spills and reloads five of them around the call.
2. Type tests compare `Ref` with `IntegerTag`/`FloatTag` loaded from static fields (`mov rdx, imm64; mov rdx,[rdx]` before every `cmp`), not constants.
3. Arithmetic goes through the shared `Add..Pow` case, then `Arith.Binary(op, a, b, ...)`: 16-byte structs passed by value, a second runtime `switch(op)`, `Meta.ArithEvent`
   spills on the slow path, the result returned through a hidden buffer.
4. The prologue zero-initialises about 2.6 KB of stack (reference-containing temporaries) on every `RunLoop` entry. `RunLoop` is entered again for every
   native-to-Lua callback (`gsub` with a function, metamethods through `Call1`, `pcall` re-entry, coroutine resume), so this is a per-callback tax.
5. `Jmp` does two memory read-modify-writes (`state.Ticks`, `state.GcTick`) on every backward jump, i.e. on every loop iteration; `ForLoop` is a call to a separate method on
   `r` (re-checks bounds on 4 array accesses).

Two quick experiments (temporary patches, reverted) size the first items:

- `Move` skips the barrier when `dst.Ref == src.Ref` (the common numeric case) and `ForLoop` jumps back itself instead of dispatching a `Jmp`: Move 5.7 -> 4.2 ns,
  `x = a+b` 16.6 -> 14.3, float mul 21.2 -> 19.0, 8-iteration for loop 135 -> 127 (so ~1.5 ns per barrier-free store).
- `Return` with exactly one result uses `r[dst] = r[ra]` instead of `Array.Copy(r, ra, r, dst, 1)`: fib 687 -> 615 ms (-10%), binary-trees -4%, spectral-norm -4%.

### 3.3 Sampling profiler (`dotnet-sampled-thread-time`) caveats and results

The sampled stacks attribute time inside `RunLoop` unreliably for fib, n-body and binary-trees (a stale `Array.Copy` leaf appears at 69-98%, which is impossible for n-body's
200 K returns); trust the probes and counters above for those. Where the attribution is consistent:

- nsieve: `RunLoop` 79%, `Buffer.BulkMoveWithWriteBarrierBatch` 13% (table array growth copies while `isPrime[i] = true` appends), `LuaTable.Set` 2.6%, `NumUseArray` 1.7%.
- k-nucleotide: `RunLoop` 44% exclusive, `CallBuiltin` 20% inclusive, `Meta.PrepareIndex` 13% inclusive (every `s:sub(...)` `Self` on a string goes through the metatable `__index` lookup),
  `LuaTable.Get` 12%, `StringLib.Sub` 9.6%, `Arith.Binary` 4.3%, `RawSet` 6.7%.
- string-heavy (only 2.6 M dispatches; time is in the library): `CallBuiltin` 80% inclusive, `gsub` 39% (of which `AddValue`/`CallValues` 20% and `MatchState.Match` 13%),
  `string.format` 15%, `ReadOnlySpan<LuaValue>.CopyTo` -> `BulkMoveWithWriteBarrier` 14%, `StringCache.Intern` 4%, `RunLoop` itself 6%.

## 4. Ranked causes, per benchmark

1. **fib (5.0x)**: (1) call/return machinery: 7.0 M calls at ~33 ns each versus 7 ns (Precall: `EnsureStack`, `PushFrame` with 7 stores and a read of the previous frame, 7 Frame field stores;
   `Return`: `Array.Copy` for one value, -10% measured; `GetUpval` of the recursive local each call walks `Owner.Stack[Index]`); (2) 40% of dispatches are Move/LoadK; (3) `Lt`+`Test`+`Jmp` is 3 dispatches.
2. **mandelbrot (7.4x)**: (1) 58% Move/LoadK dispatches; (2) float arithmetic through `Arith.Binary` (struct by value, double switch, `FromFloat` store with barrier); (3) `Lt`+`Test`+`Jmp`, `ForLoop`+`Jmp`.
3. **fannkuch (5.9x)**: (1) 58% Move/LoadK and `Move>Move` pairs (copying locals into temporaries around table gets/sets); (2) `GetTable`/`SetTable` through generic `Get(LuaValue)`/`RawSet`;
   (3) `Test`+`Jmp` branch pairs and `LoadBool`.
4. **n-body (5.3x)**: (1) 61% Move/LoadK; (2) `GetTable`/`SetTable` with constant string keys: `Move t; LoadK k; GetTable` then `LuaTable.Get` -> `Normalize` -> `FindNode` -> `MainPosition`
   (a 32-bit `%` division for the string hash, PUC uses a mask) -> `KeysEqual`; (3) float arithmetic (`Arith.Binary`, barrier on every `FromFloat` store); (4) PGO matters here (+20% when off).
5. **spectral-norm (5.2x)**: (1) 50% Move/LoadK; (2) float arithmetic; (3) call overhead of `A(i, j)` (3.6 M calls, `Call`/`Return`/`GetUpval`).
6. **k-nucleotide (3.5x)**: (1) `s:sub` on strings: `Self` -> `Meta.PrepareIndex` -> string metatable lookup each call; (2) builtin call overhead (`CallBuiltin`, results array via `Rets`);
   (3) table get/set with string keys (hash `%`); (4) 47% Move/LoadK.
7. **nsieve (2.8x)**: (1) per-iteration `ForLoop`+`Jmp` plus the `Jmp` tick/gc-tick poll (34% of dispatches); (2) `LoadBool`+`SetTable` with barrier; (3) array growth by repeated `Array.Copy`
   (13%, the table starts at key 2 and appends one element at a time).
8. **string-heavy (2.2x)**: startup and JIT (fixed by R2R and call-counting delay); then `gsub` callback path (`CallValues` -> `Run` -> `RunLoop` prologue and result array),
   `ReadOnlySpan<LuaValue>.CopyTo` with write barriers, `string.format`, `MatchState.Match`.
9. **binary-trees (1.8x)**: `Return` `Array.Copy` and `Precall`, `NewTable` + `SetList` for `{left, right}` (`SetList` -> `EnsureArray` + per-element `SetInt`); already close to target.

## 5. Recommendations for tickets 41-44 (scope additions; tasklog is not edited from this branch)

**41 arith/compare/for** (largest lever; expect 1.5-2x on mandelbrot, n-body, spectral-norm, fannkuch)
- Compiler: operand-in-place codegen: use a local's register directly as an operand instead of `Move` into a temporary, and constant operands (`AddK`/`MulK`-style or `RK` forms) instead of `LoadK`.
  This alone removes 30-65% of dispatches in the table above. Keep line info exact (the `RelabelPendingLeft` hack and the debug/active-line tests depend on it).
- Fuse compare+branch (`Lt/Le/Eq` + `Jmp`, plus constant/immediate forms) so a condition costs 1 dispatch and does not materialise a boolean unless needed.
- `ForLoop` takes the back-jump itself (and does the tick/gc poll there) instead of falling into a separate `Jmp`; make `ForLoop`'s integer path inline in the loop body.
- Give `Add/Sub/Mul/Div/...` their own `case`s with inline int-int and float-float fast paths (no `Arith.Binary`, no double `switch`); keep the slow path out of line.
- Skip the write barrier for numeric stores: `if (ReferenceEquals(dst.Ref, tag)) dst.Bits = v` (measured 1.5 ns per store). Needs a `Lunette.Unsafe` helper (LD005 confines `Unsafe`).
- Re-check the JIT budgets (`tests/Lunette.Tests/jit-budgets.json`) after each step; operand forms add opcodes, so move cold bodies out of line to stay under the 70% MinOpts limits.

**42 call/return** (fib, spectral-norm, binary-trees, string callbacks)
- `Return`: scalar copy for 0/1/2 results instead of `Array.Copy` (measured -10% on fib).
- `Precall`/`PushFrame`: collapse the per-call field stores (reset `PcallKind/Cont/IsTail/Native/Closing` only when set, derive `PcallLevel` lazily), avoid `EnsureStack` call when capacity is known.
- Upvalue access: cache the open upvalue's stack array or index to avoid two dependent loads per `GetUpval` (recursive `local function` is a `GetUpval` per call).
- Builtin calls (26-45 ns over PUC's 14): avoid the `ReadOnlySpan` + `LuaValue[]` result protocol for fixed-arity builtins (write results straight to the stack), inline the `Rets` release.
- Reduce `RunLoop`'s prologue (2.6 KB zeroing) by moving cold case bodies and their struct temporaries into non-inlined helpers; this also shrinks the per-callback cost of `CallValues`/`Call1`.

**43 tables** (n-body, fannkuch, nsieve, k-nucleotide)
- Specialised `GetField/SetField` (constant string key) and `GetInt/SetInt`-style opcodes: no `Move`+`LoadK`, no 16-byte key struct, no `Normalize`; string keys are interned, so compare by reference first.
- String hash index: use a power-of-two mask (as PUC `lmod`) instead of `h % ((size-1)|1)`; integer keys in the hash part likewise avoid 64-bit division where possible.
- `t[#t+1] = v` is 4x PUC (`Length()` binary search plus append and rehash): keep a length hint and a fast append path.
- Growth: array part grows by repeated `Array.Copy`; follow PUC's `computesizes` doubling and avoid starting the sieve's `t[2..]` in the hash part (nsieve BulkMove 13%).
- Generic `for` with `pairs`/`ipairs`/`next`: special-case in `TForCall` (no builtin call, no result array per iteration); today 540 ns vs 221 ns for 8 entries.
- `Self` on strings: cache the string metatable's `__index` table to skip `Meta.PrepareIndex` (13% of k-nucleotide).

**44 strings** (string-heavy, k-nucleotide)
- `gsub` with a function/table replacement: avoid `CallValues` (allocates, re-enters `RunLoop` with the 2.6 KB prologue); reuse an arguments buffer.
- Replace `ReadOnlySpan<LuaValue>.CopyTo`-based result copies (barrier-batched memmove, 14%) with a short scalar loop when the length is small.
- `string.format`, `string.sub`, `string.byte` (`CallBuiltin` plus argument conversion: 60/46 ns vs 32/25): fast paths for integer/string arguments.
- `StringCache.Intern` appears at 2-4% in both string benchmarks: check the hash/lookup cost of short-string interning.

**Process/shipping** (not engine code): ship the CLI ReadyToRun (startup 90 -> 58 ms, the largest single win for short scripts and the whole string-heavy gap); keep
`CallCountingDelayMs=0` in the CLI runtimeconfig; leave TieredPGO on. Re-measure all three configurations of section 1 after each of tickets 41-44.
