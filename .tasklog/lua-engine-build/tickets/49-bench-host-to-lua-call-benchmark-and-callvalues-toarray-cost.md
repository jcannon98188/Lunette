---
kind: ticket
title: "Bench: host-to-Lua call benchmark and CallValues ToArray cost"
status: open
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

What does a host calling a Lua function repeatedly (embedder per-frame pattern, args and results via CallValues) cost versus PUC called from C, and does removing the CallValues .ToArray() allocation measurably help, within the alloc budgets?
