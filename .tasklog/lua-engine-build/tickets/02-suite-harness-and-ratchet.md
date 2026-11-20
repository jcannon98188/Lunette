---
kind: ticket
title: Suite harness and ratchet
status: resolved
blocked_by: [1]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:52:41Z
---

## Question

### What to build

The conformance harness: the pinned, unmodified Lua 5.4.9 suite, a per-file runner (one test per file) plus a CLI `all.lua` run, an initially empty reason-coded Waiver manifest applied as overlays, the `required-files` ratchet so passing files cannot regress, and the Reference binary (PUC 5.4.9) built in CI from checksummed source on Linux only.

### Acceptance criteria

- [ ] Each suite file runs as its own test and reports pass/fail
- [ ] Waivers are applied from the manifest as overlays; the suite is never edited in place
- [ ] `required-files` ratchet fails CI when a listed file regresses
- [ ] Reference binary builds in CI from checksummed source

## Answer

Suite harness with required-files ratchet and reason-coded waivers.toml implemented; later extended with platform key and sharding (31).
