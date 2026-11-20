---
kind: ticket
title: Safe-twin build (LUA_SAFE_ACCESS) for Lunette.Unsafe helpers and nightly variant
status: resolved
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T19:45:25Z
---

## Question

Ticket 29's LUA_SAFE_ACCESS safe-twin waiver was justified by "no unsafe helpers exist". Ticket 41 introduced `src/Lunette/Unsafe/Regs.cs` (barrier-free numeric stores etc.), so the spec'd safe twin is now owed. Implement: a `LUA_SAFE_ACCESS` compile-time define that swaps every `Lunette.Unsafe.*` helper for a bounds-checked, barrier-correct safe implementation with identical behavior (the unsafe family must be the only place unsafe/Unsafe.* is used; confirm via analyzer LD-rule or a test); run the full suite + differential under the define; add the nightly `safe-twin` job to `.github/workflows/nightly-variants.yml` (never in ci-gate), replace the documented no-op comment and update `ci/nightly-variants.test.mjs`; document the unsafe helpers' invariants (why barrier-free numeric stores are sound with respect to ADR 0001/0006) next to the code. Verify locally with `dotnet test -c Release -p:DefineConstants=LUA_SAFE_ACCESS` or the project's mechanism; Actions can't run locally.

## Answer

RegsSafe twin selected by -p:LuaSafeAccess=true, with a nightly safe-twin CI job and tests (0f80501).
