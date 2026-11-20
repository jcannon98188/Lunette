---
kind: ticket
title: "Host debug hook API: LuaState.SetHook with C# delegate and GetInfo for debug adapters"
status: open
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Can the existing debug.sethook machinery (call/return/line/count events in the traced loop, Interpreter.Hooks.cs) and debug.getinfo be exposed as a public C# API (a LuaState.SetHook taking a mask, count and delegate, plus a GetInfo(level) returning a struct with source, line, name, what, locals and upvalues access) that a debug adapter can use without a Lua shim, while keeping hook counts, ftransfer/ntransfer and the official db.lua behavior unchanged, behaving correctly for coroutines and when the hook throws, and leaving the untraced fast loop and alloc/JIT budgets unaffected?
