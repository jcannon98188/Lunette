---
kind: ticket
title: Fix DoString inside a host function running under require
status: open
blocked_by: [13]
tags: [ready-for-agent, bug]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Found by ticket 25: calling `DoString` from inside a host function that is itself running under `require` throws `IndexOutOfRangeException` in the interpreter loop. Reproduce with a failing test (host function registered via the public API, invoked from a Lua chunk loaded through `require`, which calls `state.DoString`), fix the frame/stack handling so nested host re-entry works, and add the test. Ticket 25's test currently avoids this path; restore it once fixed.
