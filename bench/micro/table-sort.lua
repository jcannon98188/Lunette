-- stdlib table: sort with the default and a custom comparator
local t = {}
local x = 12345
for i = 1, 20000 do x = (x * 1103515245 + 12345) % 2147483648; t[i] = x end
local u = { table.unpack(t, 1, 5000) }
table.sort(t)
table.sort(u, function(a, b) return a > b end)
return t[1] + u[1]
