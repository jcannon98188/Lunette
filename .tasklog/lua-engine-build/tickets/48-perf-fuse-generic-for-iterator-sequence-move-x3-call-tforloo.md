---
kind: ticket
title: "Perf: fuse generic-for iterator sequence (Move x3 + Call + TForLoop)"
status: open
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Can the generic-for per-iteration sequence be fused for next/ipairs iterators while keeping "for iterator" call-site naming in errors and the hook instruction counts / ftransfer behavior that the official suite checks (db.lua, errors.lua, calls.lua)?
