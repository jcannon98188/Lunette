---
kind: ticket
title: Test isolation and suite runner cleanup
status: resolved
blocked_by: [10]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:53:13Z
---

## Question

Known test-infrastructure gaps:
- `DebugLibTests.Cli_traceback_follows_a_function_that_called_error` is flaky when run in parallel (tests change the process working directory). Find every test that mutates cwd or other process-global state and isolate (serialize via a collection, or stop mutating cwd).
- `SuiteRunner` must call `state.Dispose()` (swallowing errors) after each suite file so end-of-state `__gc` tests run (gc.lua).
- Source tarball SHA-256 for Lua 5.4.9 in `ci/build-reference.sh` was computed from a download, not a published checksum: cross-check against an independent source and record the provenance in `tests/lua-suite.SOURCE.md`.
- Debug and Release test runs must both be green, including JIT budget tests.

Acceptance criteria:
- [ ] Full test suite passes 20 consecutive runs in Release and Debug without flakes
- [ ] SuiteRunner disposes state after each file
- [ ] Checksum provenance documented
- [ ] No test mutates process-global state without isolation

## Answer

Test isolation: temp-dir scripts, SuiteRunner.WithWorkingDirectory, guard test that only the suite runner mutates process-global state; flaky CLI traceback test fixed.
