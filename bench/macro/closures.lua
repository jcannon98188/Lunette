-- closures: creation in a loop, open and closed upvalue read/write, counters, closure-built iterators, shared upvalues.
local lap_clock, lap_t = os.clock, os.clock() -- per-section time (ms) goes to stderr; stdout stays the checksum
local function lap(name) local t = lap_clock(); io.stderr:write(string.format("  %-34s %8.1f ms\n", name, (t - lap_t) * 1000)); lap_t = lap_clock() end
local N = tonumber(arg and arg[1]) or 2000000

-- 1. closure creation in a loop (captures a loop variable each time) and immediate call
local csum = 0
for i = 1, N do
  local f = function(x) return x + i end
  csum = csum + f(1)
end

lap("1. closure creation in a loop")
-- 2. open upvalue read/write: inner function runs while the enclosing frame is live
local function open_upvalues(n)
  local a, b = 0, 1
  local function step() a = a + b; b = b + 1 end
  for _ = 1, n do step() end
  return a
end
local osum = open_upvalues(N)

lap("2. open upvalue read/write")
-- 3. closed upvalues: counters whose frame is gone
local function counter()
  local c = 0
  return function() c = c + 1; return c end
end
local cs = {}
for i = 1, 10 do cs[i] = counter() end
local ksum = 0
for i = 1, N do ksum = ksum + cs[i % 10 + 1]() end

lap("3. closed upvalues")
-- 4. many closures sharing one upvalue
local function shared()
  local v = 0
  return function() v = v + 1 end, function() return v end
end
local inc, get = shared()
for _ = 1, N do inc() end
local shsum = get()

lap("4. many closures sharing one upval")
-- 5. closure-built iterator (stateful, no coroutine)
local function iter(n)
  local i = 0
  return function() i = i + 1; if i <= n then return i end end
end
local isum = 0
for x in iter(N) do isum = isum + x end

lap("5. closure-built iterator")
-- 6. creating closures that outlive the loop (table of closures, then called)
local fs = {}
for i = 1, N // 10 do
  local k = i * 2
  fs[i] = function() k = k + 1; return k end
end
local fsum = 0
for i = 1, #fs do fsum = fsum + fs[i]() end

lap("6. creating closures that outlive")
print(csum, osum, ksum, shsum, isum, fsum)
