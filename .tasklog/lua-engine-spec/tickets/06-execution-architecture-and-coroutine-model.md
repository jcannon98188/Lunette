---
kind: ticket
title: Execution architecture and coroutine model
status: resolved
type: grilling
blocked_by: [5]
created: 2026-11-20
updated: 2026-11-20T02:42:06Z
---

## Question

Decide the execution pipeline: lexer/parser/compiler output (PUC-style register bytecode, other IR, or tree/closure compilation), the interpreter loop and dispatch, and the call stack as heap-allocated frames so coroutines need no threads and recursion does not depend on the .NET stack. Settle tail calls, varargs, `goto`, `<close>`/`<const>`, and how the design leaves room for a faster backend later without committing to one.

## Answer

All six proposals accepted by the user.

**Compiler and bytecode.** One-pass compiler from tokens straight to register bytecode (structure of PUC's `lparser`/`lcode`), no AST. Instruction set and formats follow PUC 5.4 (`iABC`, `iABx`, `iAsBx`, `iAx`, `isJ`, 32-bit words) with our own encoding; binary compatibility is out of scope. Rationale: variable-name error messages ("attempt to index a nil value (field 'x')") come from symbolic execution of the bytecode as in `ldebug.c`, and `debug`/line-info tests depend on that shape. `Proto` carries line info, local-variable ranges and upvalue names.

**Interpreter loop.** A single `Execute(Coroutine, CallInfo)` method with a `switch` over the opcode (jump table per research ticket 03). Hot state (pc, base, constants) in locals or `ref` locals into the stack array. Hot opcodes inlined (moves, arithmetic fast paths, table get/set fast paths, calls, jumps); cold and slow paths (metamethod fallbacks, concat, closures, varargs) in non-inlined helpers. Reason: the JIT drops a method to MinOpts past size thresholds (recalled as ~60 KB IL, ~2000 basic blocks, ~2000 locals; NOT yet verified, see the follow-up research ticket). Spec requires a CI check that `Execute` is not compiled under MinOpts and stays within an IL-size budget. `AggressiveOptimization` is the fallback (it disables tiering and OSR).

**Re-entrancy and yield.**
- Inside the loop, as pushed frames, yieldable: Lua-function metamethods, `__call`, the generic-`for` iterator call, `__close`. Completed by a `FinishOp`-style step as in `luaV_finishOp`. No .NET recursion.
- Interpreter-aware natives, yieldable: `pcall`, `xpcall`, `dofile`, implemented as frame-level constructs, not a .NET `try`/`catch` around a recursive call. The exact mechanism belongs to the error-model ticket.
- Re-entrant host calls via .NET recursion, non-yieldable ("attempt to yield across a C-call boundary", as in PUC): `table.sort` comparators, `string.gsub` callbacks, `load` readers, `__gc`, `tostring`'s `__tostring`. These count against the re-entrancy limit.
- How host functions in general may yield is left to the host API ticket.

**Coroutines and limits.** Each coroutine owns its stack, `CallInfo` chain, open-upvalue list and TBC list. `resume` nests a new `Execute` on the .NET stack; `yield` unwinds to that `resume` by returning out of `Execute` (safe because yield across .NET frames is forbidden). PUC's limits are spec constants: 1,000,000 Lua stack slots ("stack overflow", catchable); 200 levels of nested .NET re-entrancy covering resume, re-entrant host calls and parser recursion ("C stack overflow", "chunk has too many syntax levels"). Lua-to-Lua call depth never uses the .NET stack. Rejected: a trampolining scheduler (avoids the cap on resume chains but diverges from the reference and complicates nested `coroutine.wrap`).

**Frames and language features.** Reused `CallInfo` chain per coroutine (PUC's linked list, or a struct array; implementer's choice). A `CallInfo` holds func index, top, saved pc, nresults, a status flag (Lua / host / protected / fresh-from-.NET) and the closure. `OP_TAILCALL` reuses the frame (marked as a tail call for `debug`). 5.4-style `OP_VARARGPREP` copies the function and fixed parameters above the extra arguments; no per-call array for varargs. `goto` compiles to jumps with PUC's label and scope rules including the `<close>` interaction. `<const>` locals are folded at compile time and take no register. `<close>` uses a per-coroutine TBC list, closed deterministically in reverse order on normal exit, `break`/`goto`, error unwinding and coroutine close.

**Backend seam.** The bytecode `Proto` is the seam for any later backend. The spec commits to three things: a reserved slot on `Proto` for an optional compiled artifact; the call path checks that slot before entering `Execute`; any backend must use the same stack, `CallInfo` and upvalue layout so it can hand a frame back to the interpreter mid-function and yield, error and `debug` keep working. No backend is chosen; no speculative interfaces.

**Handed to other tickets.** Error and `pcall` internals go to "Error model, pcall and debug library scope". Whether to build any backend goes to "Native AOT, trimming and IL-compilation backend". Host-function yield protocol goes to "Host API shape".
