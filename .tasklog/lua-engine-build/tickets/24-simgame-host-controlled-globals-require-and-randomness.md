---
kind: ticket
title: "SimGame: host-controlled globals, require and randomness"
status: resolved
blocked_by: [10, 14]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:53:05Z
---

## Question

### What to build

Requirements from the SimGame embedder, which gives each mod pack its own state with deny-by-default globals:

1. **Deny by default.** A state can be created with only an explicit allowlist of standard libraries and base functions; anything not listed is absent, not merely removable afterwards.
2. **Gated functions.** `dofile`, `loadfile`, `load` (text and binary chunks), `collectgarbage` and `coroutine` can each be withheld or replaced by the host independently.
3. **Host-seeded `require`.** `require` resolves only names the host registers (SimGame registers closed `simgame.<area>` modules per granted capability); it never touches the filesystem or `package`.
4. **Replaceable `math.random`.** The host seeds the generator per state, or replaces `math.random`/`math.randomseed`, so streams are deterministic and loggable; no clock seeding.

### Acceptance criteria

- [ ] State created from an allowlist exposes nothing else
- [ ] Each gated function can be withheld or replaced independently
- [ ] require resolves only host-registered modules, no filesystem/package access
- [ ] math.random deterministic per host seed; replaceable
- [ ] Tests for each path above

## Answer

SimGame: host-controlled globals, require and seeded randomness via LuaStateOptions allowlist; withheld libs no longer constructed (34).
