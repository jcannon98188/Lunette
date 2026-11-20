---
kind: ticket
title: "Perf: VM runtime and string error raising 2.5-8x PUC (message building)"
status: open
blocked_by: [48, 51]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Why do VM-raised runtime errors (6-8x) and caught error("str") (2.5-5.6x) cost far more than table errors and deep unwinding (1.0x) in pcall-error.lua, and can position lookup, VarInfo.Describe and string conversion be made cheaper or lazier without changing any message text? See bench/COVERAGE.md.
