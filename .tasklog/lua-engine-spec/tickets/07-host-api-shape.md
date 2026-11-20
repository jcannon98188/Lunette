---
kind: ticket
title: Host API shape
status: open
type: grilling
blocked_by: [4, 5]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Decide the embedding API for .NET applications: state/chunk lifecycle, calling Lua from C# and C# from Lua, value conversion and marshalling, userdata and metatable exposure of .NET objects, sandboxing and resource limits, and the shape of the minimal `lua` CLI used as the test driver. Note how the string interop mode interacts with it.
