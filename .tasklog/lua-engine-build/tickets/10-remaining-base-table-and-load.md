---
kind: ticket
title: Remaining base, table and load
status: resolved
blocked_by: [6, 8]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:47Z
---

## Question

### What to build

`table.sort`/`move`/`concat`/`insert`/`remove`/`unpack`, text-only `load`/`loadfile`/`dofile`/`require`, `package`, and the remaining base functions. Precompiled binary chunks and `string.dump` are out of scope.

### Acceptance criteria

- [ ] `sort.lua`, `literals.lua` and the applicable parts of the remaining suite files that need only base/table/load pass or carry reason-coded waivers
- [ ] Added to the `required-files` ratchet

## Answer

Remaining base functions, table library and load/loadstring-style chunk loading implemented (no binary chunks: string.dump unsupported, waived with reason no-bytecode).
