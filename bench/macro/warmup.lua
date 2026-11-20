-- warmup: time of the first iterations of a small script, to expose JIT / tiering warm-up. stdout is a deterministic checksum;
-- the per-chunk times (ms, one line to stderr) are the report. The work per chunk is fixed, so a flat curve means no warm-up cost;
-- Lunette's first chunks include tier-0 JIT of the interpreter loop and on-stack replacement.
local CHUNKS = tonumber(arg and arg[1]) or 40
local PER = tonumber(arg and arg[2]) or 20000

local function work(n)
  local t = {}
  for i = 1, n do t[i] = i * 2 end
  local s = 0
  for i = 1, #t do s = s + t[i] % 7 end
  local str = {}
  for i = 1, n // 20 do str[#str + 1] = tostring(i) end
  local function f(x) return x * 2 + 1 end
  for i = 1, n do s = s + f(i) end
  return s + #table.concat(str)
end

local clock = os.clock
local times, sum = {}, 0
local t_all = clock()
for c = 1, CHUNKS do
  local t0 = clock()
  sum = (sum + work(PER)) & 0xffffff
  times[c] = (clock() - t0) * 1000
end
local total = (clock() - t_all) * 1000

local out = {}
for c = 1, CHUNKS do out[c] = string.format("%.1f", times[c]) end
io.stderr:write("chunk ms: ", table.concat(out, " "), "\n")
local first5, last5 = 0, 0
for c = 1, 5 do first5 = first5 + times[c]; last5 = last5 + times[CHUNKS - 5 + c] end
io.stderr:write(string.format("first 5 chunks %.1f ms, last 5 chunks %.1f ms (warm-up penalty %.1f ms), total %.1f ms\n", first5, last5, first5 - last5, total))
print(sum)
