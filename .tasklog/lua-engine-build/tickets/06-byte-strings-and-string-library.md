---
kind: ticket
title: Byte strings and string library
status: resolved
blocked_by: [4, 5]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:44Z
---

## Question

### What to build

Lua strings as single-allocation headered `byte[]`, the best-effort Intern cache (ADR 0005: 40-byte cut-off, 4-way set-associative, seeded hash, pointer-first equality), and the string library: `format`, patterns, `gsub`, `pack`/`unpack`, with no intermediate allocations. Plus `utf8`.

### Acceptance criteria

- [ ] `strings.lua`, `pm.lua`, `tpack.lua` and `utf8.lua` pass or carry reason-coded waivers
- [ ] Suite still passes with a capacity-1 intern cache and a constant hash
- [ ] Added to the `required-files` ratchet

## Answer

Byte strings (LuaString), best-effort interning (ADR 0005) and the full string library incl. patterns, format, pack/unpack implemented.
