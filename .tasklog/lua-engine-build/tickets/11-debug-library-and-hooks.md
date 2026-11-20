---
kind: ticket
title: debug library and hooks
status: open
blocked_by: [8, 9]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

### What to build

The full `debug` library including hooks, with two source-generated VM loop variants (with and without hook checks) so hook cost stays contained.

### Acceptance criteria

- [ ] `db.lua` passes or carries reason-coded waivers
- [ ] Non-hooked variant carries no hook-check code in its loop
- [ ] Added to the `required-files` ratchet
