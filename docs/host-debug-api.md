# Host debug API

`LuaState.SetHook`, `TryGetInfo`, `TryGetLocal`/`TrySetLocal` and `TryGetUpvalue`/`TrySetUpvalue` give a debug adapter, profiler or
coverage tool the machinery behind `debug.sethook` and `debug.getinfo` without a Lua shim. They add no dispatch loop: a host hook is
installed in the same per-coroutine slot as a Lua hook and runs through the same traced loop, so event order, count periods and
`ftransfer`/`ntransfer` are the debug library's, and a state with no hook still runs the untraced fast loop.

```csharp
state.SetHook((s, ev, line) =>
{
    if (ev == LuaHookEvent.Line && s.TryGetInfo(0, out var info))
        Console.WriteLine($"{info.ShortSource}:{line} in {info.Name ?? info.What}");
}, LuaHookMask.Line);
```

## Rules

- **Mask and count.** `LuaHookMask` is `Call | Return | Line`; a `count` above zero adds a count event every `count` instructions. A null
  hook, or an empty mask with count 0, clears the hook.
- **Per coroutine.** The `thread` argument (a `LuaValue` of type Thread, nil meaning the running coroutine) selects whose hook is set or
  whose stack is read. Coroutines created later start without a hook, as in the debug library. `LuaState.RunningThread` tells a hook
  which coroutine it fired on.
- **One hook per coroutine.** A host hook and a Lua hook share the slot: whichever is set last replaces the other. `debug.gethook`
  reports a host hook as an opaque function with its mask and count.
- **Levels.** Level 0 is the innermost function. Inside a hook that is the function the event is about, including a host function for a
  call or return event (`What == "C"`). Called from a host function that Lua called, level 0 is its Lua caller. On a suspended coroutine,
  level 0 is the `yield` that suspended it. `TryGetInfo` returns false past the bottom; `TryGetLocal` indexes from 1 (negative for varargs)
  and returns false when there is no such local.
- **Not re-entrant.** While the hook runs, hooks are off: Lua code and `state.Call` made from the hook raise no events.
- **Errors.** A `LuaException` thrown by the hook is raised at the hooked point as an ordinary Lua error, like an error thrown by a Lua
  hook (a script can `pcall` it); the hook stays installed. A budget or memory abort propagates as usual. Any other exception
  propagates to the host caller of the running call; the state stays usable after the hook is cleared, but the interrupted script is
  gone.
- **Budgets and interrupts.** The hook costs the script's instruction budget like any other code; `RequestInterrupt` (also from inside the
  hook) still ends a hooked loop.
- **Threading.** Everything except `RequestInterrupt` must be called on the thread that runs the state.
- **Setting from a host function.** The hook takes effect when that host function returns to Lua.
