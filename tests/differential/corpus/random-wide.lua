-- Wider math.random/randomseed coverage: many seeds, ranges, bounds and error cases (xoshiro256** and "project" in lmathlib.c).
local function run(seed, ...)
  math.randomseed(seed)
  local out = {}
  for i = 1, 6 do out[#out + 1] = tostring(math.random(...)) end
  print(seed, table.concat(out, " "))
end
for _, seed in ipairs { 0, 1, 2, 3, 7, 42, 100, 255, 256, 65535, 2^31, 2^53, 1e10, -1, -42, math.maxinteger, math.mininteger, 3.0, 12345678 } do
  run(seed, 1, 6)
  run(seed, 100)
  run(seed, 0)
  run(seed, -10, 10)
  run(seed, math.mininteger, math.maxinteger)
  run(seed, 0, math.maxinteger)
  run(seed, math.mininteger, 0)
  run(seed, 1, 2^40)
  run(seed, 5, 5)
  run(seed, 1 << 62)
end
for _, seed in ipairs { 1, 99, 2024 } do
  math.randomseed(seed)
  local f = {}
  for i = 1, 5 do f[#f + 1] = math.random() end
  print(seed, table.concat(f, " "))
end
for _, pair in ipairs { {1, 1}, {1, 2}, {0, 0}, {-1, 1}, {123456789, 987654321}, {math.maxinteger, math.mininteger} } do
  math.randomseed(pair[1], pair[2])
  print(pair[1], pair[2], math.random(0), math.random(1, 1000), math.random())
end
-- seed given as a float with an integer value, and as a float without one
math.randomseed(5.0)
local a = math.random(0)
math.randomseed(5)
print(a == math.random(0))
print(pcall(math.randomseed, 1.5))
print(pcall(math.randomseed, "x"))
print(pcall(math.random, 0, 0, 0))
print(pcall(math.random, 2, 1))
print(pcall(math.random, 1, 2, 3))
print(pcall(math.random, 1.5))
print(pcall(math.random, "a"))
print(pcall(math.random, math.maxinteger + 0.0))
print(pcall(math.random, 0.0))
print(pcall(math.random, -1))
print(pcall(math.random, 3, 2))
print(select("#", math.randomseed(1, 2)), select("#", math.randomseed()))
print(math.type(math.random(0)), math.type(math.random()), math.type(math.random(3)))
