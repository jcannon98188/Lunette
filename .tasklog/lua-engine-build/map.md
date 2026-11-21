---
kind: map
title: Build the .NET 11 Lua 5.4 engine
status: open
created: 2026-11-20
updated: 2026-11-20
---

## Destination

A pure-managed C# (.NET 11) Lua 5.4 engine and minimal CLI, built to the lua-engine-spec decisions and passing the pinned official 5.4.9 suite on Linux x64 (with reason-coded waivers only), gated by one aggregate ci-gate check.

## Notes

## Decisions so far

- [Walking skeleton](tickets/01-walking-skeleton.md): Walking skeleton landed.
- [Suite harness and ratchet](tickets/02-suite-harness-and-ratchet.md): Suite harness and ratchet landed.
- [Control flow, functions and closures](tickets/03-control-flow-functions-and-closures.md): Control flow and closures landed.
- [Tables and metatables](tickets/04-tables-and-metatables.md): Tables and metatables landed.
- [Number tower and math library](tickets/05-number-tower-and-math-library.md): Number tower and math landed.
- [Byte strings and string library](tickets/06-byte-strings-and-string-library.md): Byte strings and string lib landed.
- [Errors and pcall](tickets/07-errors-and-pcall.md): Errors and pcall landed.
- [Coroutines](tickets/08-coroutines.md): Coroutines landed.
- [goto, to-be-closed and const](tickets/09-goto-to-be-closed-and-const.md): goto, tbc and const landed.
- [Remaining base, table and load](tickets/10-remaining-base-table-and-load.md): Base, table and load landed.
- [debug library and hooks](tickets/11-debug-library-and-hooks.md): debug library and hooks landed.
- [GC: weak tables, __gc and collectgarbage](tickets/12-gc-weak-tables-gc-and-collectgarbage.md): GC landed.
- [Platform interface, io and os](tickets/13-platform-interface-io-and-os.md): Platform interface, io and os landed.
- [Host API and interop](tickets/14-host-api-and-interop.md): Host API landed.
- [Analyzers](tickets/15-analyzers.md): Analyzers landed.
- [Allocation and JIT-budget tests](tickets/16-allocation-and-jit-budget-tests.md): Alloc and JIT budget tests landed.
- [Differential testing](tickets/17-differential-testing.md): Differential testing landed.
- [Native AOT and trimming gate](tickets/18-native-aot-and-trimming-gate.md): AOT gate landed (unrun on CI).
- [Benchmarks](tickets/19-benchmarks.md): Benchmarks landed.
- [Full green and nightly variants](tickets/20-full-green-and-nightly-variants.md): Full green and nightly variants landed; CI unrun on GitHub.
- [Install math library globals, math.type and string coercion](tickets/21-install-math-library-globals-math-type-and-string-coercion.md): Math globals and coercion landed.
- [Close allocation and JIT-budget gaps](tickets/22-close-allocation-and-jit-budget-gaps.md): Alloc and JIT gaps closed.
- [SimGame: uncatchable per-call budget abort](tickets/23-simgame-uncatchable-per-call-budget-abort.md): Uncatchable budget abort landed.
- [SimGame: host-controlled globals, require and randomness](tickets/24-simgame-host-controlled-globals-require-and-randomness.md): Host-controlled globals landed.
- [SimGame: dev-mode diagnostics and lifecycle](tickets/25-simgame-dev-mode-diagnostics-and-lifecycle.md): Dev-mode diagnostics landed.
- [Yieldability gaps](tickets/26-yieldability-gaps.md): Yieldability gaps closed (two documented exceptions).
- [Error message and debug fidelity](tickets/27-error-message-and-debug-fidelity.md): Error/debug fidelity landed with documented gaps.
- [Raw byte output and resource limits polish](tickets/28-raw-byte-output-and-resource-limits-polish.md): Raw byte output and resource-limit polish landed.
- [Compiler and analyzer spec deviations](tickets/29-compiler-and-analyzer-spec-deviations.md): Compiler/analyzer deviations closed with two explicit waivers.
- [Test isolation and suite runner cleanup](tickets/30-test-isolation-and-suite-runner-cleanup.md): Test isolation landed.
- [CI shards, timeouts and unrun-CI verification](tickets/31-ci-shards-timeouts-and-unrun-ci-verification.md): CI hardening landed; workflows unrun on GitHub.
- [Verify behaviour against reference Lua 5.4.9](tickets/32-verify-behaviour-against-reference-lua-5-4-9.md): Reference verification landed.
- [GC suite failures: gc.lua, closure.lua, coroutine.lua close-handler frames](tickets/33-gc-suite-failures-gc-lua-closure-lua-coroutine-lua-close-han.md): GC suite failures closed (two waivers).
- [Sandbox allowlist: build states without registering withheld libraries](tickets/34-sandbox-allowlist-build-states-without-registering-withheld.md): Sandbox allowlist landed.
- [Fix differential-test divergences from reference 5.4.9](tickets/35-fix-differential-test-divergences-from-reference-5-4-9.md): Differential divergences fixed.
- [CI: add all-lua job and verify ci-gate covers every job](tickets/36-ci-add-all-lua-job-and-verify-ci-gate-covers-every-job.md): all-lua CI job landed.
- [Fix DoString inside a host function running under require](tickets/37-fix-dostring-inside-a-host-function-running-under-require.md): DoString re-entry fixed.
- [Suite: make db.lua, errors.lua, all.lua, locals.lua pass or waive](tickets/38-suite-make-db-lua-errors-lua-all-lua-locals-lua-pass-or-waiv.md): db/errors/all/locals suite files green.
- [Fix gc.lua timeout flake in the full test run](tickets/39-fix-gc-lua-timeout-flake-in-the-full-test-run.md): gc.lua full-run timeout fixed.
- [Perf: reproducible Linux macro-benchmark baseline vs PUC and profile](tickets/40-perf-reproducible-linux-macro-benchmark-baseline-vs-puc-and.md): Reproducible Linux macro baseline and profile (25a54e8)
- [Perf: arithmetic, comparison and numeric-for fast paths in the VM loop](tickets/41-perf-arithmetic-comparison-and-numeric-for-fast-paths-in-the.md): Arithmetic, comparison and for-loop fast paths
- [Perf: call/return and frame setup overhead](tickets/42-perf-call-return-and-frame-setup-overhead.md): Call/return fast paths (eac3b84)
- [Perf: table get/set, array part and GETFIELD/SETFIELD fast paths](tickets/43-perf-table-get-set-array-part-and-getfield-setfield-fast-pat.md): Table and field fast paths (83f7748)
- [Perf: string library, concatenation and interning costs](tickets/44-perf-string-library-concatenation-and-interning-costs.md): String library and interning costs (38c1227)
- [Bench: compare Lunette against Lua-in-WASM (PUC 5.4.9 compiled to WASM, wasmoon)](tickets/45-bench-compare-lunette-against-lua-in-wasm-puc-5-4-9-compiled.md): Lua-in-WASM comparison (2026eb9)
- [Safe-twin build (LUA_SAFE_ACCESS) for Lunette.Unsafe helpers and nightly variant](tickets/46-safe-twin-build-lua-safe-access-for-lunette-unsafe-helpers-a.md): Safe twin for Lunette.Unsafe (0f80501)
- [Perf: smaller RunLoop frame, cached closure state, builtin call protocol](tickets/47-perf-smaller-runloop-frame-cached-closure-state-builtin-call.md): RunLoop frame and builtin call protocol (5a4d0e7)
- [Bench: host-to-Lua call benchmark and CallValues ToArray cost](tickets/49-bench-host-to-lua-call-benchmark-and-callvalues-toarray-cost.md): Host-call benchmark; ToArray not worth fixing (e75cf62)
- [Bench: coverage gaps (coroutines, closures, metatables/OOP, varargs, pcall/error, GC latency, strings, table lib, startup, footprint)](tickets/50-bench-coverage-gaps-coroutines-closures-metatables-oop-varar.md): Benchmark coverage set and gap analysis (0d9ddbc)
- [Perf: fuse generic-for iterator sequence (Move x3 + Call + TForLoop)](tickets/48-perf-fuse-generic-for-iterator-sequence-move-x3-call-tforloo.md): Generic-for fusion for next/ipairs (8da5fcc)
- [Host debug hook API: LuaState.SetHook with C# delegate and GetInfo for debug adapters](tickets/51-host-debug-hook-api-luastate-sethook-with-c-delegate-and-get.md): Host debug hook API (1b8db4c)
- [Perf: VM runtime and string error raising 2.5-8x PUC (message building)](tickets/53-perf-vm-runtime-and-string-error-raising-2-5-8x-puc-message.md): Error raising without .NET throws (202df0d)
- [Perf: warm-up and GC pause tail (first-chunk JIT cost, gen0 p99)](tickets/54-perf-warm-up-and-gc-pause-tail-first-chunk-jit-cost-gen0-p99.md): Non-concurrent GC for CLI, AOT recommendation (de7fc97)
- [Perf: metamethod function calls (__index chains, __newindex, __call) 3-5.7x PUC](tickets/52-perf-metamethod-function-calls-index-chains-newindex-call-3.md): Metamethod call fast paths (7fda119)
- [Perf: varargs/select, closure iterators and builtin per-call overhead (unpack, tonumber, string.rep/sub/byte)](tickets/55-perf-varargs-select-closure-iterators-and-builtin-per-call-o.md): Vararg and builtin fast paths (915d09a)
- [Perf: io.write flushes per argument; lazy LuaFile buffers and growable StringCache](tickets/59-perf-io-write-flushes-per-argument-lazy-luafile-buffers-and.md): io.write flush removed, lazy buffers; 6x faster writes
- [Perf: load() compile is quadratic in constants (lazy hash index for constants and names)](tickets/56-perf-load-compile-is-quadratic-in-constants-lazy-hash-index.md): load() quadratic constant lookup fixed; compile metered
- [Perf: GC bookkeeping scaling (GcState.Close quadratic, weak entries via GCHandle)](tickets/58-perf-gc-bookkeeping-scaling-gcstate-close-quadratic-weak-ent.md): Quadratic Close fixed; GCHandle weak entries
- [Perf: frozen type tags and barrier-free scalar stores (core audit 1)](tickets/61-perf-frozen-type-tags-and-barrier-free-scalar-stores-core-au.md): Frozen tags + barrier-free stores; wrap error bug fixed

## Not yet specified

## Out of scope
