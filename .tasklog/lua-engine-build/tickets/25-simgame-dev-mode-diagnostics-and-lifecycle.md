---
kind: ticket
title: "SimGame: dev-mode diagnostics and lifecycle"
status: open
blocked_by: [10, 11, 14]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Implement requirements of `simgame-embedding/03` (read it via get_item; source of truth). Every Lua error surfaced to the host carries chunk name, line and a structured stack trace (function names, source, line) without exposing the debug library to scripts; host console evaluation of a snippet inside an existing state (its globals, modules, budgets) returning values or a structured error; cheap repeated discard-and-recreate of states for hot reload with disposal releasing all state-owned resources.

Acceptance criteria:
- [ ] Host-visible errors carry chunk, line and structured trace
- [ ] Snippet evaluation inside a live state returns values or a structured error
- [ ] Repeated create/dispose cycles leak nothing (tested)
- [ ] Tests for each path above
