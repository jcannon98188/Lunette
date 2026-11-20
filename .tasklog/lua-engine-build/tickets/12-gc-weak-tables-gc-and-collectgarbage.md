---
kind: ticket
title: "GC: weak tables, __gc and collectgarbage"
status: resolved
blocked_by: [8, 9]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:50Z
---

## Question

### What to build

Weak and ephemeron tables as a separate storage variant on `WeakReference`/`DependentHandle`, `__gc` via a finalizable sentinel plus a Lua-thread drain queue (reverse-mark order per batch, registry for `IDisposable`), and best-effort `collectgarbage` mapped onto the .NET GC.

### Acceptance criteria

- [ ] `gc.lua` passes with per-assertion overlays recorded in the manifest; `gengc.lua` waived whole
- [ ] Added to the `required-files` ratchet

## Answer

Weak tables, __gc finalizers and collectgarbage implemented (ADR 0006: weak tables weakened only in collection windows); suite gaps closed in 33.
