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

Each script takes an optional size as `arg[1]`; the defaults are sized for roughly a second on PUC Lua 5.4.9.
The official Lua test suite is not used as a benchmark.
