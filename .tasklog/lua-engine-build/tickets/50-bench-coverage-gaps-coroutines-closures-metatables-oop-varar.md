---
kind: ticket
title: "Bench: coverage gaps (coroutines, closures, metatables/OOP, varargs, pcall/error, GC latency, strings, table lib, startup, footprint)"
status: open
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Where does Lunette lag native PUC (and Lua-in-WASM) on common scripting patterns the current macro set does not cover: coroutine switching, closures/upvalues, metatable/OOP dispatch, varargs/multiple returns, pcall/error, GC pause latency and footprint, string building, table library, warm-up and many-small-states? File a ticket for each gap larger than about 2.5x.
