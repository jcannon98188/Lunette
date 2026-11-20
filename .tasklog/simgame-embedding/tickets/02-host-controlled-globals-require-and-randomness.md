---
kind: ticket
title: Host-controlled globals, require and randomness
status: open
type: task
created: 2026-11-20
updated: 2026-11-20
---

## Question

SimGame gives each mod pack its own state with deny-by-default globals. Requirements:

1. **Deny by default.** A state can be created with only an explicit allowlist of standard libraries and base functions; anything not listed is absent, not merely removable afterwards.
2. **Gated functions.** `dofile`, `loadfile`, `load` (text and binary chunks), `collectgarbage` and `coroutine` can each be withheld or replaced by the host independently.
3. **Host-seeded `require`.** `require` resolves only names the host registers (SimGame registers closed `simgame.<area>` modules per granted capability); it never touches the filesystem or `package`.
4. **Replaceable `math.random`.** The host seeds the generator per state, or replaces `math.random`/`math.randomseed`, so streams are deterministic and loggable; no clock seeding.

Source: SimGame foundation-spec "Decide the Mod API surface", decisions 7 and 13.
