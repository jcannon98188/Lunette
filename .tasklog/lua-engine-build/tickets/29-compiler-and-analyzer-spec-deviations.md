---
kind: ticket
title: Compiler and analyzer spec deviations
status: open
blocked_by: [10]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
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
