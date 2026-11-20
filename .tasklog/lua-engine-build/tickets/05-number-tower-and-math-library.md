---
kind: ticket
title: Number tower and math library
status: resolved
blocked_by: [3]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:43Z
---

## Question

### What to build

PUC-exact integer/float semantics (conversions, overflow, comparison, string-to-number), bitwise and integer-division operators, and the `math` library.

### Acceptance criteria

- [ ] `math.lua` and `bitwise.lua` pass or carry reason-coded waivers
- [ ] Added to the `required-files` ratchet

## Answer

Integer/float number tower and math library implemented; globals install and string coercion completed in ticket 21.
