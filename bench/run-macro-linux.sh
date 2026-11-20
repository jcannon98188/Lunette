#!/usr/bin/env bash
# Node-free macro benchmark runner for Linux / WSL: Lunette vs PUC Lua, startup measured separately and subtracted.
#
# usage: bench/run-macro-linux.sh [--runs N] [--only a,b] [--reference PATH] [--no-build] [--r2r] [--startup] [--engine-env "K=V K=V"]
#   --runs N          samples per benchmark, median reported (default 3), after one warm-up run
#   --only a,b        comma-separated benchmark names (default: all of bench/macro/*.lua)
#   --reference PATH  PUC lua binary (default: .reference/lua, then $LUA_REF, then `lua` on PATH)
#   --no-build        reuse an existing Release build instead of running `dotnet build -c Release src/Lunette.Cli`
#   --startup         only print the startup baseline (bench/startup.lua = `print(1)`) for both engines
#   --r2r             time a ReadyToRun publish of the CLI (startup ~30 ms lower, see bench/PROFILE.md) instead of the plain build
#   --engine-env ".." extra environment for the Lunette process only, e.g. "DOTNET_TieredPGO=0" (runtime-config experiments)
#
# Environment: BENCH_WORK=<dir> builds in a copy of the repo there instead of in place. Use this under WSL when the checkout is on
# /mnt/c, so Linux builds do not clobber the Windows obj/ and bin/ directories (file I/O across /mnt/c is also slow).
# Columns: wall = total process time; net = wall minus that engine's own startup baseline; ratio = Lunette net / PUC net.
set -euo pipefail

runs=3; only=""; ref=""; build=1; startup_only=0; engine_env=""; r2r=0
while [ $# -gt 0 ]; do
  case "$1" in
    --runs) runs="$2"; shift 2 ;;
    --only) only="$2"; shift 2 ;;
    --reference) ref="$2"; shift 2 ;;
    --no-build) build=0; shift ;;
    --startup) startup_only=1; shift ;;
    --r2r) r2r=1; shift ;;
    --engine-env) engine_env="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

src="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
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

if [ "$build" = 1 ]; then
  (cd "$root" && dotnet build -c Release src/Lunette.Cli --nologo -v q | tail -2)
fi
cli="${LUNETTE_CLI_DLL:-$root/src/Lunette.Cli/bin/Release/net11.0/Lunette.Cli.dll}" # override to time a published (e.g. ReadyToRun) build
if [ "$r2r" = 1 ]; then
  # ReadyToRun (precompiled) CLI: JIT-free startup for everything except the interpreter loop (AggressiveOptimization is never R2R).
  # Overrides the project's PublishAot=true for this publish only; the output still runs under `dotnet`.
  if [ "$build" = 1 ] || [ ! -f "$root/r2r-out/Lunette.Cli.dll" ]; then
    rid="$(dotnet --info | awk '/RID:/ { print $2; exit }')"
    (cd "$root" && dotnet publish src/Lunette.Cli -c Release -r "$rid" --self-contained false -p:PublishAot=false -p:PublishReadyToRun=true -o "$root/r2r-out" --nologo -v q | tail -1)
  fi
  cli="$root/r2r-out/Lunette.Cli.dll"
fi
[ -f "$cli" ] || { echo "missing $cli (build first)" >&2; exit 1; }

# time <cmd...>: wall milliseconds of one run, output discarded
now_ms() { echo $(( $(date +%s%N) / 1000000 )); }
run_ms() { local s e; s=$(now_ms); "$@" >/dev/null 2>&1 || { echo "FAIL: $*" >&2; }; e=$(now_ms); echo $(( e - s )); }
median() { sort -n | awk '{a[NR]=$1} END {print a[int((NR+1)/2)]}'; }
lunette() { if [ -n "$engine_env" ]; then env $engine_env dotnet "$cli" "$@"; else dotnet "$cli" "$@"; fi; }
sample() { # sample <engine-fn> <script>
  local fn="$1" script="$2"
  "$fn" "$script" >/dev/null 2>&1 || true # warm-up
  for _ in $(seq "$runs"); do run_ms "$fn" "$script"; done | median
}
puc() { "$ref" "$@"; }

ls_startup=$(sample lunette "$src/bench/startup.lua")
puc_startup=""
[ -n "$ref" ] && puc_startup=$(sample puc "$src/bench/startup.lua")
echo "startup (print(1)): lunette=${ls_startup}ms puc=${puc_startup:-n/a}ms${engine_env:+  [engine-env: $engine_env]}"
[ "$startup_only" = 1 ] && exit 0

printf '%-14s %9s %9s %9s %9s %7s\n' benchmark lunette puc lun-net puc-net ratio
for f in "$src"/bench/macro/*.lua; do
  n=$(basename "$f" .lua)
  if [ -n "$only" ] && [[ ",$only," != *",$n,"* ]]; then continue; fi
  l=$(sample lunette "$f")
  if [ -n "$ref" ]; then
    p=$(sample puc "$f")
    ln=$(( l - ls_startup )); pn=$(( p - puc_startup ))
    [ "$pn" -lt 1 ] && pn=1
    r=$(awk -v a="$ln" -v b="$pn" 'BEGIN { printf "%.2fx", a / b }')
    printf '%-14s %7dms %7dms %7dms %7dms %7s\n' "$n" "$l" "$p" "$ln" "$pn" "$r"
  else
    printf '%-14s %7dms %9s %7dms %9s %7s\n' "$n" "$l" n/a $(( l - ls_startup )) n/a n/a
  fi
done
