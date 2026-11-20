---
kind: ticket
title: JIT MinOpts thresholds and detecting them in CI
status: open
type: research
created: 2026-11-20
updated: 2026-11-20
---

## Question

The execution-architecture decision assumes the .NET JIT silently falls back to MinOpts for methods past size thresholds (recalled, unverified: ~60 KB IL, ~2000 basic blocks, ~2000 locals, ~8000 local refs). Establish from primary sources (dotnet/runtime source, docs) and, if feasible, empirically on SDK 11.0.100:\n\n1. The actual MinOpts thresholds in .NET 11 and whether they are configurable.\n2. How a build or CI step can detect that a given method was compiled under MinOpts (e.g. `DOTNET_JitStdOutFile`/`JitDisasmSummary`, ETW/EventPipe JIT events, `MethodImplOptions` introspection) and how to assert an IL-size budget.\n3. Whether `AggressiveOptimization` bypasses the thresholds, and what it costs (tiering, OSR, PGO).\n4. The IL size and block count of a realistic 83-opcode `switch` interpreter loop, estimated from a similar existing .NET interpreter if one is available.\n\nCapture findings in a file on a throwaway `research/` branch; no PR.
