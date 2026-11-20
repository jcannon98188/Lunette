# 16-byte LuaValue as `{object? Ref; long Bits}`

Every Lua value is one 16-byte struct whose type is derived from `Ref`, with number payloads in `Bits`. .NET 11 throws `TypeLoadException` when an object reference overlaps a non-reference field, so NaN-boxing-style overlap is impossible; an extra tag field would grow the struct to 24 bytes. Source: [Value representation and number tower](../../.tasklog/lua-engine-spec/tickets/05-value-representation-and-number-tower.md).
