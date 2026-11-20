---
kind: ticket
title: "Perf: string library, concatenation and interning costs"
status: resolved
blocked_by: [40]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T19:45:23Z
---

## Question

Target string-heavy (5.5x) and k-nucleotide (5.6x). Work in src/Lunette/Strings and Lib/StringLib.cs (avoid Interpreter.cs, owned by tickets 41-43 — if a VM change is needed append a note to ticket 43 instead): string.sub/byte/char/find/gsub/format/rep fast paths, span-based matching without intermediate allocations, concat of multiple operands in one allocation, hash computation cost and intern-cache probe cost, LuaString construction. Keep alloc budgets (only result strings allocate); respect LUNETTE_INTERN_CAPACITY/CONSTANT_HASH variants. Use `tdd`; full suite + differential (corpus incl. random/format cases) green. Report before/after via the ticket 40 runner.
Baseline after ticket 40's CallCountingDelayMs=0 change: string-heavy 2.2x, k-nucleotide 3.5x (target <2x/<3x). See `bench/PROFILE.md` for per-construct costs.

## Answer

gsub literal scan, %d format fast path, shared one-byte strings, stored hash in interning, match/find result reuse (38c1227). string-heavy about 1.9x to about 1.7x.
