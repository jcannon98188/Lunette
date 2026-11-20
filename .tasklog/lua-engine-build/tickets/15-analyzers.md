---
kind: ticket
title: Analyzers
status: resolved
blocked_by: [1]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:53Z
---

## Question

### What to build

Banned-API and AOT analyzers plus custom marker-driven analyzers LD001-LD004, errors-only, with a suppression ratchet. Confine `unsafe` to its dedicated assembly.

### Acceptance criteria

- [ ] Violations of each rule fail the build
- [ ] Suppression count can only decrease in CI
- [ ] Analyzer failures are part of `ci-gate`

## Answer

Roslyn analyzers LD001-LD006 plus BannedApiAnalyzers/PublicApiAnalyzers with a canary script; LD006 (no try/using/lock in dispatch loop) added in 29.
