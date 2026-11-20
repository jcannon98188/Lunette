---
kind: ticket
title: Platform interface, io and os
status: open
blocked_by: [6, 10]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20
---

## Question

### What to build

A host-replaceable platform interface under `io` and `os`: binary-always buffered files, `popen`/`execute` via `Process` behind capability flags, own `strftime`, CPU-time `os.clock`, "C"-only `setlocale`, strict-UTF-8 filenames and PUC-literal errno messages. Add Windows and macOS CI jobs running `files.lua` and `main.lua` with reason-coded platform waivers.

### Acceptance criteria

- [ ] `files.lua` and `main.lua` pass on Linux, and on Windows/macOS with platform waivers
- [ ] With capability flags off, `popen`/`execute` fail cleanly
- [ ] Added to the `required-files` ratchet
