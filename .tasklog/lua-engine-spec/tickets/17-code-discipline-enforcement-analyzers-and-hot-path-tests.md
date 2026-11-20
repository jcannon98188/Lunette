---
kind: ticket
title: "Code-discipline enforcement: analyzers and hot-path tests"
status: resolved
type: grilling
blocked_by: [14]
created: 2026-11-20
updated: 2026-11-20T03:16:57Z
---

## Question

Which performance and discipline rules from earlier decisions (no intermediate string allocations in the string library, header-aware string access, no reflection or dynamic code in core, allocation budgets on hot paths, no exceptions on common error paths) are enforced, and by what mechanism: custom Roslyn analyzers, built-in analyzer rulesets, allocation-budget tests, or review checklist? Include the interpreter-loop JIT budget from the JIT MinOpts research (instruction/local-ref/block/locals ceilings near 70% of the JIT limits, `MinOptJitted` check, leftover-call check for the 921-locals inlining cliff, whether the loop carries `AggressiveOptimization`). For each rule, name the rule, its scope, the enforcement tool, and the failure mode.

## Answer

Decisions (all six recommendations accepted by the user):

1. **Enforcement tiers**: (a) build-time bans via `Microsoft.CodeAnalysis.BannedApiAnalyzers` with a checked-in `BannedSymbols.txt` (System.Reflection.Emit, System.Linq and System.Text.Encoding in core and string library, Activator, Type.GetType); (b) custom Roslyn analyzers in `LuaDotNet.Analyzers`; (c) allocation and JIT run-time tests. A review checklist holds only residuals no tool can check. `IsAotCompatible` is on for `LuaDotNet` so the trim/AOT analyzers (IL2xxx/IL3xxx) enforce "no reflection or dynamic code in core".
2. **Custom analyzers, marker-driven** by an internal `[LuaHotPath]` attribute (dispatch loop, table get/set, call/return, string-library inner loops), all build errors: LD001 no heap-allocating constructs in hot paths (new of reference types, boxing, closures/lambdas, params arrays, string concat/interpolation, LINQ, foreach over anything but arrays/spans); LD002 no `throw` in hot paths (errors use the pending-error-state path; throw only in `[LuaColdThrow]` methods); LD003 no direct indexing of a `LuaString`'s backing `byte[]`, only header-aware accessors; LD004 no `Encoding.*`, `string`, `StringBuilder` or `ToArray()` on string-library paths. Each analyzer has positive and negative tests via `Microsoft.CodeAnalysis.Testing`; the analyzer project builds with the same warnings-as-errors policy.
3. **Allocation-budget tests**: unit-test project, Release, warm a Lua scenario then assert an exact per-iteration `GC.GetAllocatedBytesForCurrentThread()` budget. Zero-alloc: arithmetic loop, Lua-to-Lua call/return, table array and hash get/set steady state, non-erroring `pcall`, `pcall` of `error()`, coroutine resume/yield steady state. String-library scenarios (`sub`, `format('%d')`, `rep`, `gsub`): budget equals the single result allocation. Allocation only, never time, so consistent with the no-numeric-perf-gate decision; PR gate; budgets in `alloc-budgets.json`.
4. **Interpreter-loop JIT budget**, three checks on every PR in Release at 70% ceilings (<=14000 IL instructions, <=5600 local refs, <=1400 basic blocks, <=36000 IL bytes, <=~500 IL locals): a static IL walk over every `[LuaInterpreterLoop]` method (each generated loop variant budgeted separately); an in-process `EventListener` test asserting no `MinOptJitted`; a scoped `DOTNET_JitDisasm` check asserting no leftover `call` to inline-intended helpers (the 921-locals cliff). `AggressiveOptimization` on the loop methods only, recorded as the starting choice to be revisited against the first real macro benchmark. Numbers live in `jit-budgets.json`.
5. **Unsafe policy**: `AllowUnsafeBlocks` off everywhere except `LuaDotNet`; there `unsafe`, `Unsafe.*` and `MemoryMarshal.*` are allowed only inside the internal `LuaDotNet.Unsafe` helper family (banned-API rule elsewhere). Every helper has a bounds-checked safe twin selected by a `LUA_SAFE_ACCESS` define; a scheduled CI variant builds and runs the whole suite with it so out-of-bounds bugs surface as exceptions.
6. **Governance**: analyzer diagnostics are errors in CI and locally; only suppression is `[SuppressMessage]` with a mandatory justification; a CI ratchet counts suppressions per rule against `suppressions-baseline.json`, which must be edited in the same PR to rise. `alloc-budgets.json`, `jit-budgets.json` and `BannedSymbols.txt` may only be loosened in a PR citing before/after numbers, enforced by review plus CODEOWNERS, not tooling.
