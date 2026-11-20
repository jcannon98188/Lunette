---
kind: ticket
title: "Bench: compare Lunette against Lua-in-WASM (PUC 5.4.9 compiled to WASM, wasmoon)"
status: resolved
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T19:45:24Z
---

## Question

Goal: see how Lunette stacks up against another VM-hosted embed target: real PUC Lua 5.4.9 compiled to WebAssembly and run in a WASM runtime. Report-only (never blocks ci-gate). Build/obtain, with pinned versions and checksums recorded: (1) PUC Lua 5.4.9 compiled to WASM (emscripten or wasi-sdk; the same tarball as ci/build-reference.sh) run under Node (V8, TurboFan) and, if obtainable, wasmtime; (2) wasmoon (pinned npm version; reports the Lua version it embeds—note if not 5.4.9). Run the same bench/macro/*.lua programs (wall time including engine startup, startup measured separately via a `print(1)` script and subtracted, median of N after warm-up, same machine and same session as Lunette and native PUC, output cross-checked to match). Extend bench/run-macro-linux.sh (WSL) and/or run-macro.mjs with `--wasm` engines, document setup in bench/README.md, and write a table (Lunette / native PUC / PUC-WASM(node) / PUC-WASM(wasmtime) / wasmoon, with ratios vs each) in bench/WASM.md including caveats (V8 JIT warmup, startup, memory limits, WASM lacks io/os parity so use programs that only print). Record the current Lunette numbers; ticket 41-43 may land meanwhile so state the commit SHA measured and re-run at the end if the tree changed. If a WASM build/tool cannot be obtained in WSL/Windows, say exactly which and why, and report the others.

## Answer

bench/run-macro-wasm.sh and bench/WASM.md compare Lunette with PUC 5.4.9 WASI under node and wasmtime, plus wasmoon; report-only (2026eb9). Before tickets 41-44, Lunette was 1.0-2.4x slower than PUC-WASM under node.
