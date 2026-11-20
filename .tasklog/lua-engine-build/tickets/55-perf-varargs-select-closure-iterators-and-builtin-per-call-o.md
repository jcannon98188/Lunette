---
kind: ticket
title: "Perf: varargs/select, closure iterators and builtin per-call overhead (unpack, tonumber, string.rep/sub/byte)"
status: resolved
blocked_by: [48]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T22:11:10Z
---

## Question

Can select('#', ...), select(n, ...), 3-layer vararg forwarding (2.5-3x), closure-built iterators in generic for (2.9-3.2x, after ticket 48) and per-call builtin overhead in table.unpack-with-range, tonumber and string.rep/sub/byte (2.2-3x) be brought under ~2.5x PUC via in-place builtin paths and cheaper vararg handling? See bench/COVERAGE.md.

## Answer

Root cause: builtins in tail position (return select('#', ...), return select(n, ...)) bypassed the in-place CallFast protocol from ticket 47. Kept (915d09a): CallFast for tail-called builtins, in-place table.unpack (plain table, up to 64 values; table-lib section 251 to 135 ms, 3.2x to 1.7x PUC), in-place tonumber(x) (about 1.9-2.1x), math.max/min with 2 numbers, scalar copies in Vararg for up to 4 values (forwarding 2.8x to 2.6x). Reverted with no measured win: closure-iterator fusion inside TForMove, dedicated PrecallVararg. string.sub/rep/char, table.insert/remove and tostring already measure 0.55-1.6x; the earlier 2.5x string reading looks like first-section warm-up (not re-measured). Closure iterators (2.7-2.9x), select (2.7x), forwarding (2.6x) and multiple returns (2.2x) stay bound by the general Lua call/return cost, same as fib. Tests in Ticket55FastPathTests.cs with values from PUC; suite, constant-hash and full safe twin pass; intern-capacity variant had one flaky failure then passed; gc.lua:473 differential fails identically on the base build.
