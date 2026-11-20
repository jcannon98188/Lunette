---
kind: ticket
title: debug library and hooks
status: resolved
blocked_by: [8, 9]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:49Z
---

## Question

### What to build

The full `debug` library including hooks, with two source-generated VM loop variants (with and without hook checks) so hook cost stays contained.

### Acceptance criteria

- [ ] `db.lua` passes or carries reason-coded waivers
- [ ] Non-hooked variant carries no hook-check code in its loop
- [ ] Added to the `required-files` ratchet

## Answer

debug library and hooks implemented with generic RunLoop<TFast/TTraced> variants; JIT probe test asserts the fast variant has no hook checks. Known gap: a hook set inside a builtin invoked as a metamethod from a non-call instruction takes effect when the outer loop re-enters; debug.getlocal for temporaries unchanged.
