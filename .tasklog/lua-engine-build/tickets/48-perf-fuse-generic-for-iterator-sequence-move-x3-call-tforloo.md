---
kind: ticket
title: "Perf: fuse generic-for iterator sequence (Move x3 + Call + TForLoop)"
status: resolved
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T20:24:08Z
---

## Question

Can the generic-for per-iteration sequence be fused for next/ipairs iterators while keeping "for iterator" call-site naming in errors and the hook instruction counts / ftransfer behavior that the official suite checks (db.lua, errors.lua, calls.lua)?

## Answer

New TForMove opcode fuses the generic-for step for next and the ipairs iterator in the untraced loop (8da5fcc, merge f903a1b). Anything unusual, traced mode, other iterators, to-be-closed value and yields fall back to the ordinary five-instruction sequence, so error naming, hook counts and ftransfer/ntransfer are unchanged. pairs/ipairs micro loop 995 to 601 ms; macro benchmarks neutral to slightly faster. Full suite 1048 passed, intern variants and safe twin green. Only next and ipairs are fused; closure and __pairs iterators are not (ticket 55).
