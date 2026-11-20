---
kind: ticket
title: "CI: add all-lua job and verify ci-gate covers every job"
status: open
blocked_by: [13, 31]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Gap from ticket 31: the spec's `all-lua` job (and the Windows/macOS `platform` jobs, if ticket 13 does not land them) are missing from `.github/workflows/ci.yml`. Read the effort spec for what `all-lua` must run, add it, and make sure `ci-gate.needs` lists it (the ci-lib check in `fast-checks` enforces this). Also record ticket 29's deferred items (`LUA_SAFE_ACCESS` nightly job, multi-name const folding) as explicit waivers.
