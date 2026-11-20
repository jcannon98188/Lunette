---
kind: ticket
title: Error model, pcall and debug library scope
status: resolved
type: grilling
blocked_by: [6]
created: 2026-11-20
updated: 2026-11-20T02:48:46Z
---

## Question

Decide how Lua errors are represented and propagated on top of the chosen execution model (exceptions vs result codes, cost of `pcall` on the hot path), error values and messages matching the reference (the suite checks message text), tracebacks and line info, and how much of the `debug` library to support (hooks, `getinfo`, `sethook`, `getlocal`) given what the suite exercises.

## Answer

Decided with the user (Q1 revised after the user asked for a more performance-focused design; Q2–Q9 otherwise accepted).

**Propagation (Q1′).** No .NET exception on the common paths. VM-detected errors (nil index/arith/call, compare, concat): the slow-path helper stores the error value in the coroutine and returns a flag; the loop jumps to an in-loop `HandleError`, which walks the heap `CallInfo` chain to the nearest protected frame (`pcall`/`xpcall`) and continues. `error` and `assert` are recognised by the interpreter. Host functions can fail by returning a negative sentinel via `CallContext.Error(value)` from `int Fn(ref CallContext)` (the call site already tests the result count). Exceptions remain only an ergonomic fallback: stdlib helpers may `throw LuaException`; the `catch` lives only in a small outer wrapper around `Execute`, never in the hot loop method, converts to the same pending-error state and re-enters `Execute` (state lives in `CallInfo`). Re-entrant host calls (sort, gsub callbacks): the nested `Execute` returns an error status; `LuaFunction.Call` throws `LuaException` for C# callers, `TryCall` returns a status for sentinel-aware code such as `pcall`. Rejected: exceptions as the primary mechanism (microseconds per throw, rising with stack depth; `try` regions cost the JIT registers for `pc`/`base`; throw cost not yet measured on .NET 11). Cost accepted: two error channels that must converge on one handler; tests cover both. Spec requirements: a CI microbenchmark of error-heavy loops (no pass/fail gate) and a Roslyn analyzer forbidding `try` in the dispatch method.

**Foreign exceptions (Q2).** An allow-list of host-thrown exceptions (`IOException`, `InvalidCastException`, ...) and `OutOfMemoryException` is converted at the outer wrapper to a Lua error whose value is the message string, with the original exception attached as `InnerException` for the host; `OutOfMemoryException` maps to "not enough memory". Engine-bug exceptions (`NullReferenceException`, `IndexOutOfRangeException`) are not converted and propagate as fatal, so bugs surface in the suite.

**Handler timing and tracebacks (Q3).** The `xpcall` handler runs before the `CallInfo` chain unwinds, so `debug.traceback` in the handler sees the faulting stack. An error in the handler gives "error in error handling". An uncaught error reaching a C# caller carries a traceback string captured at the throw site.

**Message fidelity (Q4).** Literal ports of `ldebug.c` and `lauxlib.c` logic: variable-info naming (`global`/`field`/`upvalue`/`local`/`method`/`constant`), `funcnamefromcode` for "bad argument #n to 'f'", "calling 'f' on bad self", exact arith/concat/compare/call/for wording. All stdlib argument checks go through shared `ArgError`/`TypeError` helpers, never ad-hoc strings. Differential testing checks the text.

**Coroutine and `<close>` error state (Q5).** An uncaught error in a coroutine leaves its `CallInfo` chain intact so `debug.traceback(co)` works; the coroutine is dead and TBC variables close only via `coroutine.close`. Inside `pcall`, TBC variables close in reverse order while unwinding; an error in a `__close` handler replaces the current error (the 5.4 semantics `locals.lua` tests).

**`debug` library scope (Q6).** Full 5.4 library: `getinfo` (all `nSltufrL`, incl. `activelines`, `istailcall`), `traceback`, `getlocal`/`setlocal` (varargs and temporaries), `getupvalue`/`setupvalue`/`upvalueid`/`upvaluejoin`, `sethook`/`gethook` (call, return, line, count; per coroutine), `get/setmetatable`, `getregistry`, `get/setuservalue`, `debug.debug` as a trivial stdin loop, `setcstacklimit` as a no-op. Nothing waived up front; waivers come only from failing suite files.

**Hook cost (Q7).** The dispatch loop is generated twice from one template by a source generator. The fast loop has no per-instruction hook test; it polls the interrupt/hook flag only on backward jumps and calls, and call/return hooks are handled on those paths. Installing a line or count hook switches the coroutine to the traced loop at the next poll, by returning out of `Execute` and re-entering. Both loops share the `CallInfo` and stack layout (the backend-seam rule from ticket 06). Rejected: a PUC-style `trap` check on every instruction fetch (a branch per instruction forever).

**Exhaustion (Q8).** Stack overflow at 1M slots is catchable as "stack overflow" with PUC's extra headroom for handlers; a second overflow in a handler gives "error in error handling"; the 200-level re-entrancy limit gives "C stack overflow"; "not enough memory" comes from the memory budget (ticket 07) or a converted `OutOfMemoryException`; the state stays usable best-effort afterwards.

**`error` and `warn` (Q9).** `error(msg, level)` prefixes `chunk:line:` only for string messages with `level > 0`; non-string values (including `nil`) pass through unchanged. `warn` matches PUC: off by default, `@on`/`@off`, `Lua warning: ...` on stderr, host-replaceable. The CLI prints `lua: msg` plus a traceback for uncaught errors and honours `__tostring`.

**Handed on.** The source-generated dual loop and the analyzer rules (no `try` in dispatch, single error-handler convergence) are further instances of the "Enforcement of code-discipline rules" fog item; the MinOpts/IL-budget check applies to both generated loops (research ticket on JIT MinOpts thresholds).
