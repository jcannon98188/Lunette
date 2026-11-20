---
kind: ticket
title: .NET 11 runtime facts relevant to a managed Lua VM
status: resolved
type: research
created: 2026-11-20
updated: 2026-11-20T02:32:36Z
---

## Question

Establish from primary sources (Microsoft docs, dotnet/runtime repo and release notes) the .NET 11 facts a VM design hinges on: struct layout and explicit layout/overlapping-field rules with references (can a value struct hold an object reference and a double without boxing?), ref structs/Span/stackalloc limits, GC options (weak references, `ConditionalWeakTable`, finalization, resurrection), tiered JIT / PGO / OSR behavior for interpreter loops, switch-dispatch codegen, `System.Reflection.Emit`/`Expression.Compile` availability under Native AOT and trimming, and any .NET 11 preview features relevant (new APIs, runtime async). Flag what is stable vs preview. Output: facts, not decisions.

## Answer

Findings are on the throwaway branch `research/dotnet11-runtime-facts` (file `research/dotnet11-runtime-facts.md`, pushed to origin, no PR). Verified empirically with SDK 11.0.100 / runtime 11.0.0 on win-x64, including a Native AOT publish.

- **Struct layout:** a struct can hold an object reference and a double side by side without boxing (size 16). Overlapping a reference with a double, or a reference at a misaligned offset, throws `TypeLoadException` on both JIT and Native AOT. A reference may overlap another reference. `[InlineArray]` of `object` works. `stackalloc` is limited to unmanaged types.
- **GC:** long weak references see an object resurrected by its finalizer; short ones do not. A finalizer does not rerun unless `GC.ReRegisterForFinalize` is called. A `ConditionalWeakTable` value referencing its own key does not keep the key alive.
- **JIT:** a once-called method with a hot loop starts at Instrumented Tier0 and moves to Tier1-OSR with no throughput ramp. Switch dispatch compiles to a bounds check plus a relative jump table and a single indirect `jmp`. The compilation-settings doc page says loop methods skip quick JIT, which contradicts observation; treat that page as stale.
- **Dynamic code:** `DynamicMethod` and `Expression.Compile` work on the JIT. Under Native AOT, `IsDynamicCodeSupported` is false and `DynamicMethod` throws `PlatformNotSupportedException`. Docs say `Expression` falls back to an interpreter under AOT; not run.
- **.NET 11 status:** .NET 11 and C# 15 are GA. Runtime Async is GA, opt-in per project via `<Features>runtime-async=on</Features>`. C# 15 memory-safety rules are preview (`LangVersion=preview`). Nothing .NET 11-specific found on struct layout, `stackalloc` limits or weak-reference/finalizer behaviour.
- **Gaps (untested):** arm64 and Linux codegen, sparse or very large switches, `Expression.Compile` under AOT. The three dotnet/runtime design docs cited may lag the .NET 11 implementation.
