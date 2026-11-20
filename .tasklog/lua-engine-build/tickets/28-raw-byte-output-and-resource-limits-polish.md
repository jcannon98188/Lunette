---
kind: ticket
title: Raw byte output and resource limits polish
status: resolved
blocked_by: [10]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:53:11Z
---

## Question

Known gaps from tickets 06, 14 and 23:
- `print` writes through a `TextWriter`, so non-UTF-8 bytes print as U+FFFD. Output must preserve raw bytes (CLI stdout and host-supplied sinks).
- Memory cap is checked only at poll points, so one huge library call (`string.rep`, `table.concat`, `string.format`) can overshoot before the abort fires. Pre-check requested sizes against the remaining budget in allocating library functions.
- Instruction budget counts backward jumps and Lua calls, not instructions. Decide and document the unit; if not exact, document the bound precisely in the host API docs and test it.
- `LuaState.Dispose` should run pending `__close` handlers of a cleanly finished state where the manual requires, and stay idempotent.

Acceptance criteria:
- [ ] print/io.write round-trip arbitrary bytes through the CLI (tested with a non-UTF-8 byte sequence)
- [ ] Huge single library allocations abort at the cap before allocating
- [ ] Budget unit documented with a test pinning the bound
- [ ] Dispose semantics documented and tested

## Answer

Raw byte output (LuaState.OutputStream), CheckAlloc pre-checks for string.rep/table.concat/table.unpack/string.format/gsub, documented budget unit and Dispose semantics; non-ASCII differential waiver removed. Gap: io.stderr through CLI with a TextWriter overload still decodes as UTF-8 (real Main uses raw stderr).
