---
kind: ticket
title: Native AOT and trimming gate
status: resolved
blocked_by: [10]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:57Z
---

## Question

### What to build

A CI job that publishes the CLI with Native AOT and trimming and runs a suite subset against it. No dynamic code in the core.

### Acceptance criteria

- [ ] AOT-published CLI builds with no trim/AOT warnings
- [ ] Suite subset passes on the AOT build
- [ ] Job is part of `ci-gate`

## Answer

Native AOT and trimming gate: AOT subset run script and CI job wired into ci-gate. The Linux publish job has not been run on a GitHub runner.
