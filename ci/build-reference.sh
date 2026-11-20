#!/usr/bin/env bash
# Builds the Reference binary: PUC Lua 5.4.9 from the checksummed source tarball, plain `make`, Linux only.
# Usage: ci/build-reference.sh [output-dir]   (default: .reference). Prints nothing but the binary path on stdout.
set -euo pipefail

VERSION=5.4.9
URL="https://www.lua.org/ftp/lua-${VERSION}.tar.gz"
SHA256=2335b6c582a52654f94612bf10d2f4672805d05329aa6568b1d8cd9e5c6fb8e6

out="${1:-.reference}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"

if [ ! -x "$out/lua" ]; then
  tarball="$out/lua-${VERSION}.tar.gz"
  [ -f "$tarball" ] || curl -fsSL "$URL" -o "$tarball"
  echo "$SHA256  $tarball" | sha256sum -c - >&2
  tar -xzf "$tarball" -C "$out"
  make -C "$out/lua-${VERSION}" -j"$(nproc)" linux >&2
  cp "$out/lua-${VERSION}/src/lua" "$out/lua"
fi
"$out/lua" -v >&2
echo "$out/lua"
