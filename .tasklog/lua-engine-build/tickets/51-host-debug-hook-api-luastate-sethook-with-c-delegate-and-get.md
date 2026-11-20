---
kind: ticket
title: "Host debug hook API: LuaState.SetHook with C# delegate and GetInfo for debug adapters"
status: resolved
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T20:39:20Z
---

## Question

Can the existing debug.sethook machinery (call/return/line/count events in the traced loop, Interpreter.Hooks.cs) and debug.getinfo be exposed as a public C# API (a LuaState.SetHook taking a mask, count and delegate, plus a GetInfo(level) returning a struct with source, line, name, what, locals and upvalues access) that a debug adapter can use without a Lua shim, while keeping hook counts, ftransfer/ntransfer and the official db.lua behavior unchanged, behaving correctly for coroutines and when the hook throws, and leaving the untraced fast loop and alloc/JIT budgets unaffected?

## Answer

Public host debug API in src/Lunette/Host/LuaState.Debug.cs (1b8db4c, merge 6fdc348): LuaHookMask, LuaHookEvent, LuaHookCallback, LuaDebugInfo, and LuaState.SetHook, TryGetInfo, TryGetLocal/TrySetLocal, TryGetUpvalue/TrySetUpvalue, RunningThread. It reuses the existing traced loop, so hook counts and ftransfer/ntransfer are unchanged; docs in docs/host-debug-api.md. A host hook and debug.sethook share one slot per coroutine (last set wins). A throwing LuaException hook surfaces as a catchable Lua error; other exceptions propagate to the host. Same-thread only (documented, not enforced). 22 new tests; full suite 1070 passed. Not exposed: GetHook, ActiveLines, ftransfer/ntransfer. The LuaSafeAccess variant was only run on a subset after the merge (first full run hung).
