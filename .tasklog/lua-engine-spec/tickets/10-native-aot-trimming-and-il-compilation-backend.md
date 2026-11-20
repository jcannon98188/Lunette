---
kind: ticket
title: Native AOT, trimming and IL-compilation backend
status: open
type: grilling
blocked_by: [3, 6]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Decide whether the library must support Native AOT and trimming (which rules out `Reflection.Emit` and `Expression.Compile`) or whether an optional IL-compiling backend is allowed for speed on JIT runtimes. Make the performance-versus-reach trade-off explicit, as the user rates performance as very important.
