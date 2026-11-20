---
kind: ticket
title: String model and the opt-in .NET interop mode
status: resolved
type: grilling
blocked_by: [2]
created: 2026-11-20
updated: 2026-11-20T02:38:07Z
---

## Question

Byte-string semantics are required for compliance (`#s`, `string.byte`, `string.pack`, patterns, the official suite). The user wants the embedding app to be able to opt in to either correctness or .NET interop. Decide what "interop mode" actually means without forking Lua semantics: e.g. one byte-string core plus conversion at the host boundary (UTF-8/UTF-16 in and out, `string` accessors), versus a per-state switch with different semantics. Settle: the representation (byte array, interned or not, hashing), what each mode changes and what it must never change, the cost on the hot path, and which mode the test suite runs in.

## Comments

User lean (at charting): a single byte-string core plus an opt-in interop layer at the host boundary will probably be enough; no per-state switch that changes Lua semantics. To be confirmed when this ticket is worked.

## Answer

A single byte-string core; the interop "mode" is only an opt-in set of conversion helpers at the host boundary. No per-state switch changes Lua semantics: `#s`, `string.byte`, patterns and `string.pack` are byte-exact in all cases, and the official suite runs against this one semantics.

- **Representation:** a Lua string is an immutable byte sequence, never a .NET `string` internally. For performance the engine creates strings with a single allocation: the string is a `byte[]` with a small header (hash and flags) at the front, held in the object slot of the value struct (the value tag identifies it as a string). `LuaString` is a thin struct accessor over it. The headered-array layout is a measured choice that an implementer may revisit if profiling says otherwise. The hash is computed at creation or lazily written into the header.
- **Interning:** short strings (PUC's threshold is 40 bytes) are interned so equality and lookup are pointer compares; long strings compare by content with a lazy hash. The intern table's weakness and lifetime tie into the GC/weak-table fog, so that dependency is noted, not settled here.
- **No intermediate allocations in the string library:** `..`, `string.rep`, `gsub`, `format` and `tostring` build into a pooled or stack buffer (a ref-struct builder) and allocate exactly once for the result. Number-to-string formats into a `stackalloc` span first. `string.sub` copies, so a small substring never pins a large parent.
- **Zero-copy host API:** reading a Lua string from C# yields `ReadOnlySpan<byte>` (a .NET `string` only on request); creating one encodes UTF-8 directly into the destination array or takes a `ReadOnlySpan<byte>`; the intern table supports lookup by `ReadOnlySpan<byte>` so host field access with a known key does not allocate. A cached .NET `string` per Lua string is not part of the base design (costs a field per string).
- **Boundary encoding:** UTF-8 by default when turning a .NET `string` into a Lua string, with Latin-1/raw-byte conversion available explicitly. On decode to .NET, invalid UTF-8 is replaced with U+FFFD by default, with a strict mode that throws.
- **Locale:** `string.upper`/`lower`/`isalpha` and friends use byte-wise ASCII "C" locale semantics only; no .NET culture behaviour leaks in. `os.setlocale` returns the current locale for `"C"`/`""` and `nil` for anything else. The suite's `setlocale` uses go through the waiver policy in the test harness ticket.
- **Standing preference:** the user is explicitly willing to chase performance wins that require code discipline, enforced by analyzers and strict testing.
