#!/usr/bin/env bash
# Builds PUC Lua 5.4.9 (the same checksummed tarball as ci/build-reference.sh) to a WASI module with the pinned wasi-sdk.
# The module (lua.wasm) runs under wasmtime and under Node's node:wasi (see run-node-wasi.mjs). Needs bench/wasm/setup.sh first.
# usage: bench/wasm/build-lua-wasm.sh [output-dir]   (default: $WASM_TOOLS/lua-wasm). Prints the lua.wasm path on stdout.
#
# Flags: -O2 like the native `make linux` build. setjmp/longjmp (lua's error mechanism) uses the WebAssembly exception-handling
# proposal with exnref (-mllvm -wasm-enable-sjlj -mllvm -wasm-use-legacy-eh=false + -lsetjmp), supported by V8 and wasmtime. LUA_USE_C89 keeps the build free of POSIX-only calls; os.tmpname is stubbed to fail (WASI has no tmpnam/mkstemp), which no bench program uses (system/tmpfile are stubbed in wasi-stubs.c).
set -euo pipefail

VERSION=5.4.9
URL="https://www.lua.org/ftp/lua-${VERSION}.tar.gz"
SHA256=2335b6c582a52654f94612bf10d2f4672805d05329aa6568b1d8cd9e5c6fb8e6

tools="${WASM_TOOLS:-$HOME/lunette-wasm-tools}"
out="${1:-$tools/lua-wasm}"
mkdir -p "$out"; out="$(cd "$out" && pwd)"
clang="$tools/wasi-sdk/bin/clang"
[ -x "$clang" ] || { echo "run bench/wasm/setup.sh first" >&2; exit 1; }

if [ ! -f "$out/lua.wasm" ]; then
  tarball="$out/lua-${VERSION}.tar.gz"
  [ -f "$tarball" ] || curl -fsSL "$URL" -o "$tarball"
  echo "$SHA256  $tarball" | sha256sum -c - >&2
  tar -xzf "$tarball" -C "$out"
  srcdir="$out/lua-${VERSION}/src"
  files=$(ls "$srcdir"/*.c | grep -v -E '/(luac|ltests)\.c$')
  # shellcheck disable=SC2086
  "$clang" --target=wasm32-wasip1 -O2 -DNDEBUG -DLUA_USE_C89 -DLUA_TMPNAMBUFSIZE=32 "-Dlua_tmpnam(b,e)=((e)=1)" -D_WASI_EMULATED_SIGNAL -D_WASI_EMULATED_PROCESS_CLOCKS -mllvm -wasm-enable-sjlj -mllvm -wasm-use-legacy-eh=false -mexception-handling \
    -Wno-deprecated-declarations "$(dirname "${BASH_SOURCE[0]}")/wasi-stubs.c" $files -o "$out/lua.wasm" -lsetjmp -lwasi-emulated-signal -lwasi-emulated-process-clocks >&2
fi
echo "$out/lua.wasm"
