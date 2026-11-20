---
kind: ticket
title: "Official Lua 5.4 test suite: contents and requirements"
status: resolved
type: research
created: 2026-11-20
updated: 2026-11-20T02:32:06Z
---

## Question

Establish the facts about the official Lua 5.4 test suite (`lua-5.4.x-tests`): where to get it and which version matches the latest 5.4.x, its license, its file list with what each file covers, how it is run (`all.lua`), and every dependency it has on things a managed engine cannot offer as-is: the `T` test library (needs a special C build), `debug` use, `io`/`os` use, `_port`/`_soft` flags, locale, and GC-accounting checks. Also identify other reputable corpora worth adding (e.g. Lua's own `testes` extras, third-party conformance suites) and the reference `lua` 5.4 binary's availability on Windows. Use only primary sources (lua.org, the official repo). Output: facts, not decisions.

## Answer

Findings are on the throwaway branch `research/lua-test-suite` (file `research/lua-test-suite.md`, pushed to origin, no PR).

- Latest 5.4.x is 5.4.9; suite is `lua-5.4.9-tests.tar.gz` from lua.org/tests, MIT licence (notice in `all.lua`), not cross-version, unsupported by the authors. The lua.org tarball URL for 5.4.9 source was not verified.
- 33 `.lua` files (~16.6k lines) plus `libs/` and `ltests/`; driver is `all.lua`, run as `lua -e"_U=true" all.lua` (forces `_soft`/`_port`, sets `T=nil`); success prints "final OK". The driver runs most files through `string.dump` and `load`; `heavy.lua` is excluded from the run.
- The `T` library exists only in a special C build (`ltests.c`, `LUA_USER_H`); `api.lua` is essentially all `T.testC`; `T` is also used in code, gengc, gc, coroutine, locals, cstack. Without `T` those parts skip.
- Other dependencies: `debug` in most files, heavy `io`/`os` in `files.lua`, `os.setlocale` in several files, C-module loading from `libs/`, `main.lua` needs a Unix-like shell (`env`, `cat`), numeric `collectgarbage("count")` asserts in `gc.lua` and `api.lua`.
- Other corpora named, not verified for licence/lineage: Luau `tests/conformance`, Fengari (Lua 5.3).
- No official Windows binary from lua.org; LuaBinaries (SourceForge) latest 5.4 build is 5.4.8, not 5.4.9.
