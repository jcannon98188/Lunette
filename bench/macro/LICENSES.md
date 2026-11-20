# Macro benchmark sources and licenses

Every script in this directory was written for this repository (original code, MIT, same license as Lunette), following the
algorithm and parameters of the named classic benchmark. None is copied from a third-party file, so no third-party license applies.
If a script is ever replaced by a vendored upstream copy, record the upstream URL, revision and license here.

| script | classic benchmark it follows | notes |
|---|---|---|
| fib.lua | recursive Fibonacci | |
| binary-trees.lua | Computer Language Benchmarks Game binary-trees | algorithm only; own implementation |
| n-body.lua | Computer Language Benchmarks Game n-body | standard solar-system constants (physical data) |
| spectral-norm.lua | Computer Language Benchmarks Game spectral-norm | |
| fannkuch.lua | fannkuch-redux | |
| mandelbrot.lua | mandelbrot | prints an escape-count checksum instead of a bitmap |
| nsieve.lua | nsieve | |
| k-nucleotide.lua | k-nucleotide | input generated in-script with an LCG (no input file) |
| string-heavy.lua | concat / gsub / format / find workload | |
| coroutines.lua | resume/yield ping-pong, wrap generators, producer/filter/consumer | coverage set, bench/COVERAGE.md |
| closures.lua | closure creation, open/closed upvalues, closure iterators | coverage set |
| oop.lua | metatable classes, `__index` chains, `__newindex`, `__call`, vector operators | coverage set |
| varargs.lua | `select`, `table.pack`/`unpack`, vararg forwarding, multiple returns | coverage set |
| pcall-error.lua | `pcall`/`error`/`xpcall` success and failure paths | coverage set |
| gc-latency.lua | allocation churn against a large live heap, pause percentiles, bytes per object | coverage set; timings on stderr |
| string-build.lua | `..`, `table.concat`, `string.format`, `tostring`, `tonumber` | coverage set |
| table-lib.lua | `table.sort`, insert/remove, `table.move`, `table.unpack` | coverage set |
| warmup.lua | per-chunk time of a small script (JIT warm-up curve) | coverage set; timings on stderr |

Each script takes an optional size as `arg[1]`; the defaults are sized for roughly a second on PUC Lua 5.4.9.
The official Lua test suite is not used as a benchmark.
