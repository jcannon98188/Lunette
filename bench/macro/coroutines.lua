-- coroutines: resume/yield ping-pong, coroutine.wrap generators, producer/filter/consumer pipeline, create+run of short coroutines.
local lap_clock, lap_t = os.clock, os.clock() -- per-section time (ms) goes to stderr; stdout stays the checksum
local function lap(name) local t = lap_clock(); io.stderr:write(string.format("  %-34s %8.1f ms\n", name, (t - lap_t) * 1000)); lap_t = lap_clock() end
local N = tonumber(arg and arg[1]) or 1000000

-- 1. ping-pong: one coroutine, value passed both ways
local co = coroutine.create(function(a)
  local s = 0
  while true do
    s = s + a
    a = coroutine.yield(s & 0xffff)
  end
end)
local acc = 0
local v = 1
for i = 1, N do
  local _, r = coroutine.resume(co, i)
  acc = (acc + r) & 0xffffff
end

lap("1. ping-pong")
-- 2. generator via wrap, consumed by a for loop
local function range(n)
  return coroutine.wrap(function()
    for i = 1, n do coroutine.yield(i) end
  end)
end
local gsum = 0
for x in range(N) do gsum = gsum + x end

lap("2. generator via wrap")
-- 3. producer -> filter -> consumer pipeline (three coroutines)
local function producer(n)
  return coroutine.create(function()
    for i = 1, n do coroutine.yield(i) end
    return nil
  end)
end
local function filter(src)
  return coroutine.create(function()
    while true do
      local _, x = coroutine.resume(src)
      if x == nil then return nil end
      if x % 3 ~= 0 then coroutine.yield(x * 2) end
    end
  end)
end
local psum, pcount = 0, 0
local f = filter(producer(N // 2))
while true do
  local _, x = coroutine.resume(f)
  if x == nil then break end
  psum = psum + x; pcount = pcount + 1
end

lap("3. producer -> filter -> consumer")
-- 4. create + run to completion of many short-lived coroutines (creation cost)
local csum = 0
for i = 1, N // 10 do
  local c = coroutine.wrap(function(a, b) local t = coroutine.yield(a + b); return t * 2 end)
  csum = csum + c(i, 1) + c(3)
end

lap("4. create + run to completion of m")
print(acc, gsum, psum, pcount, csum)
