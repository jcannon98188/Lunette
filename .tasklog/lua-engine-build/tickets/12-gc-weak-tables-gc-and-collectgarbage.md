---
kind: ticket
title: "GC: weak tables, __gc and collectgarbage"
status: open
blocked_by: [8, 9]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

### What to build

Weak and ephemeron tables as a separate storage variant on `WeakReference`/`DependentHandle`, `__gc` via a finalizable sentinel plus a Lua-thread drain queue (reverse-mark order per batch, registry for `IDisposable`), and best-effort `collectgarbage` mapped onto the .NET GC.

### Acceptance criteria

- [ ] `gc.lua` passes with per-assertion overlays recorded in the manifest; `gengc.lua` waived whole
- [ ] Added to the `required-files` ratchet
