---
kind: ticket
title: Dev-mode diagnostics and lifecycle
status: open
type: task
created: 2026-11-20
updated: 2026-11-20
---

## Question

SimGame has an off-by-default dev mode in which mod packs can be reloaded, inspected and debugged. Requirements:

1. **Error locations and stack traces.** Every Lua error surfaced to the host carries chunk name, line and a structured stack trace (function names, source, line), without requiring the full `debug` library to be exposed to the script.
2. **Console evaluation.** The host can compile and run a snippet inside an existing pack's state (with its globals, upvalue-visible modules and budgets), returning values or a structured error, so a developer console can inspect live pack state.
3. **Discard-and-recreate lifecycle.** The host can dispose a state and create a fresh one for the same pack cheaply and repeatedly (hot reload), with disposal releasing all state-owned resources.

Source: SimGame foundation-spec "Decide the modding tools" (dev mode) and "Decide the Mod API surface".
