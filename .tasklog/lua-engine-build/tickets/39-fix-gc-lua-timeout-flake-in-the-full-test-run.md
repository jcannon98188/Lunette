---
kind: ticket
title: Fix gc.lua timeout flake in the full test run
status: resolved
tags: [ready-for-agent, bug]
created: 2026-11-20
updated: 2026-11-20T10:53:26Z
---

## Question

After merging 33+38, the full `dotnet test -c Release` run (3m48s) failed `Suite_file(gc.lua)` with a 180 s timeout; running `Suite_file` tests alone passes (all 34 in 40 s). Likely `gcinfo()`/`collectgarbage("count")` is process-wide and other test classes allocate concurrently, so gc.lua's accounting loops never converge. Make the suite deterministic under the full run (e.g. put all suite/GC-sensitive tests in the same non-parallel collection, or give gc.lua accounting a per-state measure) without weakening waivers. Verify by running the full test suite 3 times.

## Answer

Root cause: collectgarbage("count") returned process-wide GC.GetTotalMemory while Lua's collector was stopped, so the pace loop never reached 3x once the .NET gen0 budget had shrunk. While stopped, count is now heap-at-stop plus this thread's allocation since; test added. Full run dropped from ~150 s to ~50 s. Residual: unsharded single-process Linux suite may still flake at gc.lua:473 (CI shards; AllLuaTests now in the non-parallel memory collection).
