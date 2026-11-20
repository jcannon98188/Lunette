---
kind: spec
title: Build spec for a .NET 11 Lua 5.4 engine
status: open
created: 2026-11-20
updated: 2026-11-20
---

## Problem Statement

No mature, pure-managed Lua 5.4 engine exists for .NET. Developers who want to embed Lua in a .NET 11 application today must bind native PUC Lua (losing portability, Native AOT friendliness and a safe managed boundary), or use older managed engines that target 5.1/5.2, treat strings as UTF-16 text, and omit weak tables, `__gc` and the `debug` library. Nobody can currently verify such an engine against the official Lua 5.4 test suite in a way that proves it is a faithful Lua 5.4.

The decisions that say how to build it all exist (the lua-engine-spec map, 19 resolved decision tickets, the glossary and five ADRs), but not as a single build-ready brief an implementer can work from.

## Solution

Lunette: a pure-managed C# (.NET 11) implementation of Lua 5.4. It is an embeddable engine library plus a minimal `lua`-compatible CLI that acts as the test driver. A developer loads and runs Lua source from C# or the CLI and sees behavior, error messages and output matching PUC Lua 5.4.9. Correctness is proven by the pinned, unmodified official 5.4.9 test suite (with reason-coded waivers only where a managed runtime cannot apply a file or assertion) and by differential testing against a reference PUC 5.4.9 built in CI. The engine is AOT- and trim-safe, performs competitively without a numeric performance gate, and is built in tiers aligned to the suite files.

## User Stories

1. As a .NET application developer, I want to create a `LuaState`, load a chunk and call it, so that I can run Lua scripts inside my application.
2. As a .NET application developer, I want a pure-managed engine with no native dependencies, so that my app deploys unchanged on Windows, Linux and macOS.
3. As a .NET application developer, I want the engine to be Native AOT and trim safe, so that I can publish AOT-compiled apps that embed Lua.
4. As an embedder, I want one 16-byte `LuaValue` type for every Lua value, so that moving values across the host boundary does not box or allocate.
5. As an embedder, I want span-based host functions, so that I can register C# functions with no per-call allocation.
6. As an embedder, I want host functions to opt in to yielding with continuations, so that C# code can participate in coroutines.
7. As an embedder, I want a `LuaState` that is single-threaded by contract, so that its behavior is simple and predictable.
8. As an embedder running untrusted scripts, I want best-effort sandbox limits (instruction, memory, stack depth), so that a script cannot easily hang or exhaust my process.
9. As an embedder, I want a host-replaceable platform interface under `io` and `os`, so that I can virtualise or deny file, process and clock access.
10. As an embedder, I want capability flags for `io.popen` and `os.execute`, so that I can disable process spawning entirely.
11. As an embedder, I want no reflection in the core, so that nothing breaks under trimming.
12. As a Lua script author, I want Lua 5.4 language semantics, including integers and floats, goto, to-be-closed variables, `<const>` and integer division, so that existing 5.4 code runs unchanged.
13. As a Lua script author, I want integer/float conversion, overflow, comparison and string-to-number rules exactly as in PUC, so that numeric code gives identical results.
14. As a Lua script author, I want `#t` on sequences with holes to match PUC, so that code relying on observed border behavior still works.
15. As a Lua script author, I want Lua strings to be byte strings, so that binary data and arbitrary encodings pass through unchanged.
16. As a Lua script author, I want the full standard library (base, string, table, math, utf8, coroutine, io, os, debug, package), so that I do not hit missing functions.
17. As a Lua script author, I want `string.format`, patterns, `pack`/`unpack` and `gsub` to behave as in PUC, so that text-processing scripts port without edits.
18. As a Lua script author, I want error messages worded exactly as PUC words them, so that scripts and tests that match on message text keep working.
19. As a Lua script author, I want coroutines whose resume/yield works across metamethods, iterators and `pcall`, so that idiomatic coroutine code is not constrained.
20. As a Lua script author, I want deep Lua-to-Lua recursion that does not overflow the .NET stack, so that recursive algorithms fail with a Lua stack-overflow error instead of crashing the process.
21. As a Lua script author, I want weak tables and `__gc` finalizers to work on a best-effort basis, so that caches and resource cleanup behave sensibly.
22. As a Lua script author, I want `collectgarbage` to be accepted and mapped best-effort onto the .NET GC, so that existing scripts do not fail on it.
23. As a Lua script author, I want a full `debug` library including hooks, so that debuggers, profilers and coverage tools can be written in Lua.
24. As a Lua script author, I want `load` to accept text chunks, so that I can compile code at runtime. (Precompiled binary chunks and `string.dump` are not required.)
25. As a CLI user, I want a `lua`-compatible command line (script file, `-e`, `-i`, `-l`, `-`, `arg` table, exit codes), so that I can run scripts and the official test suite from a shell.
26. As an interop user, I want opt-in boundary helpers that convert between Lua strings and .NET `string` as UTF-8, so that C# code can work in text without changing Lua semantics.
27. As a maintainer, I want the pinned official 5.4.9 suite run unmodified, one test per file plus a CLI `all.lua` run, so that conformance is measured against the real thing.
28. As a maintainer, I want a reason-coded waiver manifest applied as overlays, so that every deviation from the suite is explicit, reviewable and never silent.
29. As a maintainer, I want a reference PUC 5.4.9 built from checksummed source in CI, so that I can run differential tests against known-good behavior.
30. As a maintainer, I want tiered milestones tied to suite files, so that the stdlib is built in an order that always has a measurable green target.
31. As a maintainer, I want a ratchet (`required-files`) so that once a suite file passes it can never regress unnoticed.
32. As a maintainer, I want one aggregate `ci-gate` required check, so that branch protection is simple and robust.
33. As a maintainer, I want Linux x64 to be the full authoritative gate, so that the pass criteria are unambiguous.
34. As a maintainer, I want Windows and macOS runs of the platform-sensitive files (`files.lua`, `main.lua`) with reason-coded platform waivers, so that portability is verified without false failures.
35. As a maintainer, I want nightly arm64, preview-SDK and variant runs, so that architecture and upcoming-runtime problems surface early.
36. As a maintainer, I want CI variants with a capacity-1 intern cache and a constant string hash, so that correctness is proven not to depend on interning or hash quality.
37. As a maintainer, I want custom analyzers (LD001-LD004) plus banned-API and AOT analyzers as errors, so that hot-path discipline is enforced by the build.
38. As a maintainer, I want exact-byte allocation tests on hot paths, so that accidental allocations are caught per PR.
39. As a maintainer, I want a per-PR JIT budget at 70% of the retail JIT's MinOpts limits, so that the interpreter loop is never silently downgraded to unoptimised code.
40. As a maintainer, I want `unsafe` code confined to one assembly with a safe-twin CI variant, so that unsafe optimisations are contained and verifiable.
41. As a maintainer, I want a suppression ratchet on analyzer suppressions, so that suppressions can only shrink over time.
42. As a performance-minded maintainer, I want classic macro benchmarks plus per-opcode and stdlib micro-benchmarks, so that I can see where time goes.
43. As a performance-minded maintainer, I want results expressed as a ratio to PUC 5.4.9 and kept in a perf-history branch, so that trends are visible without blocking merges.
44. As a performance-minded maintainer, I want perf-motivated changes to cite before/after numbers, so that optimisations are justified.
45. As a future backend author, I want the bytecode `Proto` to be a stable seam, so that an optional IL-compilation backend can be added as a separate package without touching the core.
46. As a contributor, I want a minimal public API surface tracked by PublicApiAnalyzers, so that accidental API growth is caught in review.
47. As a contributor, I want a glossary and ADRs for the load-bearing decisions (LuaValue layout, heap frames, pending-state errors, single assembly, best-effort interning), so that I understand why the engine is shaped this way.
48. As a contributor, I want every normative rule to have a stable ID, RFC 2119 keyword and `Enforced by:` line, so that I know what is mandatory and how it is checked.
49. As a contributor, I want recorded open questions with an owner tier and a default, so that I am never blocked waiting for a decision.
50. As a reviewer, I want a traceability matrix from decision tickets to rule IDs, so that I can confirm every decision landed in the spec.

## Implementation Decisions

Detail for each decision lives in its ticket (frozen as the decision record) and in the ADRs under `docs/adr`; this section is the index of what is decided. Vocabulary follows `GLOSSARY.md`.

**Layout and modules**
- One core assembly holds compiler, VM, host API and the full stdlib. Satellites: a CLI, analyzers, an Interop package and optional IL-backend packages. Target `net11.0` only. Public surface is minimal and tracked by PublicApiAnalyzers. `unsafe` is confined to one dedicated assembly with a safe-twin CI variant. (ADR 0004)
- The stdlib is built in three steps aligned to the test tiers, driven by which suite files need which functions.

**Value model and numbers**
- `LuaValue` is a 16-byte struct `{object? Ref; long Bits}`; the type is derived from `Ref`. Overlapping the reference and number fields (NaN-boxing style) is not possible on .NET 11. (ADR 0001)
- Number semantics are PUC-exact. Tables are PUC-faithful array-part plus node-part so `#t` borders match the reference.
- Upvalues are PUC-style open/closed over a growable per-Coroutine `LuaValue[]` stack.

**Strings**
- One byte-string core: single-allocation headered `byte[]`. Short strings (40-byte cut-off) pass through a per-state, bounded, 4-way set-associative strong Intern cache with a seeded full-length hash, pointer-first equality and hash/length/byte fallback, pinned reserved strings and span-based non-allocating lookup. Interning is never a correctness invariant. (ADR 0005)
- The string library makes no intermediate allocations. The host boundary is UTF-8 and span-based; locale is ASCII "C". "Interop mode" is only boundary helpers, not a semantic switch.

**Execution**
- A one-pass compiler emits PUC 5.4-style register bytecode in the engine's own encoding, with no AST. One `switch` loop keeps hot paths inline and cold paths in helpers.
- Lua-to-Lua calls use heap-allocated Frames, so recursion never uses the .NET stack. Metamethods, iterators and `pcall` are yieldable; `sort`, `gsub` and `load` readers are re-entrant but non-yieldable. Nested resume obeys PUC limits (1M stack slots, 200 re-entrancy levels). (ADR 0002)
- The loop's JIT cost is budgeted against the retail MinOpts limits (60000 IL bytes, 20000 IL instructions, 2000 blocks, 2000 locals, 8000 local refs) at 70% per PR; `AggressiveOptimization` applies to the loop only.
- The `Proto` is the seam for any later backend. An IL backend, if ever built, is a separate package gated on `IsDynamicCodeSupported`. Native AOT is verified by a CI Native AOT CLI run on a suite subset.

**Errors and debug**
- Errors are pending-state with in-loop unwinding; no exceptions on common paths, and a throw only as an ergonomic fallback caught outside the loop. Message text is PUC-literal. (ADR 0003)
- The full `debug` library is provided. Two source-generated loop variants (with and without hook checks) contain hook cost.

**GC**
- Weak tables are a separate storage variant on `WeakReference`/`DependentHandle` (ephemerons, no own mark pass). `__gc` uses a finalizable sentinel plus a drain queue run on the Lua thread (reverse-mark order per batch; registry for `IDisposable`). `collectgarbage` maps best-effort onto the .NET GC. `gc.lua` gets per-assertion overlays; `gengc.lua` is waived whole.

**Host API**
- Idiomatic C# over `LuaValue`; single-threaded `LuaState`; span-based host-function fast path with opt-in yield and continuations; no reflection in the core; best-effort sandbox limits; minimal PUC-compatible CLI.

**io and os**
- Both sit on a host-replaceable platform interface: a binary-always buffered file layer, `popen`/`execute` via `Process` behind capability flags, own `strftime`, CPU-time `os.clock`, "C"-only `setlocale`, strict-UTF-8 filenames and PUC-literal errno messages.

**Enforcement**
- Three tiers: banned-API plus AOT analyzers; custom marker-driven analyzers LD001-LD004; allocation and JIT run-time tests. Analyzers are errors-only with a suppression ratchet.

**Spec documents**
- The authoritative build spec is a short index `spec.md` plus about 8 chapters. Each decision is stated once in its owning chapter; rules carry stable IDs, RFC 2119 keywords and `Enforced by:` lines; each chapter ends with open questions; waivers live only in the manifest; a traceability matrix appendix covers all tickets.

## Testing Decisions

**One test seam: a Lua chunk in, observable behavior out.** Tests run Lua source through `LuaState` load/call or the CLI and assert on returned values, error messages, output and exit codes. Compiler, VM, tables, Intern cache and other internals are never tested directly.

**What makes a good test:** it exercises external behavior only, so internals can be rewritten without touching tests. Where a PUC behavior is the spec, the test is "same as the Reference binary".

**What is tested at that seam**
- The pinned, unmodified official Lua 5.4.9 suite: one test per file plus a CLI `all.lua` run. There is no managed `T` library, so `api.lua` is waived. Waivers are reason-coded manifest entries applied as overlays; nothing is edited in place.
- Differential testing against the Reference binary (PUC 5.4.9 built from checksummed source on Linux only), in three parts.
- Tiered milestones: each Tier's suite files must pass before the next stdlib build step. A `required-files` ratchet prevents regression.
- Variant runs: capacity-1 Intern cache, constant string hash, safe-twin (no `unsafe`), and the Native AOT CLI on a suite subset.

**Run-time discipline tests** (the deliberate exception to "seam only", since the properties are not observable as Lua behavior): exact-byte allocation tests on hot paths, and a per-PR JIT check against 70% of the MinOpts limits, including `MinOptJitted` and inlining-cliff checks.

**CI:** GitHub Actions with script-based steps. Linux x64 is the full gate; Windows and macOS run `files.lua` and `main.lua` with reason-coded platform waivers; nightly arm64, preview SDK and variants. One aggregate `ci-gate` required check. Benchmarks never block.

**Prior art:** none in this repo (no code yet). The model is the official suite's own runner and PUC's `all.lua`.

## Out of Scope

- Bytecode compatibility: `string.dump` and loading PUC binary chunks are not required.
- A numeric performance gate. Performance shapes design and is measured and reported, but never blocks.
- Ahead-of-time Lua-to-C# compilation.
- Committing to an IL-compilation backend (any such backend is a separate optional package).
- A managed `T` test library (`api.lua` waived).
- Exact `gengc.lua` generational-GC behavior, and exact GC-count assertions the .NET GC cannot satisfy (waived or overlaid).
- Multi-threaded use of a single `LuaState`.

## Further Notes

- Source of truth: the lua-engine-spec map, tickets 01-19, `GLOSSARY.md` and ADRs 0001-0005. Ticket 19 specifies that the full normative chapters (about 8, with rule IDs and a traceability matrix) are written in the implementation effort; this document is the brief that sits above them.
- Ticket 02 (prior art) evidence was self-reported through a summarising fetch tool and should be re-checked before the spec leans on it.
- The official suite is 5.4.9 (MIT, 33 files); the Windows reference binary is only 5.4.8, so the Reference binary is built from source in CI.
- Triage: the tasklog `triage` tool only applies to tickets, not to `spec.md`, so the `ready-for-agent` label could not be applied to this spec. A ticket pointing at it can carry the label if you want one.
