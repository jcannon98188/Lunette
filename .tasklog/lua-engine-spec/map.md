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

## Not yet specified

- Module and solution layout, and the build order for the stdlib (string patterns/format/pack, table, math, utf8, os, io, coroutine, debug).
- The `io` and `os` portability story (files, processes, clock, locale) on .NET.
- GC/weak-table/`__gc` implementation approach on the .NET GC.
- Performance methodology: benchmarks and profiling, with no pass/fail gate.
- Structure and format of the final build spec document.
- CI and how the suite and differential tests run in it.
- Enforcement of code-discipline rules: which Roslyn analyzers and tests guard the performance rules (e.g. no intermediate string allocations, header-aware string access, allocation budgets on hot paths).
- Lifetime and weakness of the short-string intern table under the .NET GC (depends on the GC/weak-table approach above).

## Out of scope

- [Bytecode compatibility](map.md): `string.dump` and loading precompiled binary chunks are not required; the engine need not read or write PUC bytecode.
- No numeric performance gate (user decision): performance shapes design choices but is not a pass/fail target.
- Building the engine: the implementation is a separate, later effort; this map ends at the build spec.
