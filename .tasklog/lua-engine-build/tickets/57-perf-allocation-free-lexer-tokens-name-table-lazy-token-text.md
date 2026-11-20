---
kind: ticket
title: "Perf: allocation-free lexer tokens (name table, lazy token text)"
status: open
blocked_by: [56]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Can the lexer avoid per-token allocations (interned name table, lazy token text) so function-heavy chunks parse at about 1.2x PUC instead of 2.5x and expression-heavy ones at 1.2x instead of 1.7-2.0x, without making load("return 1 + 2") slower and with identical error texts? Source: audit-libs.md candidate 6.
