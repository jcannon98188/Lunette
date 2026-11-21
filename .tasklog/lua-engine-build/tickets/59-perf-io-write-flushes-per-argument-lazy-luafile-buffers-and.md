---
kind: ticket
title: "Perf: io.write flushes per argument; lazy LuaFile buffers and growable StringCache"
status: resolved
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-21T01:56:48Z
---

## Question

Can io.write to a redirected stdout stop flushing the host stream on every write (1e6 calls 2.0-2.7 s vs PUC 0.16-0.19 s; print keeps its flush; output byte-identical, interleaving with stderr and io.stdout:setvbuf semantics preserved), and can LuaFile read buffers be allocated lazily and StringCache grown on demand (about 57 KB of 101 KB per LuaState)? Source: audit-libs.md candidates 2 and 10.

## Answer

io.write no longer flushes per call (host stdout buffered; setvbuf modes honoured; flush at io.flush/close/Dispose/exit), lazy LuaFile read buffer, StringCache grows from 256. 1e6 io.write: 1.7-2.3s → 0.28-0.38s; state create/dispose 80→40us. Embedder contract documented. Tests in StdoutBufferingTests.cs.
