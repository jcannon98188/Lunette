---
kind: map
title: Build spec for a .NET 11 Lua 5.4 engine
status: open
created: 2026-11-20
updated: 2026-11-20
---

## Destination

A build spec from which the engine can be built: a pure-managed C# (.NET 11) implementation of Lua 5.4. It covers architecture, module boundaries, key semantic decisions and a test strategy gated on the official Lua 5.4 test suite. The spec is the deliverable; the implementation is a separate, later effort.

## Notes

Settled in charting (grilling with the user):

- Pure managed C# on .NET 11 (SDK 11.0.100 installed); no native dependency; embeddable library plus a minimal `lua`-compatible CLI used as the test driver.
- "5.4 compliant" = full language and stdlib semantics per the reference manual, except bytecode compatibility (`string.dump`, binary chunk loading).
- Correctness gate: the official Lua 5.4 test suite runs, with every skipped or waived file listed and justified; differential testing against the reference `lua` 5.4 binary supplements it. Waivers cover tests that cannot apply to a managed runtime (the `T` library, exact GC accounting).
- Performance is very important and must shape architecture decisions, but there is NO numeric performance gate.
- Coroutines: heap-allocated frames, no OS threads; deep recursion and `pcall` must not depend on .NET stack depth.
- Strings: byte-string semantics are required for compliance. User wants an opt-in mode for .NET interop; its exact meaning is an open ticket.
- GC: `__gc` and weak tables are best-effort on the .NET GC; `__close` is exact and deterministic.
- Native AOT / trimming / IL-compilation backend is NOT assumed; it is a deliberate decision ticket.
- Plan, don't do: tickets resolve decisions, not code.
- Standing preference (user): performance wins that are difficult or need code discipline are acceptable; discipline is to be enforced with Roslyn analyzers and strict testing. Specify such rules in the spec, with their enforcement mechanism.

## Decisions so far

- [Official Lua 5.4 test suite: contents and requirements](tickets/01-official-lua-5-4-test-suite-contents-and-requirements.md): Suite is 5.4.9 (MIT, 33 files, `all.lua` driver); `T` library, `debug`, `io`/`os`, locale and GC-count asserts are the managed-engine hurdles; the suite also runs files through `string.dump`/`load`; Windows reference binary is only 5.4.8.
- [.NET 11 runtime facts relevant to a managed Lua VM](tickets/03-net-11-runtime-facts-relevant-to-a-managed-lua-vm.md): Object ref + double side by side in a 16-byte struct works, but overlapping them (NaN-boxing style) throws TypeLoadException; Native AOT has no DynamicMethod; hot-loop methods reach Tier1 via OSR; switch dispatch is a jump table.
- [Prior art: existing .NET Lua implementations](tickets/02-prior-art-existing-net-lua-implementations.md): No mature 5.4 .NET engine exists; the closest managed designs are heap-state-per-coroutine bytecode VMs with a tag+double+object struct value; Lua-CSharp has an opt-in byte-string mode; none implement weak tables or `__gc`; evidence (self-reported, via a summarising fetch tool) needs re-checking.
- [String model and the opt-in .NET interop mode](tickets/04-string-model-and-the-opt-in-net-interop-mode.md): One byte-string core, single-allocation headered `byte[]` strings, interned short strings, no intermediate allocations in the string library, zero-copy span-based host API, UTF-8 boundary, ASCII "C" locale; "interop mode" is only boundary helpers, no semantic switch.
- [Value representation and number tower](tickets/05-value-representation-and-number-tower.md): 16-byte `{object? Ref; long Bits}` value with type derived from Ref; PUC-exact number semantics; PUC-faithful array/node tables (so `#t` matches the reference); PUC-style open/closed upvalues over a growable per-coroutine `Value[]` stack.
- [Execution architecture and coroutine model](tickets/06-execution-architecture-and-coroutine-model.md): One-pass PUC 5.4 register bytecode (own encoding, no AST); one `switch` loop with hot paths inline and cold paths in helpers; Lua-to-Lua calls are heap frames, metamethods/iterators/pcall yieldable, sort/gsub/load-reader re-entrant and non-yieldable; nested resume with PUC limits (1M stack slots, 200 re-entrancy levels); bytecode `Proto` is the seam for a later backend.
- [Host API shape](tickets/07-host-api-shape.md): Idiomatic C# host API over one 16-byte `LuaValue`; single-threaded `LuaState`; span-based host-function fast path with opt-in yield/continuations; no-reflection core; best-effort sandbox limits; minimal PUC-compatible CLI.
- [Error model, pcall and debug library scope](tickets/08-error-model-pcall-and-debug-library-scope.md): Errors are pending-state plus in-loop unwinding with no exceptions on common paths (throw only as an ergonomic fallback caught outside the loop); PUC-literal message text; full `debug` library; two source-generated loop variants for hook cost.
- [Test harness and waiver policy](tickets/09-test-harness-and-waiver-policy.md): Pinned unmodified 5.4.9 suite, one test per file plus CLI `all.lua` run, no managed `T` (`api.lua` waived), reason-coded waiver manifest applied as overlays, reference 5.4.9 built in CI for differential testing, tiered milestones, ratchet CI via `required-files`.
- [Native AOT, trimming and IL-compilation backend](tickets/10-native-aot-trimming-and-il-compilation-backend.md): Core is AOT/trim-safe with no dynamic code; any IL backend is a separate optional package gated on IsDynamicCodeSupported and not committed to by the spec; AOT verified by a CI Native AOT CLI run on a suite subset; Lua-to-C# AOT compilation out of scope.
- [JIT MinOpts thresholds and detecting them in CI](tickets/11-jit-minopts-thresholds-and-detecting-them-in-ci.md): Retail JIT hard-codes five MinOpts limits (60000 IL bytes, 20000 IL instrs, 2000 blocks, 2000 locals, 8000 local refs; unchanged .NET 8–11, not configurable); detect via JitDisasmSummary or MethodLoadVerbose; AggressiveOptimization does not bypass them; an 85-opcode loop is only 20–40% of each limit; instr count/local refs trip before IL size, and a silent inlining cliff sits at 921 locals.
- [GC, weak tables and __gc on the .NET GC](tickets/12-gc-weak-tables-and-gc-on-the-net-gc.md): Weak tables are a separate storage variant on WeakReference/DependentHandle (ephemerons, no own mark pass); `__gc` via a finalizable sentinel plus a Lua-thread drain queue (reverse-mark order per batch, registry for Dispose); `collectgarbage` maps best-effort onto the .NET GC; `gc.lua` gets per-assertion overlays, `gengc.lua` is waived whole.
- [Short-string intern table lifetime and weakness](tickets/13-short-string-intern-table-lifetime-and-weakness.md): Interning is a best-effort optimisation, not an invariant: per-state bounded 4-way set-associative strong cache (no weak handles), 40-byte cut-off, seeded full-length hash, pointer-first equality with hash/length/bytes fallback, pinned reserved strings, non-allocating span lookup, and CI variants (capacity-1 cache, constant hash) prove correctness without it.

## Not yet specified

_Nothing currently in the fog: all remaining decisions are live tickets._

## Out of scope

- [Bytecode compatibility](map.md): `string.dump` and loading precompiled binary chunks are not required; the engine need not read or write PUC bytecode.
- No numeric performance gate (user decision): performance shapes design choices but is not a pass/fail target.
- Building the engine: the implementation is a separate, later effort; this map ends at the build spec.
- Ahead-of-time Lua-to-C# compilation: source-generating C# from Lua scripts at build time is excluded; it needs a second front-end and its own semantics-parity story, beyond a spec for the interpreter engine (decided in [Native AOT, trimming and IL-compilation backend](tickets/10-native-aot-trimming-and-il-compilation-backend.md)).
