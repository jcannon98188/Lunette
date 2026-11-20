---
kind: ticket
title: Test harness and waiver policy
status: resolved
type: grilling
blocked_by: [1]
created: 2026-11-20
updated: 2026-11-20T02:49:43Z
---

## Question

Decide the test strategy the spec will mandate: how the official suite is vendored and run, the differential-testing setup against the reference `lua` binary, how waivers are recorded (per file, per assertion) and what justifies one, what "done" means for a milestone (e.g. which files must pass at each stage), and how the harness reports progress.

## Answer

Decisions (all accepted by the user):

1. **Vendoring**: the `lua-5.4.9-tests` suite is committed unmodified under e.g. `tests/lua-suite/` with its MIT notice and the tarball SHA-256 recorded; never edited in place. All deviations are overlays.
2. **Running**: one test case per suite `.lua` file, in-process via the host API in a fresh `LuaState` with the suite directory as cwd, plus one end-to-end `all.lua` run through the CLI with `_U=true` (also checks the CLI contract from ticket 07).
3. **`T` library**: no managed `T`; engine runs with `T=nil`. `api.lua` is waived whole-file and its intent covered by our own C# host-API tests. `T`-gated code stays skipped.
4. **Waivers**: whole-file or per-assertion overlay, recorded in a checked-in manifest (e.g. `waivers.toml`: file, line/anchor, mandatory reason code). Harness applies overlays to a temp copy at test time. Reason codes: `host-C-API`, `no-bytecode`, `gc-accounting`, `os-env`, `bug-wontfix` (needs a linked ticket and expiry review). "Slow" or "hard to implement" never qualifies; a waiver without a valid reason code fails the harness; unwaived failures are failures.
5. **Differential testing**: build reference Lua 5.4.9 from source in CI (Linux, pinned by hash; no official Windows binary, LuaBinaries only 5.4.8). Run a curated snippet corpus plus seeded-random generated programs through both engines, comparing stdout, exit status and normalised error messages. Nondeterministic/reference-only output (non-sequence iteration order, addresses) is avoided or normalised. Divergences are fixed or waived with a reason code. The corpus is a spec artefact for the implementation effort.
6. **Milestones**: gated on ordered file tiers. (1) core language: constructs, math, strings, nextvar, events, calls, closures, vararg, goto, locals; (2) library breadth: sort, tpack, utf8, pm, bitwise, literals, attrib, errors, big, bwcoercion, verybig, cstack; (3) runtime-heavy: coroutine, gc, gengc, db, files, main, code, api (waived), heavy (excluded as upstream does). Tier lists are a starting point, finalised in the build spec after checking file contents. Tier gate = every non-waived file in the tier passes and the waiver list is reviewed. Final "done" = every non-waived file passes with zero unreasoned waivers.
7. **Reporting and regression**: harness emits machine-readable per-file results (pass/fail/waived/skipped, duration) and a generated Markdown matrix (committed at release milestones, CI artefact otherwise). CI is a ratchet: any file that passed on `main` must keep passing; failures are tolerated only for files not yet in the growing `required-files` list, which is the milestone gate.
8. **Other corpora**: spec is confined to the official suite, the differential corpus and C# unit tests. Luau conformance and Fengari are named as optional inspiration only; adding any needs a separate licence check.
