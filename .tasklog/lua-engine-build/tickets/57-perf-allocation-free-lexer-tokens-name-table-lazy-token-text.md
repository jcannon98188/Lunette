---
kind: ticket
title: "Perf: allocation-free lexer tokens (name table, lazy token text)"
status: resolved
blocked_by: [56]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-21T02:43:04Z
---

## Question

Can the lexer avoid per-token allocations (interned name table, lazy token text) so function-heavy chunks parse at about 1.2x PUC instead of 2.5x and expression-heavy ones at 1.2x instead of 1.7-2.0x, without making load("return 1 + 2") slower and with identical error texts? Source: audit-libs.md candidate 6.

## Answer

Allocation-free lexer tokens: per-compilation name table keyed by source bytes (grows lazily), static keyword and punctuation tables, number/string tokens as Start/End spans, lazy long-string dictionary, error 'near' text sliced only on error. Lexing 64k tokens: ~64 B/token → <4 B/token. load() of 3MB chunk 123-144ms → 82-92ms (PUC 64-80); repeated-function chunk 135-146 → 70. Error texts match PUC (7 tests). Known pre-existing: const-assign error text differs from PUC.
