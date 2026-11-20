---
kind: ticket
title: Tables and metatables
status: open
blocked_by: [3]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

### What to build

PUC-faithful array-part plus node-part tables so `#t` borders match the reference, `next`/`pairs`, and all metamethods.

### Acceptance criteria

- [ ] `nextvar.lua`, `events.lua` and `constructs.lua` pass or carry reason-coded waivers
- [ ] `#t` on tables with holes matches the Reference binary on differential samples
- [ ] Added to the `required-files` ratchet
