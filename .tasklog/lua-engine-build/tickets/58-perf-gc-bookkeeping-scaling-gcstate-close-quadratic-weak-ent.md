---
kind: ticket
title: "Perf: GC bookkeeping scaling (GcState.Close quadratic, weak entries via GCHandle)"
status: open
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Can GcState.Close use a HashSet (exit with 100k live __gc objects currently 15.4 s vs PUC 23 ms) and weak-table entries use GCHandle structs instead of a finalizable WeakReference each (weak collects 30-43% faster), within ADR 0006 and without changing __gc ordering or gc.lua results? Run gc.lua on the unmodified tree first because gc.lua:473 is load-sensitive. Source: audit-libs.md candidate 3.
