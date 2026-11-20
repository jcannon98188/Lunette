-- gc-latency: steady allocation churn against a large live heap. stdout is a deterministic checksum (comparable across engines);
-- throughput, per-batch pause percentiles and memory-per-object go to stderr (report-only; see bench/COVERAGE.md).
-- Pauses are measured as the wall (os.clock) time of each batch of BATCH churn iterations; a collector stall shows up as the tail
-- (p99 / max) relative to p50. os.clock is process CPU time under both engines; fine at ms scale.
local LIVE = tonumber(arg and arg[1]) or 100000   -- live tables kept reachable throughout
local ITER = tonumber(arg and arg[2]) or 400000   -- churn iterations
local BATCH = 100

local clock, collectgarbage, format = os.clock, collectgarbage, string.format
local function log(...) io.stderr:write(format(...), "\n") end

-- memory per object (after a full collect; KB * 1024 / count)
local function footprint(name, make, count)
  collectgarbage(); collectgarbage()
  local before = collectgarbage("count")
  local hold = {}
  for i = 1, count do hold[i] = make(i) end
  collectgarbage(); collectgarbage()
  local after = collectgarbage("count")
  local per = (after - before) * 1024 / count - 16 -- subtract the slot in `hold` (approx, 16 bytes/array slot in PUC)
  log("footprint %-22s %7.1f bytes/object", name, per)
  return #hold
end
local fcount = 0
fcount = fcount + footprint("empty table", function() return {} end, 100000)
fcount = fcount + footprint("table 4 array items", function(i) return { i, i, i, i } end, 100000)
fcount = fcount + footprint("table 4 hash fields", function(i) return { a = i, b = i, c = i, d = i } end, 100000)
fcount = fcount + footprint("string 16 bytes", function(i) return format("%016d", i) end, 100000)
fcount = fcount + footprint("closure 1 upvalue", function(i) return function() return i end end, 100000)

-- large live heap: tables with a few fields plus a string each
collectgarbage(); collectgarbage()
local live = {}
for i = 1, LIVE do live[i] = { id = i, name = "obj" .. i, tags = { i, i + 1 } } end
log("live heap: %d objects, %.1f MB", LIVE, collectgarbage("count") / 1024)

-- churn: replace a pseudo-random live object, plus short-lived garbage
local seed = 12345
local sum = 0
local pauses, np = {}, 0
local t_all = clock()
local t0 = clock()
for it = 1, ITER do
  seed = (seed * 1103515245 + 12345) & 0x7fffffff
  local slot = seed % LIVE + 1
  local old = live[slot]
  sum = (sum + old.id) & 0xffffff
  live[slot] = { id = it, name = "obj" .. it, tags = { it, it + 1 } }
  local junk = { it, it + 1, it + 2 }
  junk[4] = "x" .. (it & 15)
  if it % BATCH == 0 then
    local t1 = clock()
    np = np + 1; pauses[np] = t1 - t0
    t0 = clock()
  end
end
local total = clock() - t_all

table.sort(pauses)
local function pct(p) return pauses[math.max(1, math.floor(np * p + 0.5))] * 1000 end
log("churn: %d iterations in %.3f s (%.0f ns/iter)", ITER, total, total * 1e9 / ITER)
log("batch(%d iters) time ms: p50=%.3f p99=%.3f p99.9=%.3f max=%.3f  max/p50=%.1fx", BATCH, pct(0.5), pct(0.99), pct(0.999), pauses[np] * 1000, pauses[np] / pauses[math.max(1, np // 2)])

print(sum, fcount, #live)
