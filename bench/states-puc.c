/* Many-small-states benchmark for PUC Lua 5.4 (counterpart of bench/Lunette.Benchmarks/StateBenchmarks.cs).
 * Build (WSL/Linux): cc -O2 -I<lua-src> bench/states-puc.c <lua-src>/liblua.a -lm -ldl -o states-puc
 * Run: ./states-puc [states]   Prints microseconds per state for each shape (best of 7). */
#include <stdio.h>
#include <stdlib.h>
#include <time.h>
#include "lua.h"
#include "lauxlib.h"
#include "lualib.h"

static double now_us(void) {
  struct timespec ts;
  clock_gettime(CLOCK_MONOTONIC, &ts);
  return ts.tv_sec * 1e6 + ts.tv_nsec / 1e3;
}

static void bench(const char *name, const char *src, long states) {
  double best = 1e30;
  for (int rep = 0; rep < 7; rep++) {
    double t0 = now_us();
    for (long i = 0; i < states; i++) {
      lua_State *L = luaL_newstate();
      luaL_openlibs(L);
      if (src && luaL_dostring(L, src) != 0) { fprintf(stderr, "error: %s\n", lua_tostring(L, -1)); exit(1); }
      lua_close(L);
    }
    double per = (now_us() - t0) / states;
    if (per < best) best = per;
  }
  printf("%-24s %9.1f us/state\n", name, best);
}

int main(int argc, char **argv) {
  long states = argc > 1 ? atol(argv[1]) : 2000;
  bench("create+dispose", NULL, states);
  bench("create+run+dispose", "local t = {} for i = 1, 10 do t[i] = i end return #t", states);
  bench("create+fill+dispose", "local t = {} for i = 1, 1000 do t[i] = i end for i = 1, 200 do t['k' .. i] = 'v' .. i end", states);
  return 0;
}
