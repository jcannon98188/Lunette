---
kind: ticket
title: Walking skeleton
status: resolved
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:40Z
---

## Question

### What to build

The thinnest end-to-end path: a script file run through the CLI that does integer and float arithmetic and calls `print`. Covers solution layout (core assembly, Cli, Analyzers, Interop satellites; net11.0 only), the 16-byte `LuaValue`, a minimal one-pass compiler emitting register bytecode into a `Proto`, a single-`switch` VM loop, and a Linux CI build behind one aggregate `ci-gate` required check. Follow ADRs 0001 and 0004 and the glossary.

### Acceptance criteria

- [ ] CLI runs a script file and prints the result of integer/float arithmetic
- [ ] Solution layout matches the single-core-assembly decision
- [ ] CI builds on Linux and exposes a single `ci-gate` check

## Answer

Implemented on integration branch `lua-engine-build`: solution skeleton (Lunette core, CLI, Interop, Analyzers, tests), 16-byte LuaValue, end-to-end compile+run of a trivial chunk, CI skeleton with the ci-gate aggregate.
