---
kind: ticket
title: "CI pipeline: suite, differential tests and gates"
status: resolved
type: grilling
blocked_by: [14, 17]
created: 2026-11-20
updated: 2026-11-20T03:18:30Z
---

## Question

How does CI run end to end: platform/OS matrix, building the pinned reference 5.4.9, running the unmodified official suite (per-file tests plus CLI `all.lua`), the waiver-manifest and ratchet (`required-files`) checks, differential testing against the reference binary (corpus, generators, comparison rules), the Native AOT CLI subset run, the JIT budget gate, analyzer/allocation-budget tests, and non-blocking benchmark reporting? Define job structure, runtimes, caching, and what blocks a merge.

## Answer

All decisions below were agreed with the user in two grilling rounds.

### Host and matrix
- GitHub Actions is the target. Workflows live in `.github/workflows`, shared steps are composite actions, and all logic lives in repo scripts (`dotnet`, `make`, `ci/*.sh`, `ci/*.ps1`), so the host is replaceable.
- Linux x64 is the full gate: suite, `all.lua`, differential tests, analyzers, allocation and JIT-budget tests, and the AOT run.
- Windows x64 and macOS arm64 run only the platform files (`files.lua`, `main.lua`, with reason-coded platform waivers from ticket 15) plus unit tests.
- Linux arm64 runs nightly, not per-PR.
- The .NET SDK is pinned (11.0.100) in `global.json`. A scheduled, non-blocking job runs the newest 11.x preview or servicing SDK to catch JIT regressions early.

### Reference build
- PUC Lua 5.4.9 is built from the pinned, checksum-verified source tarball with plain `make`, on Linux only, with no extra flags.
- The binary is cached keyed on version, tarball SHA and runner image.
- Windows and macOS do not run differential tests (the Windows reference binary is only 5.4.8, per ticket 1).

### Differential testing
- **Corpus:** a checked-in directory of `.lua` programs (hand-written semantic probes plus the suite's own chunks). Each runs through both binaries as separate processes.
- **Generators:** a seeded random program generator covering number-tower edge cases, string and pattern/format calls, table and `#t` behaviour, metamethods, goto and integer-for loops. PRs run a fixed seed set. Nightly runs fresh seeds, and any failing seed is minimised and committed to the corpus.
- **Comparison rules:** exit code, stdout and stderr byte for byte. Chunk names and source lines are normalised. These are normalised or excluded: addresses (`%p`, `tostring(table)`), `os.time` and `os.clock`, hash-part `pairs` order (compared as sorted sets), and GC counts. Any other divergence fails unless it is in the waiver manifest with a reason code.

### PR versus scheduled
- **Per-PR (Linux):** build, analyzers, unit and allocation tests, the JIT budget gate at 70% (ticket 17), the suite per-file with ratchet and waiver checks, `all.lua`, the differential corpus plus fixed seeds, and the AOT CLI subset. Target under about 15 minutes.
- **Per-PR (Windows, macOS):** the platform files.
- **Nightly:** fresh-seed generator runs, the capacity-1-cache and constant-hash variants (ticket 13), the safe-twin variant (ticket 17), Linux arm64, and the preview-SDK run.
- **Scheduled, non-blocking:** benchmarks per ticket 16.

### Job graph (per PR)
- `build`: Linux Release, uploaded as an artifact. Windows and macOS build for themselves.
- Parallel after build: `fast-checks` (analyzers with warnings-as-errors, PublicApiAnalyzers, suppression-ratchet check, unit and allocation tests), `jit-budget` (its own job, needing `DOTNET_JitStdOutFile` and JitDisasmSummary), `suite` (a matrix of 4 shards by file group, running the unmodified suite per file with waiver overlays applied, and checking that `required-files` hasn't shrunk), `all-lua` (a single CLI process, to catch cross-file state bleed), `differential` (needs the cached reference), `aot` (publishes the Native AOT CLI and runs a pinned suite subset), and `platform` (Windows and macOS).

### Merge gating
- Branch protection requires one aggregate check, `ci-gate`, which depends on every per-PR job above.
- Nightly, benchmark and preview-SDK jobs never block. A failure opens or updates a tracking issue.
- Additions to the waiver manifest need a `CODEOWNERS` review, so a waiver cannot ride in with a code change.

### Caching and artifacts
- Cached: the reference build (key above), NuGet (keyed on `Directory.Packages.props` plus lock files), and the pinned, checksum-verified 5.4.9 tests tarball.
- There is no build cache beyond NuGet, so a stale incremental build cannot hide a failure.
- On failure CI uploads each suite file's stdout and stderr, the minimised differential repro and the JIT disasm summary, kept for 14 days.

### AOT subset and flakiness
- The AOT job runs the suite files that need no dynamic-code or `io` waivers (the core language tier), plus a smoke script asserting `IsDynamicCodeSupported` is false and the IL backend is absent.
- Suite tests get no retries. The only exception is the GC-sensitive files (`gc.lua` overlays; `gengc.lua` is waived whole), which get one automatic retry. A test that needs a retry more than once in a rolling 30 days is flagged for a waiver or a fix.
- Job timeouts: suite shard 20 minutes, AOT 15 minutes, so a hung coroutine bug cannot stall the queue.
