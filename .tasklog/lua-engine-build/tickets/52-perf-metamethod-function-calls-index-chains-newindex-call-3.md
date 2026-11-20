---
kind: ticket
title: "Perf: metamethod function calls (__index chains, __newindex, __call) 3-5.7x PUC"
status: resolved
blocked_by: [48, 51]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T21:38:45Z
---

## Question

Can __index-chain method calls, __newindex function proxies and __call objects be made cheaper (push a frame instead of a nested interpreter loop in Meta.CallMeta, cache absent-metamethod flags, inline-cache chain walks) to get oop.lua and the per-construct probes under ~2.5x PUC? See bench/COVERAGE.md.

## Answer

Lua metamethods start a Frame directly (StartMetaLua) with Return completing Value/Cond continuations inline; table-valued __index/__newindex chains are walked without building a MetaCall (Meta.IndexChainStr, NewIndexChain); __call objects have a ResolveCallable fast path; setmetatable is a CallFast builtin for the plain case (7fda119, merge ed5cb0e). Probe times vs PUC, before to after (ms, 2M iterations): 4-level method call 283 to 253 (PUC 90), __index function 196 to 143 (47), __add function 158 to 103 (44), __eq function 149 to 105 (49), __call object 151 to 141 (50); oop.lua 1357 to 1275 ms (PUC 562), about 2.3x. Remaining ratios of 2.8-3x are bounded by plain Lua call cost (about 2.3x PUC). Reverted: inline one-level __index in SelfK, cached __index accessor, first Return fast-path version. Not done: per-table cached __index slot (8 bytes per table) and global method inline cache. 17 new tests. Full suite green on the idle merged tree (1056 passed, 4 skipped); one gc.lua:473 differential failure seen under load by the agent is the known load-sensitive flake.
