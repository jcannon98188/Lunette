# Errors as pending state, not exceptions

Lua errors are recorded as pending state and unwound inside the VM loop; .NET exceptions are only an ergonomic fallback caught outside the loop. Exception throw cost on common error paths (`pcall`, `error`) is unacceptable for a performance-shaped design, and exceptions would also tie unwinding to the .NET stack. Source: [Error model, pcall and debug library scope](../../.tasklog/lua-engine-spec/tickets/08-error-model-pcall-and-debug-library-scope.md).
