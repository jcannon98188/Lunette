-- table-lib: table.sort (default and comparator, ints and strings), insert/remove in the middle and at ends, table.move, table.unpack.
local lap_clock, lap_t = os.clock, os.clock() -- per-section time (ms) goes to stderr; stdout stays the checksum
local function lap(name) local t = lap_clock(); io.stderr:write(string.format("  %-34s %8.1f ms\n", name, (t - lap_t) * 1000)); lap_t = lap_clock() end
local N = tonumber(arg and arg[1]) or 150000

local function lcg_array(n, seed, mod)
  local t, s = {}, seed
  for i = 1, n do s = (s * 1103515245 + 12345) & 0x7fffffff; t[i] = s % mod end
  return t
end
local function checksum(t) local c = 0; for i = 1, #t, 97 do c = (c * 31 + t[i]) & 0xffffff end return c end

-- 1. sort integers, default comparison
local a = lcg_array(N, 1, 1000000)
table.sort(a)
local c1 = checksum(a)

lap("1. sort integers")
-- 2. sort integers with a comparator function (descending)
local b = lcg_array(N, 2, 1000000)
table.sort(b, function(x, y) return x > y end)
local c2 = checksum(b)

lap("2. sort integers with a comparator")
-- 3. sort strings
local words = {}
for i, v in ipairs(lcg_array(N // 3, 3, 1000000)) do words[i] = "w" .. v end
table.sort(words)
local c3 = #words[1] + #words[#words]

lap("3. sort strings")
-- 4. sort records by key through a comparator
local recs = {}
for i, v in ipairs(lcg_array(N // 3, 4, 1000000)) do recs[i] = { k = v, i = i } end
table.sort(recs, function(x, y) if x.k ~= y.k then return x.k < y.k end return x.i < y.i end)
local c4 = recs[1].k + recs[#recs].k

lap("4. sort records by key through a c")
-- 5. insert / remove in the middle of a moderately sized array (O(n) shifts) and at the ends
local m = {}
for i = 1, 2000 do m[i] = i end
local c5 = 0
for i = 1, N // 30 do
  table.insert(m, 1000, i)
  c5 = c5 + table.remove(m, 500)
end
for i = 1, N do m[#m + 1] = i; c5 = c5 + table.remove(m) end
for i = 1, N do table.insert(m, i) end
for i = 1, N do c5 = c5 + table.remove(m) end

lap("5. insert / remove in the middle o")
-- 6. table.move: shifting within and copying between tables
local src = {}
for i = 1, 5000 do src[i] = i end
local dst = {}
local c6 = 0
for i = 1, N // 100 do
  table.move(src, 1, 5000, 1, dst)
  table.move(dst, 1, 4999, 2)
  c6 = c6 + dst[2] + dst[5000]
end

lap("6. table.move")
-- 7. table.unpack with varying counts, and select-style reassembly
local small = { 1, 2, 3, 4, 5, 6, 7, 8 }
local c7 = 0
for i = 1, N * 4 do
  local x, y, z = table.unpack(small, 2, 4)
  c7 = c7 + x + y + z + select('#', table.unpack(small))
end

lap("7. table.unpack with varying count")
-- 8. removal churn: delete every other element from a 1000-element array, refill, repeat
local q = {}
for i = 1, 1000 do q[i] = i end
local c8 = 0
for r = 1, N // 600 do
  for i = #q, 1, -1 do if q[i] % 2 == r % 2 then table.remove(q, i) end end
  for i = 1, 1000 - #q do q[#q + 1] = i end
  c8 = c8 + #q
end

lap("8. removal churn")
print(c1, c2, c3, c4, c5, c6, c7, c8)
