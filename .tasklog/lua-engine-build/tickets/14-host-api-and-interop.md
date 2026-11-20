---
kind: ticket
title: Host API and interop
status: resolved
blocked_by: [8]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:53Z
---

## Question

### What to build

The idiomatic C# host API over `LuaValue`: single-threaded `LuaState`, span-based host functions, opt-in yield and continuations, best-effort sandbox limits (instruction, memory, stack depth), no reflection in the core, UTF-8 interop helpers (boundary only, not a semantic switch), and a PublicApiAnalyzers baseline.

### Acceptance criteria

- [ ] Embedding tests load and call chunks and exercise host functions, yields and sandbox limits through the chunk seam
- [ ] Public surface is tracked by PublicApiAnalyzers
- [ ] A script exceeding a configured limit fails with a Lua error

## Answer

Host API implemented: LuaState.Load/Call, LuaLimits, LuaAbortException, LuaStateOptions allowlist, UTF-8 interop helpers in core (Lunette.Interop remains a placeholder satellite per ADR 0004).
