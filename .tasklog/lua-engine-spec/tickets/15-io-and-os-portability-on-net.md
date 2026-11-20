---
kind: ticket
title: io and os portability on .NET
status: resolved
type: grilling
created: 2026-11-20
updated: 2026-11-20T03:14:30Z
---

## Question

How do the `io` and `os` libraries map onto .NET portably (Windows, Linux, macOS): file handles and buffering/seek/`setvbuf` semantics, byte-exact text vs binary mode and newline handling, `io.popen`/`os.execute` (process spawning, availability under AOT/sandbox), `os.clock`/`os.time`/`os.date` (time zones, strftime subset), `os.getenv`/`setlocale` against the ASCII "C" locale decision, `os.tmpname`/`remove`/`rename`, and the filename encoding boundary (UTF-8 vs Windows paths)? Which functions are optional or host-replaceable (sandbox ties to the host API decision), and which suite files (`files.lua`, `main.lua`) are affected?

## Answer

Decided with the user (all seven grilling questions accepted as recommended).

1. **Platform seam**: `io`/`os` talk only to a small host-replaceable platform interface (file open/stat/remove/rename, process spawn, clock, environment, temp name). Default implementation over `System.IO` and `System.Diagnostics.Process`. Each capability (files, processes, environment, clock) can be removed independently for sandboxing; removed functions are absent from the library table or return `nil, msg` where PUC does.
2. **File handles**: own byte-exact buffered layer over `FileStream` (`bufferSize: 0`) implementing `setvbuf` no/full/line, all read formats (`n`,`l`,`L`,`a`,count), `seek`/`lines`/`close`, and the `__gc`/`__close` handle lifecycle. Binary-always: no newline translation on any OS, so `"r"` == `"rb"` (documented Windows deviation). Standard streams use raw OS handles; `io.stdout` line-buffered on a TTY, fully buffered otherwise.
3. **popen/execute**: `Process` with `/bin/sh -c` (Unix) or `cmd.exe /c` (Windows); popen supports `r`/`w` only; `close` returns the PUC triple `true|nil, "exit"|"signal", code` (128+n on Unix treated as signal); gated by the Q1 capability flag; `os.execute()` with no argument reports shell availability. AOT-safe.
4. **Time/date**: `os.time`/`os.date` over `TimeZoneInfo` with PUC `mktime`-style field normalisation and `isdst`; own strftime accepting exactly the C99 set PUC validates (`LUA_STRFTIMEOPTIONS`, incl. `E`/`O` modifiers), always "C" locale, invalid specifiers error as in PUC (identical output on all OSes, not `DateTime.ToString`); `os.clock` is process CPU time via a cached `Process.TotalProcessorTime`; `os.difftime` plain subtraction.
5. **Env/locale/files**: `os.getenv` returns bytes or nil; `os.setlocale` accepts only `nil`/`""`/`"C"`/`"POSIX"` and returns `"C"`, otherwise nil; `os.tmpname` creates a unique temp file and returns its path; `os.remove` removes files and empty directories (C `remove`); `os.rename` overwrites on every OS (`File.Move(overwrite: true)`); `os.exit(code, close)` with `close=true` closing the state, and in embedded use raising a host-visible exit request instead of killing the process (the CLI honours it).
6. **Filename encoding and errors**: filenames decoded as strict UTF-8; invalid UTF-8 fails with an `io` error rather than lossy replacement. `nil, msg, errno` from a mapping of `IOException`/`UnauthorizedAccessException`/HResult to the POSIX errno table and strerror strings (e.g. "No such file or directory", 2), keeping messages PUC-literal. Windows maps `ERROR_*`→errno; Unix uses the raw errno from the HResult; unmappable errors fall back to errno 0 plus the .NET message.
7. **Suite and CI**: `files.lua` runs unmodified on Linux with per-assertion overlays from the waiver manifest for platform-specific assertions (`setvbuf`/`tmpname` details, locale-dependent `os.date`, shell-dependent `popen`). `main.lua` runs via the CLI test driver (it shells out via `os.execute`/`popen`), with a documented Windows waiver for POSIX shell-quoting assertions, reason-coded in the manifest. CI matrix: Linux is the authoritative gate; Windows and macOS run the same suite with their own overlays and must pass `files.lua`/`main.lua` apart from listed platform waivers. Every waiver lists platform, assertion and reason code; none are silent.
