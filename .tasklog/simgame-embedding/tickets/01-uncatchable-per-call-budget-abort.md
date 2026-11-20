---
kind: ticket
title: Uncatchable, per-call budget abort
status: open
type: task
created: 2026-11-20
updated: 2026-11-20
---

## Question

SimGame runs untrusted mod scripts and needs the Host to regain control within a bound. Requirements on the host API (see lua-engine-build/14, whose current acceptance says a limit breach "fails with a Lua error", which is catchable):

1. **Uncatchable abort.** An instruction-budget or memory-budget abort must not be catchable by `pcall`, `xpcall`, `coroutine.wrap`/`resume` or `__close`/`__gc` handlers; it unwinds straight to the host call that started the invocation.
2. **Per-call budget.** The instruction budget is settable per host-initiated call (one hook or event invocation), not only per state, plus a cumulative cap the host can apply across several calls (SimGame uses a per-tick cap per mod pack).
3. **Full memory count.** The memory cap counts every allocation Lunette makes for the state (tables, strings, closures, frames, stacks, userdata), as an upper bound, not tables and strings only.
4. **Discard after abort.** After any budget or memory abort the state is documented as unusable and must be discarded; the host never reuses it. The API makes the discard cheap and complete (no leaked handles or pinned memory).

Source: SimGame foundation-spec "Decide the Mod API surface", decision 14.
