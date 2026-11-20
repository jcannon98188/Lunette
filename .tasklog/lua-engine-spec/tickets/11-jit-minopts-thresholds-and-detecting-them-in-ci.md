---
kind: ticket
title: JIT MinOpts thresholds and detecting them in CI
status: resolved
type: research
created: 2026-11-20
updated: 2026-11-20T03:06:18Z
---

## Question

The execution-architecture decision assumes the .NET JIT silently falls back to MinOpts for methods past size thresholds (recalled, unverified: ~60 KB IL, ~2000 basic blocks, ~2000 locals, ~8000 local refs). Establish from primary sources (dotnet/runtime source, docs) and, if feasible, empirically on SDK 11.0.100:\n\n1. The actual MinOpts thresholds in .NET 11 and whether they are configurable.\n2. How a build or CI step can detect that a given method was compiled under MinOpts (e.g. `DOTNET_JitStdOutFile`/`JitDisasmSummary`, ETW/EventPipe JIT events, `MethodImplOptions` introspection) and how to assert an IL-size budget.\n3. Whether `AggressiveOptimization` bypasses the thresholds, and what it costs (tiering, OSR, PGO).\n4. The IL size and block count of a realistic 83-opcode `switch` interpreter loop, estimated from a similar existing .NET interpreter if one is available.\n\nCapture findings in a file on a throwaway `research/` branch; no PR.

## Evidence

Branch `research/jit-minopts-thresholds`, `docs/research/jit-minopts-thresholds.md` (also visible in the local worktree at `.claude/worktrees/agent-a91e14d85acbf3e73`). Claims tagged verified-empirically / verified-in-source / unverified in that file.

## Answer

Findings are on throwaway branch `research/jit-minopts-thresholds` (pushed, no PR), file `docs/research/jit-minopts-thresholds.md` with the probe project beside it. Verified on SDK 11.0.100 / runtime 11.0.0.

1. **Thresholds** (verified in source and empirically): five limits in the retail JIT: IL size 60000 bytes, IL instruction count 20000, basic blocks 2000, locals 2000, local refs 8000. Unchanged from .NET 8 to 11. Hard-coded: the `DOTNET_JITMinOpts*` knobs that raise them exist only in debug/checked JITs. `DOTNET_JITMinOpts=1` forces MinOpts for all methods; tiering/PGO/R2R knobs do not change which methods fall back.

2. **Detection** (verified empirically): `DOTNET_JitStdOutFile` + `DOTNET_JitDisasmSummary=1` prints `[Tier-0 switched MinOpts, IL size=...]` for fallbacks. The `MethodLoadVerbose` event (keyword 0x10) has `MethodFlags` bits 7-9 giving the tier; fallback appears as `MinOptJitted` (confirmed with an in-process EventListener). Static budget: walk `MethodBody.GetILAsByteArray()` on the Release build; IL size matched the JIT's reported value exactly. R2R and NativeAOT skip these thresholds, so the check must run on the plain JIT path.

3. **AggressiveOptimization** (verified empirically and in source): does NOT bypass any threshold (a 72 KB-IL method with it still went to MinOpts). Cost: no tiering, no dynamic PGO, larger up-front JIT (~31 ms vs ~7 ms for a 21 KB-IL loop). In a tiny 8-opcode VM microbenchmark steady state was ~270 ms with it, ~348 ms default tiering, ~677 ms forced MinOpts (indicative only, one machine). Unverified: whether `static readonly` folding works inside it; whether R2R skips such methods.

4. **Realistic size** (verified empirically): a synthetic 85-opcode loop measured 11-21 KB IL, 4.3-8.1k instructions, 377-770 blocks: 20-40% of each limit; crosses a limit at ~2.5-3x the bulkiest version. Existing .NET Lua VMs keep dispatch methods under 4 KB IL (KopiLua `luaV_execute` 3.7 KB, Lua-CSharp VM `MoveNext` 3.4 KB, MoonSharp `Processing_Loop` 1.9 KB).

**Design-relevant surprises**
- Instruction count and local refs trip before IL bytes (~2.5 IL bytes/instruction puts the 20000-instruction limit near 50 KB; a 55 KB method fell back under the 60 KB limit). Budget on instruction count and local refs.
- Silent inlining cliff at 921 locals (0.9 x 1024): the JIT stops inlining into the method while still reporting FullOpts; at 1024 it also stops struct promotion. Not visible in the summary log; detect with `DOTNET_JitDisasm` looking for leftover `call`s. A `LuaValue`-struct-heavy loop with many inlined helpers is more at risk of this than of MinOpts.
- A method over the thresholds is jitted three times (Tier0, instrumented Tier0, MinOpts).
- Default tiering was the slowest steady state in the microbenchmark, supporting `AggressiveOptimization` on the loop method, but only paired with a budget test and a `MinOptJitted` check.

**Suggested CI gate (Release build):** instructions <= 14000, local refs <= 5600, basic blocks <= 1400, IL <= 36000 (70% of limits), IL locals <= ~500 so inlinees keep headroom under 921; plus a JIT-summary/EventListener run asserting no `MinOptJitted` for the interpreter loop methods.
