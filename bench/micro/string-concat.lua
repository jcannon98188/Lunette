-- strings: concatenation and table.concat
local s = ""
for i = 1, 5000 do s = s .. "x" .. i end
local t = {}
for i = 1, 50000 do t[#t + 1] = "n" .. i end
return #s + #table.concat(t, ",")
