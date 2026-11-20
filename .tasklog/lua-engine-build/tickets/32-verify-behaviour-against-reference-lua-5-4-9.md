---
kind: ticket
title: Verify behaviour against reference Lua 5.4.9
status: open
blocked_by: [10, 17]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Unverified behaviour recorded by tickets 04, 05 and 21: no reference `lua` binary was available locally.
- `math.random`/`randomseed` output vs the reference for fixed seeds.
- `#t` border results for tables with holes (the unit tests encode PUC results from memory).
- Float formatting (`%.14g`), `string.format` corner cases, `tostring`/`tonumber` edge cases.
Build the reference binary with `ci/build-reference.sh` if a toolchain is available (WSL, MSYS2 gcc/make or similar) and extend the differential tests (ticket 17) with these corpora; otherwise record exact expected values from the reference source (ltests/lmathlib.c xoshiro256**, ltable.c luaH_getn) and test against those.

Acceptance criteria:
- [ ] Differential corpus for math.random, # border, number formatting committed
- [ ] Any mismatches fixed in the engine
- [ ] Where no reference binary could be run, the ticket answer says so and cites the source-derived expectations

Update after ticket 17: a reference 5.4.9 binary was built locally in WSL Ubuntu and the differential tests ran for real. Fixed-seed `math.random`, the `#t` borders tried and `%.14g` formatting already match the reference. Remaining scope for this ticket: widen the corpora (more `#t` hole patterns, `string.format` corner cases, `tostring`/`tonumber` edge cases, more `math.random` ranges and seeds), fix any new mismatches, and fill gaps listed in ticket 35.
