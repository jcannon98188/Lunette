---
kind: ticket
title: goto, to-be-closed and const
status: resolved
blocked_by: [7]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:47Z
---

## Question

### What to build

`goto`/labels, `<close>` to-be-closed variables and `<const>` locals, including their error-path behavior.

### Acceptance criteria

- [ ] `goto.lua` and `locals.lua` pass or carry reason-coded waivers
- [ ] Added to the `required-files` ratchet

## Answer

goto/labels, to-be-closed variables and const attributes implemented; literal single-name const folding added in 29.
