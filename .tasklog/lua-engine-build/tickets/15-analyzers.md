---
kind: ticket
title: Analyzers
status: open
blocked_by: [1]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

### What to build

Banned-API and AOT analyzers plus custom marker-driven analyzers LD001-LD004, errors-only, with a suppression ratchet. Confine `unsafe` to its dedicated assembly.

### Acceptance criteria

- [ ] Violations of each rule fail the build
- [ ] Suppression count can only decrease in CI
- [ ] Analyzer failures are part of `ci-gate`
