/* Embedder-style host-to-Lua call benchmark for PUC Lua 5.4 (counterpart of bench/Lunette.Benchmarks/HostCallBenchmarks.cs).
 * Build (WSL/Linux): cc -O2 -I<lua-src> bench/embed-puc.c <lua-src>/liblua.a -lm -ldl -o embed-puc
 * Run: ./embed-puc [calls]   Prints ns per call for each shape. */
#include <stdio.h>
#include <stdlib.h>
#include <time.h>
#include "lua.h"
#include "lauxlib.h"
#include "lualib.h"

static double now_ns(void) {
  struct timespec ts;
  clock_gettime(CLOCK_MONOTONIC, &ts);
  return ts.tv_sec * 1e9 + ts.tv_nsec;
}

static void bench(lua_State *L, const char *name, const char *src, int nargs, int nres, long calls) {
  luaL_loadstring(L, src);
  lua_call(L, 0, 1);
  int fn = lua_gettop(L);
  double best = 1e30;
  for (int rep = 0; rep < 7; rep++) {
    double t0 = now_ns();
    for (long i = 0; i < calls; i++) {
      lua_pushvalue(L, fn);
      for (int a = 0; a < nargs; a++) lua_pushinteger(L, i + a);
      lua_call(L, nargs, nres);
      lua_pop(L, nres);
    }
    double per = (now_ns() - t0) / calls;
    if (per < best) best = per;
  }
  printf("%-28s %8.1f ns/call\n", name, best);
  lua_settop(L, fn - 1);
}

int main(int argc, char **argv) {
  long calls = argc > 1 ? atol(argv[1]) : 5000000;
  lua_State *L = luaL_newstate();
  luaL_openlibs(L);
  bench(L, "call0-ret1", "return function() return 1 end", 0, 1, calls);
  bench(L, "call3-ret1", "return function(a, b, c) return a + b end", 3, 1, calls);
  bench(L, "call3-ret3", "return function(a, b, c) return a + b, a * b, c end", 3, 3, calls);
  lua_close(L);
  return 0;
}
