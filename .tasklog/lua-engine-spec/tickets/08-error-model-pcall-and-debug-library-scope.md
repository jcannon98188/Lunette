---
kind: ticket
title: Error model, pcall and debug library scope
status: open
type: grilling
blocked_by: [6]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Decide how Lua errors are represented and propagated on top of the chosen execution model (exceptions vs result codes, cost of `pcall` on the hot path), error values and messages matching the reference (the suite checks message text), tracebacks and line info, and how much of the `debug` library to support (hooks, `getinfo`, `sethook`, `getlocal`) given what the suite exercises.
