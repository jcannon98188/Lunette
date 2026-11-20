---
kind: ticket
title: "Perf: metamethod function calls (__index chains, __newindex, __call) 3-5.7x PUC"
status: open
blocked_by: [48, 51]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Can __index-chain method calls, __newindex function proxies and __call objects be made cheaper (push a frame instead of a nested interpreter loop in Meta.CallMeta, cache absent-metamethod flags, inline-cache chain walks) to get oop.lua and the per-construct probes under ~2.5x PUC? See bench/COVERAGE.md.
