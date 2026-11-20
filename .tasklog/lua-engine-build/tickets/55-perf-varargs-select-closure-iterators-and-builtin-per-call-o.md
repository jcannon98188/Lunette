---
kind: ticket
title: "Perf: varargs/select, closure iterators and builtin per-call overhead (unpack, tonumber, string.rep/sub/byte)"
status: open
blocked_by: [48]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Can select('#', ...), select(n, ...), 3-layer vararg forwarding (2.5-3x), closure-built iterators in generic for (2.9-3.2x, after ticket 48) and per-call builtin overhead in table.unpack-with-range, tonumber and string.rep/sub/byte (2.2-3x) be brought under ~2.5x PUC via in-place builtin paths and cheaper vararg handling? See bench/COVERAGE.md.
