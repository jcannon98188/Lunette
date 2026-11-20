---
kind: ticket
title: "Perf: table.insert/remove/move bulk copies and more in-place library forms"
status: open
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Can table.insert(t,pos,v), table.remove(t,pos) and table.move use bulk array copies (front insert/remove of 2e4 from 1062-1677 ms to about 70 ms; prove the _limit/Length() invariant), and which of math.type/tointeger/fmod/random, utf8.*, string.byte range deserve in-place CallFast forms (gaps 1.8-3.2x)? Source: audit-libs.md candidates 7 and 12.
