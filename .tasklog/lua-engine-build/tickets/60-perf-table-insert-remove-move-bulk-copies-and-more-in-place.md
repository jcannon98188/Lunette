---
kind: ticket
title: "Perf: table.insert/remove/move bulk copies and more in-place library forms"
status: resolved
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-21T02:33:42Z
---

## Question

Can table.insert(t,pos,v), table.remove(t,pos) and table.move use bulk array copies (front insert/remove of 2e4 from 1062-1677 ms to about 70 ms; prove the _limit/Length() invariant), and which of math.type/tointeger/fmod/random, utf8.*, string.byte range deserve in-place CallFast forms (gaps 1.8-3.2x)? Source: audit-libs.md candidates 7 and 12.

## Answer

table.insert/remove/move use bulk Span.CopyTo for array-part runs on metatable-free tables (border-preserving ArraySpan; fuzz matches PUC). insert(t,1,v)x2e4 1655→91ms (PUC 1383); remove(t,1) 1541→78; queue 202→35. In-place CallFast forms for math.type/tointeger/fmod/random, utf8.codepoint/len, string.byte/char (e.g. math.type 243→79ms, utf8.codepoint 257→129). Also fixed math.fmod('7','3') returning int instead of float. Remaining: math.random 1.4-1.8x, utf8.offset/codes 2.2-2.5x, table.move into {} 1.6x.
