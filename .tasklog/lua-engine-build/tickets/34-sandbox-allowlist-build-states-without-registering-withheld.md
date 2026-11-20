---
kind: ticket
title: "Sandbox allowlist: build states without registering withheld libraries"
status: resolved
blocked_by: [13]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:53:19Z
---

## Question

Gap from ticket 24: `new LuaState(LuaStateOptions)` constructs the full standard state and then removes whatever isn't allowlisted. Observable Lua behaviour is the same, but the requirement is that withheld names are absent, not removed afterwards, and a hard sandbox guarantee should not depend on removal being complete.
- Skip registration of non-allowlisted libraries/functions at construction (including string metatable/methods, `package`, `debug`, io/os once ticket 13 lands), so no reference to them is reachable from the new state (registry, `package.loaded`, upvalues, per-type metatables).
- Add a test that enumerates everything reachable from the globals, registry and per-type metatables of a minimal-allowlist state and asserts only allowlisted functions appear.
- `dofile`/`loadfile`, if allowlisted, should go through the platform interface (ticket 13) so the host can confine or replace file access; document this in the host API docs.
- `load` binary-chunk mode: confirm `load` rejects binary chunks (no bytecode support) with a clear error in every state, and say so in the docs.

Acceptance criteria:
- [ ] Withheld libraries are never constructed or registered
- [ ] Reachability test over globals, registry and metatables passes for a minimal state
- [ ] dofile/loadfile use the platform interface; documented
- [ ] load rejects binary chunks with a clear error; tested

## Answer

Withheld libraries and base functions are never constructed in an allowlisted state (LuaState.Wants/Define); reachability test; optional LuaStateOptions.Platform. Leftovers: function objects for withheld base functions are allocated then dropped; with require allowlisted the standard require is built then replaced.
