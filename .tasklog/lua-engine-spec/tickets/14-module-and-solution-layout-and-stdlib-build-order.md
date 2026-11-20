---
kind: ticket
title: Module and solution layout, and stdlib build order
status: open
type: grilling
created: 2026-11-20
updated: 2026-11-20
---

## Question

What is the project/assembly layout (core VM and compiler, stdlib, host API, CLI, test harness, analyzers, benchmarks, optional IL backend package) and the dependency rules between them? What is the build order for the stdlib (string patterns/format/pack, table, math, utf8, os, io, coroutine, debug) and how does it map to the tiered test-suite milestones from the test-harness decision? Cover namespaces, public vs internal surface, target frameworks, and package naming.
