---
kind: ticket
title: Host API shape
status: resolved
type: grilling
blocked_by: [4, 5]
created: 2026-11-20
updated: 2026-11-20T02:43:58Z
---

## Question

Decide the embedding API for .NET applications: state/chunk lifecycle, calling Lua from C# and C# from Lua, value conversion and marshalling, userdata and metatable exposure of .NET objects, sandboxing and resource limits, and the shape of the minimal `lua` CLI used as the test driver. Note how the string interop mode interacts with it.

## Answer

Decided with the user (all eight recommendations accepted):

1. **Layering**: Public API is idiomatic C# (`LuaState`, `LuaValue`, `LuaTable`, `LuaFunction`). No PUC-style push/pop stack API for embedders. The internal span-over-stack-window calling convention is also public for power users.
2. **Lifecycle/threading**: `LuaState` is single-threaded, with no internal locks. It may move between threads but is never used concurrently. Many independent states per process; no static mutable globals (only immutable statics such as opcode tables). A coroutine is a `LuaThread` owned by its state. Disposing the state deterministically runs pending `__close` and finalizers where possible.
3. **Host functions**: Raw shape is `int Fn(ref CallContext ctx)`. Args are a span over the stack window, results are pushed via the context, and the return value is the result count. The stdlib uses this fast path. Typed wrappers (`Func<double,double>` and similar) are a convenience layer with a conversion cost.
4. **Calling Lua from C#**: `LuaFunction.Call` has allocation-free overloads (fixed arity 0-4 plus a span overload). Host-to-Lua callbacks are re-entrant and non-yieldable by default (consistent with ticket 06). A host function may yield only by returning an explicit yield request with an optional continuation (`lua_yieldk`/`lua_callk` style).
5. **Value type**: The public `LuaValue` is the VM's 16-byte `{object? Ref; long Bits}` struct. It has implicit conversions from bool/long/double/LuaString/string and typed accessors (`TryGet<T>`, `AsInteger`, ...), with boxing-free paths. Per ticket 04, `string` conversion is UTF-8 encode/decode at the boundary only, with no hidden semantic switch, and zero-copy `ReadOnlySpan<byte>` access is available.
6. **.NET objects**: The core has only primitives (userdata holding any `object`, host-built metatables, a type-to-metatable registry) and no reflection. Auto-binding is an opt-in separate package, with an attribute-driven source generator preferred over runtime reflection (AOT-safe; whether AOT is supported at all is ticket 10).
7. **Sandbox/limits**: Per-library opt-in at state creation (`io`, `os`, `debug`, `package` and text `load` each removable; no ambient filesystem or process access unless granted). An instruction/time budget uses a cheap volatile flag polled on backward jumps and calls, settable asynchronously, which also provides cross-thread cancellation. An approximate memory budget is counted on table and string allocation. The existing PUC stack and C-level limits apply. All limits are best-effort with no hard real-time guarantee; the interrupt check adds only one volatile read to existing paths.
8. **CLI**: Minimal but PUC-compatible for what the suite needs: `lua [options] [script [args]]` with `-e -l -v -E -W -i -- -`, the `arg` table, `LUA_INIT`, and `LUA_PATH` (`LUA_CPATH` ignored). The REPL is basic: no readline or line editing. Anything the suite or the differential harness doesn't exercise is lowest priority.

Defaults assumed, not separately grilled (revisit when drafting the spec): chunk compilation is `state.Load(source, chunkName, mode, env)` returning a `LuaFunction`, with text chunks only (binary chunks are out of scope). Host-side surfacing of Lua errors is deferred to ticket 08.
