# Heap-allocated frames instead of the .NET call stack

Lua-to-Lua calls, coroutines and `pcall` run in one `switch` loop over heap Frames, never by recursing on the .NET stack. This makes coroutines cheap, keeps deep recursion and `pcall` independent of .NET stack depth, and allows PUC's 1M-slot and 200-level limits. The cost is a more intricate VM loop and non-yieldable re-entrant paths (sort, gsub, load reader). Source: [Execution architecture and coroutine model](../../.tasklog/lua-engine-spec/tickets/06-execution-architecture-and-coroutine-model.md).
