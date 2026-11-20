---
kind: ticket
title: Fix differential-test divergences from reference 5.4.9
status: open
blocked_by: [17]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Ticket 17's differential run against a real Lua 5.4.9 found divergences, currently isolated in `tests/differential/corpus/known-*.lua` with `bug-wontfix` waivers in `tests/differential/waivers.toml` (expiring 2027-06-01). Fix each in the engine and remove its waiver:
- builtin argument errors omit the caller `file:line:` prefix (most string builtins)
- non-numeric string in arithmetic: different message text
- message text for `ipairs()` (no argument), `break` outside a loop, `string.format` width > 99, `table.concat` invalid value, parser unclosed-block error
- `math.randomseed(1.5)` accepted by the engine; reference raises an error
- `math.random(2, 1)`: reference blames argument #1, the engine #2
- NaN sign: `fmod(inf, 1)` prints `nan`, reference prints `-nan` (match the reference's printf behaviour for NaN sign on Linux x64)
- CLI mangles bytes above 127 on output (see also ticket 28, which owns the raw-byte output fix; coordinate, do not duplicate)

A WSL Ubuntu is available on the dev machine for building the reference binary (`wsl` + `make linux` from the checksum-verified tarball); the differential tests skip when no reference binary is present and `LUNETTE_REQUIRE_REFERENCE=1` forces them.

Acceptance criteria:
- [ ] Every waiver in tests/differential/waivers.toml removed by fixing the engine, or re-justified with a real reason and ticket
- [ ] known-*.lua files merged into the normal corpus
- [ ] Differential tests (60 + generated seeds) pass with zero waivers other than justified ones
- [ ] Generator extended to cover the previously avoided areas (string builtin arguments, strings in arithmetic, NaN)
