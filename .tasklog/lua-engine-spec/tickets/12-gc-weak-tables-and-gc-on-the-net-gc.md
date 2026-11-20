---
kind: ticket
title: GC, weak tables and __gc on the .NET GC
status: open
type: grilling
created: 2026-11-20
updated: 2026-11-20
---

## Question

How are `__gc`, weak tables (`__mode` k/v/kv, ephemeron semantics) and `collectgarbage` options implemented on top of the .NET GC, given the agreed best-effort policy (`__close` stays exact)? Decide: weak reference mechanism (WeakReference / DependentHandle for ephemerons / own mark pass); finalizer-queue vs `ConditionalWeakTable`/finalizers and how `__gc` runs on the Lua thread (never on the .NET finalizer thread); resurrection and ordering guarantees; what `collectgarbage("count"/"collect"/"step"/"incremental"/"generational")` report or do; interaction with the 16-byte `LuaValue` and PUC-faithful tables; which suite files (`gc.lua`, `gengc.lua`) need waivers and how they tie into the waiver manifest.
