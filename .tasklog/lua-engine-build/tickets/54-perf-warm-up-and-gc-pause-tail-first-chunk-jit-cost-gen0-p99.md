---
kind: ticket
title: "Perf: warm-up and GC pause tail (first-chunk JIT cost, gen0 p99)"
status: open
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Can the first ~5 chunks (37-57 ms vs 6 ms PUC, 6-9x) be cut via tiering/OSR settings or startup work that stays compatible with the AOT gate, and can gen0 p99 pause (4.2 ms vs 0.23 ms PUC in gc-latency.lua, suspected card-marked old-table scanning) be reduced, without hurting throughput or max pause where Lunette already wins? See bench/COVERAGE.md, bench/PROFILE.md.
