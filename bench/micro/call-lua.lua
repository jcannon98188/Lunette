-- opcode: CALL/RETURN of a Lua function
local function add(a, b) return a + b end
local s = 0
for i = 1, 300000 do s = add(s, i) end
return s
