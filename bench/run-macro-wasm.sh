#!/usr/bin/env bash
# Report-only macro comparison: Lunette vs native PUC 5.4.9 vs PUC 5.4.9 compiled to WASM (node:wasi, wasmtime) vs wasmoon.
# All engines are measured in one session on one machine, interleaved (every round runs each engine once, in rotating order),
# wall time including process startup; startup is measured the same way with bench/startup.lua (`print(1)`) and subtracted.
# Output of every engine is compared with native PUC's; a mismatch is flagged in the table.
#
# usage: bench/run-macro-wasm.sh [--runs N] [--only a,b] [--reference PATH] [--no-build] [--no-wasmtime]
#   --runs N        samples per engine per benchmark, median reported (default 7), after one warm-up round
#   --only a,b      comma-separated benchmark names (default: all of bench/macro/*.lua)
#   --reference P   native PUC lua binary (default: .reference/lua, then $LUA_REF, then `lua` on PATH)
#   --no-build      reuse an existing Lunette Release build (and the existing $BENCH_WORK copy)
# Environment: LUNETTE_SHA=<sha> is only recorded in the header line.
#              BENCH_WORK=<dir> builds Lunette in a copy of the repo there (use under WSL when the checkout is on /mnt/c).
#              WASM_TOOLS=<dir> where bench/wasm/setup.sh installed the toolchain (default ~/lunette-wasm-tools).
# Setup: bench/wasm/setup.sh && bench/wasm/build-lua-wasm.sh (see bench/WASM.md). Needs `dotnet` on PATH.
set -euo pipefail

runs=7; only=""; ref=""; build=1; use_wasmtime=1
while [ $# -gt 0 ]; do
  case "$1" in
    --runs) runs="$2"; shift 2 ;;
    --only) only="$2"; shift 2 ;;
    --reference) ref="$2"; shift 2 ;;
    --no-build) build=0; shift ;;
    --no-wasmtime) use_wasmtime=0; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

src="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tools="${WASM_TOOLS:-$HOME/lunette-wasm-tools}"
root="$src"
if [ -n "${BENCH_WORK:-}" ]; then
  root="$BENCH_WORK"
  if [ "$build" = 1 ]; then
    rm -rf "$root"; mkdir -p "$root"
    tar -C "$src" --exclude=./.git --exclude=./.claude --exclude=./.tasklog --exclude=bin --exclude=obj --exclude=./tests/lua-suite -cf - . | tar -C "$root" -xf -
  fi
fi
if [ -z "$ref" ]; then
  if [ -x "$src/.reference/lua" ]; then ref="$src/.reference/lua"
  elif [ -n "${LUA_REF:-}" ]; then ref="$LUA_REF"
  else ref="$(command -v lua || true)"; fi
fi
[ -n "$ref" ] || { echo "no native PUC lua (see ci/build-reference.sh)" >&2; exit 1; }
if [ "$build" = 1 ]; then (cd "$root" && dotnet build -c Release src/Lunette.Cli --nologo -v q | tail -2); fi
cli="$root/src/Lunette.Cli/bin/Release/net11.0/Lunette.Cli.dll"
node="$tools/node/bin/node"; wasmtime="$tools/wasmtime/wasmtime"; wasm="$tools/lua-wasm/lua.wasm"
for f in "$cli" "$node" "$wasm"; do [ -f "$f" ] || { echo "missing $f" >&2; exit 1; }; done
[ -d "$tools/wasmoon/node_modules/wasmoon" ] || { echo "wasmoon not installed (bench/wasm/setup.sh)" >&2; exit 1; }
[ -x "$wasmtime" ] || use_wasmtime=0

engines="lunette puc wasm-node wasmoon"
[ "$use_wasmtime" = 1 ] && engines="lunette puc wasm-node wasm-wasmtime wasmoon"

run_engine() { # run_engine <engine> <script>
  case "$1" in
    lunette) dotnet "$cli" "$2" ;;
    puc) "$ref" "$2" ;;
    wasm-node) "$node" "$src/bench/wasm/run-node-wasi.mjs" "$wasm" "$2" ;;
    wasm-wasmtime) "$wasmtime" run -W exceptions=y --dir "$(dirname "$2")::/" "$wasm" "/$(basename "$2")" ;;
    wasmoon) "$node" "$src/bench/wasm/run-wasmoon.mjs" "$tools/wasmoon" "$2" ;;
  esac
}
now_ms() { echo $(( $(date +%s%N) / 1000000 )); }
median() { sort -n | awk '{a[NR]=$1} END {print a[int((NR+1)/2)]}'; }
spread() { sort -n | awk '{a[NR]=$1} END {printf "%d-%d", a[1], a[NR]}'; }

# measure <script> -> sets m_<engine> (median wall ms), r_<engine> (min-max), and ok_<engine> (output matches native)
measure() {
  local script="$1" e s t i k n=0 ref_out out
  ref_out=$("$ref" "$script" 2>/dev/null || true)
  for e in $engines; do
    out=$(run_engine "$e" "$script" 2>/dev/null || true) # warm-up (also the output check)
    if [ "$out" = "$ref_out" ]; then eval "ok_${e//-/_}=1"; else eval "ok_${e//-/_}=0"; fi
    eval "samples_${e//-/_}="
  done
  for i in $(seq "$runs"); do
    # rotate the engine order each round so no engine always follows (or precedes) the same neighbour
    set -- $engines; k=$(( (i - 1) % $# )); local order=("${@:$((k+1))}" "${@:1:$k}")
    for e in "${order[@]}"; do
      s=$(now_ms); run_engine "$e" "$script" >/dev/null 2>&1 || true; t=$(( $(now_ms) - s ))
      eval "samples_${e//-/_}=\"\$samples_${e//-/_} $t\""
    done
  done
  for e in $engines; do
    local v="samples_${e//-/_}"
    eval "m_${e//-/_}=\$(printf '%s\n' \${$v} | median)"
    eval "r_${e//-/_}=\$(printf '%s\n' \${$v} | spread)"
  done
}
get() { eval "echo \${$1_${2//-/_}}"; }

echo "# $(date -u +%FT%TZ)  host: $(uname -srm), $(nproc) cpus; runs=$runs (median, after warm-up, interleaved)"
echo "# lunette: ${LUNETTE_SHA:-unknown}; native: $("$ref" -v 2>&1 | head -1); node: $("$node" --version); wasmtime: $("$wasmtime" --version 2>/dev/null || echo n/a); wasmoon: $("$node" "$src/bench/wasm/run-wasmoon.mjs" "$tools/wasmoon" --version)"

measure "$src/bench/startup.lua"
for e in $engines; do eval "start_${e//-/_}=\$m_${e//-/_}"; done
printf 'startup wall ms (print(1)):'; for e in $engines; do printf ' %s=%s' "$e" "$(get m "$e")"; done; echo
echo
printf 'NET ms (wall minus own startup); "!" after a value = output differs from native PUC\n'
printf '%-14s' benchmark; for e in $engines; do printf ' %13s' "$e"; done; echo
for f in "$src"/bench/macro/*.lua; do
  n=$(basename "$f" .lua)
  if [ -n "$only" ] && [[ ",$only," != *",$n,"* ]]; then continue; fi
  measure "$f"
  printf '%-14s' "$n"
  for e in $engines; do
    net=$(( $(get m "$e") - $(get start "$e") )); flag=""; [ "$(get ok "$e")" = 1 ] || flag="!"
    printf ' %13s' "${net}${flag}"
  done
  echo
  printf '%-14s' "  (min-max wall)"; for e in $engines; do printf ' %13s' "$(get r "$e")"; done; echo
done
