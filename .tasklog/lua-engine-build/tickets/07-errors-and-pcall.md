---
kind: ticket
title: Errors and pcall
status: resolved
blocked_by: [4]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:45Z
---

## Question

### What to build

Pending-state error handling with in-loop unwinding and no exceptions on common paths (ADR 0003), PUC-literal message text, `error`/`assert`/`pcall`/`xpcall`, and a Lua stack-overflow error for runaway recursion.

### Acceptance criteria

- [ ] `errors.lua` passes or carries reason-coded waivers
- [ ] Error messages match the Reference binary on differential samples
- [ ] Added to the `required-files` ratchet

## Answer

error/pcall/xpcall with pending-state unwinding (ADR 0003) implemented; reference-matching messages refined in 27/35.
