-- Times each bench/micro snippet inside PUC Lua (same chunk BenchmarkDotNet runs in-process for Lunette),
-- excluding startup and compilation. Usage: lua bench/puc-micro-harness.lua <micro-dir> name1 name2 ...
-- Prints "name<TAB>median-milliseconds" per snippet.
local dir = arg[1]
local WARMUP, REPS = 4, 12

for i = 2, #arg do
  local name = arg[i]
  local chunk = assert(loadfile(dir .. "/" .. name .. ".lua"))
  for _ = 1, WARMUP do chunk() end
  local samples = {}
  for r = 1, REPS do
    local t0 = os.clock()
    chunk()
    samples[r] = (os.clock() - t0) * 1000
  end
  table.sort(samples)
  local mid = REPS // 2
  print(string.format("%s\t%.4f", name, (samples[mid] + samples[mid + 1]) / 2))
end
