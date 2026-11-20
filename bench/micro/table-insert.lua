-- stdlib table: insert/remove at the end, # length
local t = {}
for i = 1, 100000 do table.insert(t, i) end
local s = 0
for i = 1, 100000 do s = s + #t; table.remove(t) end
return s
