---
kind: ticket
title: io and os portability on .NET
status: open
type: grilling
created: 2026-11-20
updated: 2026-11-20
---

## Question

How do the `io` and `os` libraries map onto .NET portably (Windows, Linux, macOS): file handles and buffering/seek/`setvbuf` semantics, byte-exact text vs binary mode and newline handling, `io.popen`/`os.execute` (process spawning, availability under AOT/sandbox), `os.clock`/`os.time`/`os.date` (time zones, strftime subset), `os.getenv`/`setlocale` against the ASCII "C" locale decision, `os.tmpname`/`remove`/`rename`, and the filename encoding boundary (UTF-8 vs Windows paths)? Which functions are optional or host-replaceable (sandbox ties to the host API decision), and which suite files (`files.lua`, `main.lua`) are affected?
