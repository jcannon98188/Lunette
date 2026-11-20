---
kind: ticket
title: Module and solution layout, and stdlib build order
status: resolved
type: grilling
created: 2026-11-20
updated: 2026-11-20T03:13:26Z
---

## Question

What is the project/assembly layout (core VM and compiler, stdlib, host API, CLI, test harness, analyzers, benchmarks, optional IL backend package) and the dependency rules between them? What is the build order for the stdlib (string patterns/format/pack, table, math, utf8, os, io, coroutine, debug) and how does it map to the tiered test-suite milestones from the test-harness decision? Cover namespaces, public vs internal surface, target frameworks, and package naming.

## Answer

Decisions (all five recommendations accepted by the user):

1. **Assemblies**: one core assembly `LuaDotNet` holding value, string, table, lexer/parser/codegen, `Proto`, VM, error model, `LuaState`/host API and the whole stdlib (so the JIT can inline across the hot path and the stdlib can use internal fast paths; the compiler is not split from the VM because `load` needs both and `Proto` is the shared seam). Satellites: `LuaDotNet.Cli` (the `lua`-compatible driver), `LuaDotNet.Analyzers` (Roslyn analyzers), `LuaDotNet.Interop` (opt-in .NET binding via a source generator), `LuaDotNet.IL` (optional IL backend, not committed to by the spec), plus test, benchmark and differential-harness projects.
2. **Dependency rules**: `LuaDotNet` references only the BCL. `Cli`, `Interop` and `IL` use `LuaDotNet`'s public surface only; `Proto` and the opcode set are a public `[Experimental]` surface for `IL`, not exposed via `InternalsVisibleTo`. Only the unit-test and benchmark projects get `InternalsVisibleTo`. Analyzers are a build-only dependency of every project. Nothing depends on the test or differential projects.
3. **Namespaces and public surface**: public types sit in the root namespace `LuaDotNet` (`LuaState`, `LuaValue`, `LuaTable`, `LuaFunction`, `LuaThread`, `LuaString`, `CallContext`, error types). Everything else is `internal`, in sub-namespaces such as `LuaDotNet.Compiler`, `LuaDotNet.Vm`, `LuaDotNet.Libs`. The public surface is tracked with `Microsoft.CodeAnalysis.PublicApiAnalyzers` (`PublicAPI.Shipped.txt`) so every new public member is deliberate. Libraries open via `state.OpenLibs(LuaLibs.Base | LuaLibs.String | ...)`, matching the per-library opt-in sandbox.
4. **Targets and naming**: `net11.0` only, with no multi-targeting and no `netstandard`; the sole exception is the analyzers assembly on `netstandard2.0` (a Roslyn requirement). NuGet IDs match the assemblies: `LuaDotNet`, `LuaDotNet.Cli` (shipped as a `dotnet tool`), `LuaDotNet.Interop`, `LuaDotNet.IL`, `LuaDotNet.Analyzers`. The name `LuaDotNet` is confirmed.
5. **Stdlib build order mapped to test tiers** (driven by what the suite files actually need, since almost every tier-1 file uses `string.format`, patterns and `pcall`): (a) base, `string` without `pack`/`unpack`, `table`, `math`, and a `debug` stub (`traceback`, `getinfo`) close tier 1; (b) `coroutine`, `utf8` and `string.pack`/`unpack` close tier 2; (c) full `debug` (hooks, `sethook`), `os`, `io`, and `package`/`require` close tier 3 (`db`, `files`, `main`). Rule: a library is only "done" when its own suite file passes, and work on a library can't start before the tier below it is green. The exact tier file lists are re-verified against the suite contents when the final spec is drafted.

**Renamed (2026-11-19, after resolution):** the name `LuaDotNet` was replaced by **Lunette**. Read every `LuaDotNet` and `LuaDotNet.*` above as `Lunette` and `Lunette.*`. The ticket text is otherwise frozen as the decision record.
