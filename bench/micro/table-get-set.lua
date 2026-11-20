-- opcode: GETFIELD/SETFIELD on a record table
local t = { a = 1, b = 2, c = 3 }
for i = 1, 200000 do t.a = t.b + i; t.c = t.a - t.b end
return t.c
