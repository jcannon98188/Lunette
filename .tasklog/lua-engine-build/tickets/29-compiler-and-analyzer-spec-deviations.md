---
kind: ticket
title: Compiler and analyzer spec deviations
status: resolved
blocked_by: [10]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:53:13Z
---

## Question

Known deviations from tickets 09, 11 and 07 to close:
- `<const>` locals occupy a register; fold compile-time constants away as the spec describes.
- No analyzer enforces "no `try` in the dispatch method"; add one (the `try` must stay in `Interpreter.Run`, outside `RunLoop`) with positive and negative analyzer tests.
- The hook-free generic loop (`RunLoop<TFast/TTraced>`) is not locked in by any test: add a JIT-level or IL-level test showing the fast variant has no hook checks.
- The `LUA_SAFE_ACCESS` define and any other spec'd build variants not yet present (check spec tickets 17/18 against the tree).

Acceptance criteria:
- [ ] const folding implemented with tests
- [ ] try-in-dispatch analyzer with canary coverage
- [ ] Test proving the fast loop variant has no hook checks
- [ ] Any missing spec'd build variants added or listed as explicit waivers in the ticket answer

## Answer

Single-name literal const folding, LD006 analyzer (no try/using/lock in [LuaInterpreterLoop]), canary repair, fast-loop no-hook-check JIT test. Explicit waivers: multi-name const lists not folded; LUA_SAFE_ACCESS safe-twin variant not built (no unsafe helpers exist; documented in ci.yml/nightly-variants.yml and asserted by ci/nightly-variants.test.mjs).
