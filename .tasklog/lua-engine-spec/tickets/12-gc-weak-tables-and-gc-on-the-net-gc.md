---
kind: ticket
title: GC, weak tables and __gc on the .NET GC
status: resolved
type: grilling
created: 2026-11-20
updated: 2026-11-20T03:09:51Z
---

## Question

How are `__gc`, weak tables (`__mode` k/v/kv, ephemeron semantics) and `collectgarbage` options implemented on top of the .NET GC, given the agreed best-effort policy (`__close` stays exact)? Decide: weak reference mechanism (WeakReference / DependentHandle for ephemerons / own mark pass); finalizer-queue vs `ConditionalWeakTable`/finalizers and how `__gc` runs on the Lua thread (never on the .NET finalizer thread); resurrection and ordering guarantees; what `collectgarbage("count"/"collect"/"step"/"incremental"/"generational")` report or do; interaction with the 16-byte `LuaValue` and PUC-faithful tables; which suite files (`gc.lua`, `gengc.lua`) need waivers and how they tie into the waiver manifest.

## Answer

Agreed with the user in one grilling round.

**Weak tables.** `__mode` tables are a distinct storage variant chosen at `setmetatable`; strong tables keep the PUC-faithful array/node layout with no added cost. Weak values use WeakReference-style handles; weak keys use `DependentHandle` for ephemeron semantics (value alive only while key is); `kv` uses weak handles for both. Only collectable objects (tables, closures, userdata, threads) are weak; strings, numbers and booleans are values and never removed. Dead entries are purged lazily on `next`, resize and `collectgarbage`. No own tracing mark pass.

**`__gc`.** When `setmetatable` installs a metatable with `__gc`, the object is marked for finalization and gets a finalizable sentinel holding a strong ref back to it. The .NET finalizer thread only enqueues the object on a lock-free queue and never runs Lua. The Lua thread drains the queue at safepoints (allocation-count thresholds, `collectgarbage`, state disposal), running `__gc` as a non-yieldable re-entrant call. Resurrection works naturally; each object is finalized at most once unless `setmetatable` re-marks it (as in PUC). Order within a drained batch is reverse marking order via a sequence number; across batches it is best-effort. A weak registry of marked objects lets `LuaState.Dispose` / `lua_close` run all remaining `__gc` in reverse marking order. `__gc` errors produce a warning, not a propagated error; errors during `Dispose` are swallowed.

**`collectgarbage`.**
- `collect`: `GC.Collect()` (blocking, full), `GC.WaitForPendingFinalizers()`, drain the `__gc` queue on the calling thread, repeat once for resurrected objects; returns 0.
- `count`: managed heap estimate in KB as a float (`GC.GetTotalMemory(false)`; process-wide, not per-state exact).
- `step`: gen0/gen1 collect plus drain; returns true.
- `stop`/`restart`: toggle only the automatic safepoint drain; `isrunning` reflects it.
- `incremental`/`generational`: record the mode, return the previous mode name; otherwise no-ops.
- `setpause`/`setstepmul`: store and return the previous values; otherwise no-ops.

**Waivers.** `gc.lua` runs with per-assertion overlays waiving exact-count, timing and precise-cycle-collection asserts (reason code e.g. `GC-NONDETERMINISTIC`); weak-table, `__gc` ordering and resurrection checks that `collect` + drain makes deterministic must pass. `gengc.lua` is waived whole (`GC-GENERATIONAL-MODE`). Any other file depending on exact `collectgarbage("count")` gets an overlay, cross-referenced in the manifest. An engine-owned test file outside the official suite checks deterministic weak-table clearing and `__gc` ordering in common cases.
