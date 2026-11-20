---
kind: ticket
title: Control flow, functions and closures
status: resolved
blocked_by: [1]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:41Z
---

## Question

### What to build

Loops, conditionals, multiple returns, varargs, closures and PUC-style open/closed upvalues over a growable per-Coroutine stack, with Lua-to-Lua calls as heap-allocated Frames (ADR 0002). Starts Tier 1.

### Acceptance criteria

- [ ] `calls.lua`, `closure.lua` and `vararg.lua` pass or carry reason-coded waivers
- [ ] Deep Lua recursion produces no .NET stack overflow
- [ ] Added to the `required-files` ratchet

## Answer

Control flow, functions, closures and varargs implemented in the one-pass compiler and the single-switch VM loop with heap-allocated frames.
