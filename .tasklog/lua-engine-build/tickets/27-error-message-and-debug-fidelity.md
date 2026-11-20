---
kind: ticket
title: Error message and debug fidelity
status: open
blocked_by: [10]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Known gaps from tickets 07 and 11, to match PUC 5.4 messages and debug behaviour where the suite or users observe them:
- "calling 'f' on bad self" and function-name lookup at the call site; argument errors currently use hardcoded function names.
- Stale line numbers when an error comes from a builtin called through a metamethod (frame pc saved only at call sites).
- A stack overflow on a `pcall` target's very first frame is not caught by that `pcall`.
- `debug.traceback` lacks the base-level `[C]: in ?` entry; `debug.getlocal` for temporaries is approximate.
- `getuservalue`/`setuservalue` are stubs although userdata now exists.
- A hook set inside a nested `Run` does not affect outer fast loops until they re-enter; builtin call/return hook events fire only for builtins called from Lua.
- `warn` must write through `state.Gc.Warn`.

Acceptance criteria:
- [ ] Each item above fixed or explicitly documented as an intentional, reason-coded waiver
- [ ] errors.lua, db.lua and calls.lua suite assertions touching these pass (or carry reason-coded overlays)
- [ ] Tests for each
