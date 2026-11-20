-- Length operator on sequences and on the deterministic non-sequence cases that the reference exposes.
print(#{}, #{1}, #{1, 2, 3}, #{n = 1}, #{1, 2, nil}, #{1, nil, 3}, #{nil, nil, 3}, #{nil}, #{1, 2, nil, 4})
local t = {}
for i = 1, 10 do t[i] = i end
print(#t)
t[#t] = nil
print(#t)
t[5] = nil
print(#t == 9 or #t == 4)
local u = {10, 20, 30, nil, 50}
print(#u)
local v = {n = 3, 1, 2, 3}
print(#v, select("#", table.unpack(v)))
local w = {}
w[1], w[2], w[4] = 1, 2, 4
print(#w == 2 or #w == 4)
local s = {}
for i = 1, 100 do s[#s + 1] = i end
for i = 1, 50 do s[#s] = nil end
print(#s)
table.insert(s, 7)
table.insert(s, 1, 0)
print(#s, s[1], s[#s], table.remove(s), table.remove(s, 1), #s)
print(table.concat({1, 2, 3}, ", "), table.concat({}, "x"), table.concat({1, 2, 3}, "-", 2, 3))
print(table.unpack({1, 2, 3}, 2), table.unpack({1, 2, 3}, 2, 5))
print(select("#", table.unpack({}, 1, 3)))
print(table.pack(1, nil, 3).n, #table.pack())
local m = table.move({1, 2, 3}, 1, 3, 2)
print(m[1], m[2], m[3], m[4], #m)
local q = {5, 2, 8, 1, 9, 3}
table.sort(q)
print(table.concat(q, " "))
table.sort(q, function(a, b) return a > b end)
print(table.concat(q, " "))
print(pcall(function() return (table.sort({3, 2, "x"})) end))
print(next({}), type(next), rawlen({1, 2}), rawlen("abc"), rawequal("a", "a"))
print(#"abc", #"", #"\0\0")
