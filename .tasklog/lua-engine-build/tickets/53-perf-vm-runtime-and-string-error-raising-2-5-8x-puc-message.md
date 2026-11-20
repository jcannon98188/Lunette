---
kind: ticket
title: "Perf: VM runtime and string error raising 2.5-8x PUC (message building)"
status: resolved
blocked_by: [48, 51]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T20:41:57Z
---

## Question

Why do VM-raised runtime errors (6-8x) and caught error("str") (2.5-5.6x) cost far more than table errors and deep unwinding (1.0x) in pcall-error.lua, and can position lookup, VarInfo.Describe and string conversion be made cheaper or lazier without changing any message text? See bench/COVERAGE.md.

## Answer

The 6-8x on VM-raised errors was the .NET throw/catch (about 2.3 us each), not message building. Index, arithmetic and call errors are now unwound in the loop (RaiseInLoop/RaiseDeferred) with the same exception thrown when no protected frame exists, so Traceback/TraceEntries are unchanged; error("str") builds the positioned message as bytes with cached chunk display (merge 202df0d). pcall-error section times: error("str") 131-142 to 75-83 ms (PUC 38-44), VM runtime error 295-360 to 138-147 ms (PUC 48-69); 300k steady-state pcall(vm_error) 790-880 to 110 ms. 36 characterization tests pin messages and tracebacks. Compare, concat, bitwise, length and tail-call 'attempt to call' errors still throw. About 90 ms of the remaining section-4 time is tier-0 warm-up (ticket 54). The differential and all.lua runs passed before the final merge, not after.
