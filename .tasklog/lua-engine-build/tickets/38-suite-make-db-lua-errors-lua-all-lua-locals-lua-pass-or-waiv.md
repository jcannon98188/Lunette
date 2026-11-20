---
kind: ticket
title: "Suite: make db.lua, errors.lua, all.lua, locals.lua pass or waive"
status: resolved
tags: [ready-for-agent, bug]
created: 2026-11-20
updated: 2026-11-20T10:53:24Z
---

## Question

From ticket 13's suite run on the integration branch (Windows harness): `db.lua` fails at line 25 ("wrong trace!!"); `errors.lua` fails at line 30 and also uses `string.dump` at ~280/287 (needs no-bytecode line-range waivers); `all.lua` fails at `string.dump` line ~150 (waive, reason no-bytecode, or equivalent); `locals.lua` fails with "attempt to yield across a C-call boundary" (check after ticket 26 landed; may overlap ticket 33). Fix the engine where the behavior is a real divergence, add reason-coded waivers only for genuinely out-of-scope items (string.dump etc.), and add each passing file to `tests/suite/required-files.txt`. Verify against the reference 5.4.9 (WSL Ubuntu, `LUNETTE_REFERENCE_LUA`) when in doubt.

## Answer

db.lua, errors.lua, locals.lua, all.lua pass and are required; waivers for string.dump (no-bytecode) in errors.lua 279-289 and all.lua 150-151; compiler-limit errors, raw-token syntax errors, tracebacks, debug fixes, yieldable close handlers.
