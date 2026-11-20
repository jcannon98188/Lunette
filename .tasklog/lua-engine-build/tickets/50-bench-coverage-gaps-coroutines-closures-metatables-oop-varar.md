---
kind: ticket
title: "Bench: coverage gaps (coroutines, closures, metatables/OOP, varargs, pcall/error, GC latency, strings, table lib, startup, footprint)"
status: resolved
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T20:20:00Z
---

## Question

Where does Lunette lag native PUC (and Lua-in-WASM) on common scripting patterns the current macro set does not cover: coroutine switching, closures/upvalues, metatable/OOP dispatch, varargs/multiple returns, pcall/error, GC pause latency and footprint, string building, table library, warm-up and many-small-states? File a ticket for each gap larger than about 2.5x.

## Answer

Added nine macro benchmarks (coroutines, closures, oop, varargs, pcall-error, gc-latency, string-build, table-lib, warmup), a many-small-states harness (StateBenchmarks.cs, states-puc.c) and bench/COVERAGE.md (0d9ddbc, merge 239d29d). Whole scripts are 0.8x-2.3x PUC with no whole-script gap over 2.5x; per-construct gaps found: metamethod function calls 3.1-5.7x, VM/string error raising 2.5-8x, warm-up of first chunks 6-9x, GC batch p99 about 18x, closure iterators 2.9-3.2x, select/vararg forwarding 2.5-3x, unpack/tonumber/string.rep/sub/byte 2.2-3x. Coroutines are 3.6-6x faster than Lua-in-WASM. Follow-up perf tickets filed.
