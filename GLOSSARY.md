# Lunette

A pure-managed C# (.NET 11) implementation of Lua 5.4: an embeddable engine library plus a minimal `lua`-compatible CLI used as the test driver.

## Language

### Engine

**LuaValue**:
The single 16-byte value type the engine uses for every Lua value.
_Avoid_: object, variant, TValue

**Proto**:
The compiled bytecode form of one Lua function body, produced by the compiler and executed by the VM.
_Avoid_: chunk, function prototype, bytecode object

**Frame**:
One heap-allocated activation record for a Lua call.
_Avoid_: stack frame, call record

**Coroutine**:
An independently resumable Lua thread with its own stack of Frames.
_Avoid_: thread, fiber

**LuaState**:
The single-threaded embedding root that owns globals, the registry and the main Coroutine.
_Avoid_: VM, context, interpreter

### Strings

**Lua string**:
An immutable sequence of bytes, not characters; the only string type Lua code sees.
_Avoid_: text, .NET string

**Short string**:
A Lua string at or below the intern cut-off length, eligible for the intern cache.
_Avoid_: small string, atom

**Intern cache**:
A bounded per-LuaState cache that shares Short strings as a best-effort optimisation, never a correctness invariant.
_Avoid_: string table, string pool

### Verification

**Tier**:
A milestone grouping of official suite files that must pass before the next stdlib build step begins.
_Avoid_: phase, stage, level

**Waiver**:
A reason-coded manifest entry that skips or overlays a suite file or assertion that cannot apply to a managed runtime.
_Avoid_: skip, exclusion, ignore

**Reference binary**:
The PUC `lua` 5.4.9 built in CI from checksummed source, used for differential testing.
_Avoid_: oracle, baseline interpreter

### Specification

**Rule**:
A normative statement in the build spec with a stable ID, an RFC 2119 keyword and an `Enforced by:` line.
_Avoid_: requirement, guideline

**Open question**:
A recorded implementation-time decision with an ID, an owner tier and a default to use if nobody decides.
_Avoid_: TODO, TBD
