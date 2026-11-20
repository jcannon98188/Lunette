<img src="assets/icon.svg" width="64" height="64" alt="">

# Lunette

A Lua 5.4 engine written in pure managed C# for .NET 11, plus a small `lua`-compatible command-line interpreter.

Lunette has no native dependencies and no P/Invoke, so one assembly runs on every platform .NET supports. It is meant for embedding in .NET applications: sandboxing, resource limits and host interop are part of the design, not add-ons.

## Status

- Runs the pinned official Lua 5.4.9 test suite, with reason-coded waivers only (see `tests/`). The waivers cover `string.dump` bytecode, some GC accounting checks, a host C API test and a Unix `popen` environment case.
- Differentially tested against reference PUC Lua 5.4.9.
- Speed is roughly 1.2x to 3x the time of native PUC Lua on the macro benchmarks, net of startup. See [bench/README.md](bench/README.md) and [bench/PROFILE.md](bench/PROFILE.md). Startup is about 90 ms (about 58 ms with ReadyToRun).
- Not implemented: loading precompiled bytecode (`string.dump` output) and the C API or C modules.

## Requirements

- .NET SDK 11 (see `global.json`).

## Build and test

```
dotnet build -c Release
dotnet test -c Release
```

The full test run takes about a minute. Linux and Windows are both supported; CI runs the official suite on Linux x64.

## Use the CLI

```
dotnet run -c Release --project src/Lunette.Cli -- script.lua
```

The CLI follows `lua.c`: it takes a script, arguments, `-e` and the usual flags. Ctrl-C raises a catchable "interrupted" Lua error.

## Embed in a .NET app

```csharp
using Lunette;

var state = new LuaState();
state.DoString("print('hello from Lua ' .. _VERSION)");
```

Capabilities are controlled through `LuaStateOptions` (a library allowlist) and the `ILuaPlatform` abstraction for file, clock and environment access. Output goes to `LuaState.Output`. Scripts can be stopped from any thread with `RequestInterrupt`, and budgets for instructions and memory abort runaway scripts.

## Repository layout

| Path | Contents |
|---|---|
| `src/Lunette` | The engine: compiler, VM, standard libraries, GC integration. |
| `src/Lunette.Cli` | The `lua`-compatible command-line interpreter. |
| `src/Lunette.Interop` | Host interop helpers. |
| `src/Lunette.Analyzers` | Roslyn analyzers that enforce engine rules (LD001 to LD006). |
| `tests/` | Unit, differential and official-suite tests. |
| `bench/` | Macro and micro benchmarks, including a Lua-in-WASM comparison. |
| `docs/adr/` | Architecture decision records. |
| `GLOSSARY.md` | Domain terms. |

## Design notes

Key decisions are recorded as ADRs in `docs/adr/`: a 16-byte `LuaValue`, heap-allocated frames with no recursion on the .NET stack, error unwinding through pending state, a single core assembly, best-effort string interning, and weak tables that are weakened only during collection windows.

## Host features not tied to a ticket

- **Interrupt / Ctrl-C.** `LuaState.RequestInterrupt` (sticky until `ClearInterrupt`) and `RequestInterruptOnce` (one "interrupted" Lua error, catchable by `pcall`) stop a running script at its next poll point from any thread. The standalone CLI uses the one-shot form for Ctrl-C, as lua.c does. Intentional, though no ticket asked for it; unlike a budget or memory abort it is an ordinary Lua error and leaves the state usable.

## License

MIT. See [LICENSE](LICENSE). The benchmark programs under `bench/macro` carry their own licenses (`bench/macro/LICENSES.md`), and the official Lua test suite is distributed under the Lua license.

Created in [T3 Code](https://t3.codes).
