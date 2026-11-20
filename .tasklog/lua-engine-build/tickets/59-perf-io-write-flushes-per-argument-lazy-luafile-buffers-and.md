---
kind: ticket
title: "Perf: io.write flushes per argument; lazy LuaFile buffers and growable StringCache"
status: open
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

Can io.write to a redirected stdout stop flushing the host stream on every write (1e6 calls 2.0-2.7 s vs PUC 0.16-0.19 s; print keeps its flush; output byte-identical, interleaving with stderr and io.stdout:setvbuf semantics preserved), and can LuaFile read buffers be allocated lazily and StringCache grown on demand (about 57 KB of 101 KB per LuaState)? Source: audit-libs.md candidates 2 and 10.
