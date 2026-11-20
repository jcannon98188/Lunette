---
kind: ticket
title: Value representation and number tower
status: resolved
type: grilling
blocked_by: [2, 3]
created: 2026-11-20
updated: 2026-11-20T02:40:32Z
---

## Question

Decide how a Lua value is represented so performance is maximised under .NET's GC and layout rules: struct layout (type tag, 64-bit payload, object reference), integer vs float subtypes with Lua 5.4 conversion and overflow semantics, table design (array part, hash part, `next` iteration stability, border semantics), and function/closure/upvalue representation. State the performance reasoning against the research findings.

## Answer

All four proposals accepted by the user.

**Value layout.** `struct Value { object? Ref; long Bits; }`, 16 bytes, no separate tag field (the tag + double + object layout pads to 24). The type is derived from `Ref`: `null` is nil; shared tag instances stand for bool, integer, float and light userdata, with the payload in `Bits`; heap kinds (string, table, closure, host function, userdata, coroutine) are the object itself, discriminated by sealed-type checks (method-table compare). Doubles are stored by bit reinterpretation. Overlapping `Ref` and a double is not possible (TypeLoadException, research ticket 03). Costs accepted: the GC write barrier on every Value store into a heap array (unavoidable with any ref field) and a method-table load to identify heap kinds. Whether tags are static sentinels or sealed tag classes is a micro-benchmark choice an implementer may revisit. Discipline rule for the spec: a Roslyn analyzer forbids reading `Bits` without a prior `Ref` type check.

**Number tower.** PUC 5.4 semantics exactly: separate int64 and double subtypes; integer arithmetic wraps; `//` and `%` are floor-based; mixed int/float comparison is mathematically exact; float keys with integer values normalise to integer keys; `for` loops precompute the iteration count; string-to-number coercion per the manual; float-to-integer conversion only for exact values. Number formatting (`%.14g`, `%d`, `%a`) is the engine's own formatter, not .NET's `G14`.

**Tables.** PUC-faithful: array part plus a node hash part using chained scatter with Brent's variation (node = key Value, value Value, int next; 40 bytes). The array part is sized by PUC's rehash rule (largest n with more than n/2 slots used). `#t` is PUC's border search. `next` walks the array index, then the node index; assigning to existing fields, including `nil`, during traversal stays legal. Each table caches absent-metamethod bits (PUC's `flags`). Rationale: matching the sizing rule makes `#t` on tables with holes identical to the reference binary, so differential tests can compare it. Hash-part `pairs` order cannot match PUC (the string-hash seed varies per run), so the spec states that differential tests must never depend on hash-part `pairs` order.

**Closures and upvalues.** `Proto` is immutable and shared; `LuaClosure { Proto; UpVal[] }`. Each coroutine's stack is a growable heap `Value[]` (the execution-architecture ticket builds on this). `UpVal` is PUC-style: open, it points at `(Value[] stack, int index)`; closing copies the value into the `UpVal`'s own field and nulls the array, so access is `arr is null ? v : arr[idx]`. Each coroutine keeps a sorted open-upvalue list; growing the stack repoints open upvalues (as PUC's `correctstack`). Compile-time heap cells for captured locals were rejected: every access to a captured local would go through an indirection and `debug.getlocal`/`upvaluejoin` get harder.

**Deferred.** The representation of host (C#) functions and userdata is left to the host API ticket.
