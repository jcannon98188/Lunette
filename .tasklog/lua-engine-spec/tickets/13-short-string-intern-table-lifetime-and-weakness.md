---
kind: ticket
title: Short-string intern table lifetime and weakness
status: resolved
type: grilling
blocked_by: [12]
created: 2026-11-20
updated: 2026-11-20T03:11:57Z
---

## Question

The string model interns short strings. Under the .NET GC, how does the intern table avoid leaking and avoid pinning strings alive: weak entries (WeakReference/DependentHandle), periodic sweep, generation-tied clearing, or strong table cleared at collect points? Decide the table's data structure, the short-string length cut-off, hash/equality cost on lookup, per-`LuaState` vs process-wide scope (single-threaded `LuaState` per ticket 07), and the impact on pointer-equality fast paths for table keys. Depends on the GC/weak-table approach.

## Answer

Agreed with the user over two grilling rounds. This revises the "short strings are interned so equality is a pointer compare" line in ticket 04, which flagged this dependency as open.

**Core decision: interning is an optimisation, not an invariant.** The engine's correctness never depends on two equal short strings being the same object. A weak intern table was rejected because it costs one GC handle (WeakReference or DependentHandle) per live string, which scales badly for the .NET GC, and an unbounded strong table was rejected because without our own mark pass it can never be pruned.

**Scope.** One intern cache per `LuaState`, owned by it and released with it. No locking, since `LuaState` is single-threaded (ticket 07). No process-wide table.

**Cut-off.** PUC's 40 bytes (`LUAI_MAXSHORTLEN`). Strings of 40 bytes or fewer are interned on creation with an eager hash. Longer strings are never interned and carry a lazy full-content hash cached in the header on first use as a key.

**Hash.** Every byte is hashed (short and long alike) with a fast non-cryptographic hash over the `byte[]` payload; the specific function is an implementer's micro-benchmark. The hash is seeded per `LuaState` for hash-flooding resistance, as in PUC 5.4. The seed is random by default and a state option fixes it so differential tests and `pairs`/`next` order are reproducible.

**Cache structure.** A fixed-size, set-associative (4-way) array of strong string references; default capacity is a power of two (16K entries as a starting guess) and a state option sets it. Lookup hashes the bytes, probes the set and compares hash, length and bytes; a hit returns the existing string, a miss allocates and inserts. Insert evicts a way on collision, so the memory pinned by the cache is bounded at capacity x (header + at most 40 bytes), constant per state. `collectgarbage("collect")` and `LuaState.Dispose` clear the cache, which is safe because it is not an invariant.

**Equality.** `ReferenceEquals` first. For two short strings that differ by reference, equal means same hash, same length and same bytes; different hashes reject immediately. Long strings compare length then content with the hash as a first filter.

**Reserved strings.** A fixed set created at state creation and held strongly in state fields, never evicted: metamethod names (`__index`, `__newindex`, `__gc`, `__close`, `__mode`, and so on), `"n"`, `"__name"` and similar internal keys. Metamethod lookup is therefore a reference compare with no content fallback. Compiler constants and stdlib names go through the normal cache. They are not pinned, because `load` in a loop would leak. An eviction can leave a table key and a later proto constant as distinct objects with equal content; that costs one extra content compare on the fallback path and never changes behaviour.

**Table keys and host access.** String-key lookup walks the hash chain comparing by reference first, with the hash/length/bytes fallback only when both keys are short strings with equal hash and length. Host API access by `ReadOnlySpan<byte>` (ticket 04) hashes the span and walks the chain comparing content directly. It does not create or intern a `LuaString`, so a host field read allocates nothing. Creating a Lua string from a span still goes through the cache.

**Testing.** The spec names two test-only engine configurations: a capacity-1 cache (every insert evicts) and a constant-hash short-string mode (every comparison hits the fallback and chain walk). Both must pass the same suite as the default configuration with no additional waivers. A property test generates random byte strings, built both through the cache and bypassing it, and checks that `==`, table-key lookup and `next` behaviour agree across configurations. The hooks are internal test seams, not public API. Scheduling the variants in CI is left to the CI pipeline ticket.
