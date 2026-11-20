---
kind: ticket
title: Native AOT, trimming and IL-compilation backend
status: resolved
type: grilling
blocked_by: [3, 6]
created: 2026-11-20
updated: 2026-11-20T02:51:17Z
---

## Question

Decide whether the library must support Native AOT and trimming (which rules out `Reflection.Emit` and `Expression.Compile`) or whether an optional IL-compiling backend is allowed for speed on JIT runtimes. Make the performance-versus-reach trade-off explicit, as the user rates performance as very important.

## Answer

All six proposals accepted by the user.

1. **Core is Native AOT and trim-safe.** Compiler, VM, stdlib and host API publish cleanly under Native AOT and trimming: no `Reflection.Emit`, no `Expression.Compile`, no reflection-based discovery. The library sets `IsAotCompatible` and builds with trim/AOT analyzer warnings as errors. The interpreter is the only execution path the core depends on, so performance for AOT users comes entirely from the interpreter design (ticket 06).
2. **IL-compiling backend only as a separate optional package.** Any `DynamicMethod`/`Expression.Compile` backend lives in its own assembly the core never references, plugs in through the reserved `Proto` slot (ticket 06), is enabled only when `RuntimeFeature.IsDynamicCodeSupported` is true, and falls back to the interpreter otherwise. Reach is never traded for speed; speed on JIT runtimes is opt-in.
3. **Spec does not commit to building the backend.** It is recorded as a post-1.0 extension with the constraints already fixed by ticket 06 (same stack/`CallInfo`/upvalue layout, mid-function handback to the interpreter, yield/error/`debug` keep working). No IL strategy, no speculative interfaces, no performance target for it.
4. **AOT verified in CI.** CI publishes a Native AOT build of the CLI host (ticket 07) and runs a defined subset of the 5.4 suite through it, in addition to analyzer warnings-as-errors on the core. It is a smaller milestone-tier job than the full ratchet (ticket 09).
5. **Ahead-of-time Lua-to-C# compilation is out of scope** and not listed as a planned extension (it would need a second front-end and its own semantics-parity story).
6. **The `Execute` MinOpts/IL-size guard (ticket 06) applies to JIT runs only.** Native AOT goes through ILC with no tiering; the AOT CI job checks correctness, not speed. The MinOpts thresholds themselves are pending in the research ticket "JIT MinOpts thresholds and detecting them in CI".
