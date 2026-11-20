---
kind: ticket
title: "SimGame: host-controlled globals, require and randomness"
status: open
blocked_by: [10, 14]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Implement requirements of `simgame-embedding/02` (read it via get_item; source of truth). Deny-by-default state creation with an explicit allowlist of libraries/base functions (absent, not removed later); dofile/loadfile/load/collectgarbage/coroutine individually withheld or replaced by the host; host-seeded `require` resolving only host-registered names and never touching the filesystem or `package`; host-seeded or replaceable math.random/math.randomseed with no clock seeding.

Acceptance criteria:
- [ ] State created from an allowlist exposes nothing else
- [ ] Each gated function can be withheld or replaced independently
- [ ] require resolves only host-registered modules, no filesystem/package access
- [ ] math.random deterministic per host seed; replaceable
- [ ] Tests for each path above
