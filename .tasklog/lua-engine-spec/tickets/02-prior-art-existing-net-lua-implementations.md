---
kind: ticket
title: "Prior art: existing .NET Lua implementations"
status: resolved
type: research
created: 2026-11-20
updated: 2026-11-20T02:33:08Z
---

## Question

Survey existing Lua implementations on .NET (MoonSharp, KopiLua, NeoLua, Lua-CSharp and any others current for 5.4) and managed ports elsewhere that are instructive (e.g. LuaJ, Fengari). For each: Lua version targeted, architecture (bytecode VM vs tree vs IL), value representation, coroutine strategy, string model, host-interop API, known performance results and known compliance gaps, and whether they run the official test suite. Use primary sources (repos, docs, maintainers' write-ups). Output: facts and lessons, not recommendations.

## Answer

Survey is on the throwaway branch `research/dotnet-lua-prior-art` (file `research/dotnet-lua-prior-art.md`, pushed to origin, no PR). Caveat: pages were read through a summarising fetch tool, so key facts should be re-checked against the linked sources before relying on them.

- **Version targets:** only NovaSharp (a MoonSharp fork, 3 stars) and Lunil (5 stars) state a 5.4 target. MoonSharp targets 5.2 (fork says no commits in 4+ years), Lua-CSharp 5.2, NeoLua "level of 5.3", KopiLua 5.1.4. MoonSharp and Lua-CSharp store every number as a `double`.
- **Architecture and values:** most are bytecode VMs: MoonSharp (`DynValue` heap class), Lua-CSharp (16-byte-style struct of tag, `double` and `object`). NeoLua compiles to IL via the DLR. Lunil lowers to one IR run on an interpreter or a .NET 10 JIT. KopiLua is a near line-for-line C port.
- **Coroutines:** two approaches. MoonSharp and Lua-CSharp keep separate VM state per coroutine, no OS thread. NeoLua and LuaJ use one managed thread per coroutine. Yielding across a host callback boundary is a recurring limit in the first group (MoonSharp's workaround is `TailCallRequest`); NovaSharp's compatibility matrix marks it as done.
- **Strings and GC:** UTF-16 in MoonSharp, NeoLua and by default in Lua-CSharp (which has an opt-in byte mode). Fengari and LuaJ are byte-oriented. Every managed engine read relies on the host GC and lacks weak tables or `__gc`; only KopiLua ported Lua's own collector.
- **Test suites and performance:** Lua-CSharp is the only .NET engine whose docs say it carries the official suite, and that is the 5.2 one. NovaSharp uses upstream TAP "TestMore" files. Performance evidence is thin and self-reported (NovaSharp vs MoonSharp, LuaJ's own table, a Lua-CSharp benchmark published as an image the agent did not read).
- **Not verified:** whether Lunil or Fengari run the official suite; whether Lua-CSharp's 5.2 suite files pass unmodified; Lua-CSharp and Lunil VM internals.
