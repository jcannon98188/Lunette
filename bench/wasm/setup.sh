#!/usr/bin/env bash
# Installs the pinned toolchain for the Lua-in-WASM comparison under $WASM_TOOLS (default ~/lunette-wasm-tools). Linux/WSL only.
# Every download goes into its own new directory and is verified against a pinned SHA-256 before extraction.
#   Node.js  v26.13.1  (V8 for the node WASM engine; sha256 from https://nodejs.org/dist/v26.13.1/SHASUMS256.txt)
#   wasi-sdk 34.0      (clang -> wasm32-wasi; sha256 from the GitHub release asset digest)
#   wasmtime v51.0.0   (standalone runtime; sha256 from the GitHub release asset digest)
#   wasmoon  1.16.0    (npm; the registry's integrity hash is checked by npm itself, version pinned exactly)
# usage: bench/wasm/setup.sh
set -euo pipefail

tools="${WASM_TOOLS:-$HOME/lunette-wasm-tools}"
mkdir -p "$tools"

NODE_V=v26.13.1
NODE_SHA=5c063e46bab0fbc1c17cc445d7ff129cbdcd0471be6ed83b6ab864f8143bc864
WASISDK_TAG=wasi-sdk-34
WASISDK_V=34.0
WASISDK_SHA=b761e3a0721dbae9c09a0059e5fdb2bf917d1b4a8a7b430fb3b5aafb0984b2c4
WASMTIME_V=v51.0.0
WASMTIME_SHA=a18020674fac4107f329bc9ed720ea9c88e4b30a61e8a527787bcce051123d32
WASMOON_V=1.16.0

fetch() { # fetch <name> <url> <sha256> <tar-flags>  -> $tools/<name>/ (extracted, top directory stripped)
  local name="$1" url="$2" sha="$3" flags="$4" dl
  [ -d "$tools/$name" ] && return 0
  dl="$tools/dl-$name"; rm -rf "$dl"; mkdir -p "$dl"
  curl -fsSL "$url" -o "$dl/archive"
  echo "$sha  $dl/archive" | sha256sum -c - >&2
  mkdir -p "$tools/$name"
  tar "$flags" "$dl/archive" -C "$tools/$name" --strip-components=1
  rm -rf "$dl"
}

fetch node "https://nodejs.org/dist/${NODE_V}/node-${NODE_V}-linux-x64.tar.xz" "$NODE_SHA" -xJf
fetch wasi-sdk "https://github.com/WebAssembly/wasi-sdk/releases/download/${WASISDK_TAG}/wasi-sdk-${WASISDK_V}-x86_64-linux.tar.gz" "$WASISDK_SHA" -xzf
fetch wasmtime "https://github.com/bytecodealliance/wasmtime/releases/download/${WASMTIME_V}/wasmtime-${WASMTIME_V}-x86_64-linux.tar.xz" "$WASMTIME_SHA" -xJf

if [ ! -d "$tools/wasmoon/node_modules/wasmoon" ]; then
  mkdir -p "$tools/wasmoon"
  (cd "$tools/wasmoon" && PATH="$tools/node/bin:$PATH" npm install --no-audit --no-fund --save-exact "wasmoon@${WASMOON_V}" >&2)
fi

"$tools/node/bin/node" --version >&2
"$tools/wasmtime/wasmtime" --version >&2
"$tools/wasi-sdk/bin/clang" --version | head -1 >&2
echo "$tools"
