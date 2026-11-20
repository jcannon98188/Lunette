# One core assembly for compiler, VM, host API and stdlib

The compiler, VM, host API and full stdlib live in a single `Lunette` assembly, with Cli, Analyzers, Interop and optional IL satellites kept separate. Splitting the hot path across assemblies would block cross-assembly inlining and widen the public surface; one assembly keeps internals `internal` and lets PublicApiAnalyzers track a minimal API. Source: [Module and solution layout, and stdlib build order](../../.tasklog/lua-engine-spec/tickets/14-module-and-solution-layout-and-stdlib-build-order.md).
