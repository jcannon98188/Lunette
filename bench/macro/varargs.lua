-- varargs: select('#',...), select(n,...), table.pack/unpack, vararg forwarding, multiple returns.
local lap_clock, lap_t = os.clock, os.clock() -- per-section time (ms) goes to stderr; stdout stays the checksum
local function lap(name) local t = lap_clock(); io.stderr:write(string.format("  %-34s %8.1f ms\n", name, (t - lap_t) * 1000)); lap_t = lap_clock() end
local N = tonumber(arg and arg[1]) or 1000000

local select, pack, unpack = select, table.pack, table.unpack

-- 1. select('#', ...) and select(n, ...)
local function count(...) return select('#', ...) end
local function nth(n, ...) return (select(n, ...)) end
local s1 = 0
for i = 1, N do s1 = s1 + count(i, i, i, i) + nth(3, i, i + 1, i + 2, i + 3) end

lap("1. select")
-- 2. loop over varargs with select (O(n^2) idiom)
local function sum(...)
  local t = 0
  for i = 1, select('#', ...) do t = t + (select(i, ...)) end
  return t
end
local s2 = 0
for i = 1, N // 3 do s2 = s2 + sum(i, 1, 2, 3, 4, 5) end

lap("2. loop over varargs with select")
-- 3. table.pack / table.unpack round trip
local function through(...)
  local t = pack(...)
  return unpack(t, 1, t.n)
end
local s3 = 0
for i = 1, N // 2 do
  local a, b, c = through(i, 2, 3)
  s3 = s3 + a + b + c
end

lap("3. table.pack / table.unpack round")
-- 4. vararg forwarding through several layers
local function l3(a, b, c) return a + b + c end
local function l2(...) return l3(...) end
local function l1(...) return l2(...) end
local s4 = 0
for i = 1, N do s4 = s4 + l1(i, 1, 2) end

lap("4. vararg forwarding through sever")
-- 5. multiple returns: produce, adjust, pass through
local function three(i) return i, i + 1, i + 2 end
local function pass(...) return ... end
local s5 = 0
for i = 1, N do
  local a, b, c = three(i)
  local x, y = pass(three(i))
  s5 = s5 + a + b + c + x + y
end

lap("5. multiple returns")
-- 6. {...} capture and #
local function capture(...) local t = { ... }; return #t end
local s6 = 0
for i = 1, N // 2 do s6 = s6 + capture(i, i, i, i, i) end

lap("6. {...} capture and #")
print(s1, s2, s3, s4, s5, s6)
