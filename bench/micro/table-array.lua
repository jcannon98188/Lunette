-- opcode: GETTABLE/SETTABLE with integer keys (array part)
local t = {}
for i = 1, 100000 do t[i] = i end
local s = 0
for i = 1, 100000 do s = s + t[i] end
return s
