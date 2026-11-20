-- math.random with fixed seeds must produce the reference's xoshiro256** sequence.
math.randomseed(42)
local t = {}
for i = 1, 10 do t[#t + 1] = math.random(1, 100) end
print(table.concat(t, " "))
math.randomseed(42)
print(math.random(1, 100), math.random(0), math.random(), math.random(10), math.random(-5, 5))
math.randomseed(0)
print(math.random(0), math.random(0))
math.randomseed(123, 456)
print(math.random(1000000), math.random(), math.random(math.mininteger, math.maxinteger))
print(pcall(function() local r = math.random(1, 2, 3) return r end))
print(select("#", math.randomseed(7)))
