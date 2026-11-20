-- k-nucleotide: string.sub and hash-table counting over a deterministic pseudo-random DNA sequence
-- (generated in-script with an LCG so no input file is needed).
local N = tonumber(arg and arg[1]) or 250000

local seed = 42
local letters = { "A", "C", "G", "T" }
local parts = {}
for i = 1, N do
  seed = (seed * 3877 + 29573) % 139968
  parts[i] = letters[(seed >> 3) % 4 + 1]
end
local seq = table.concat(parts)

local function count(k)
  local counts = {}
  for i = 1, #seq - k + 1 do
    local frag = seq:sub(i, i + k - 1)
    counts[frag] = (counts[frag] or 0) + 1
  end
  return counts
end

local function report(k)
  local counts = count(k)
  local keys = {}
  for frag in pairs(counts) do keys[#keys + 1] = frag end
  table.sort(keys, function(a, b)
    if counts[a] ~= counts[b] then return counts[a] > counts[b] end
    return a < b
  end)
  local total = #seq - k + 1
  for i = 1, math.min(#keys, 4) do
    print(string.format("%s %0.3f", keys[i], counts[keys[i]] * 100 / total))
  end
  print()
end

report(1)
report(2)
for _, frag in ipairs({ "GGT", "GGTA", "GGTATT", "GGTATTTTAATT" }) do
  print(string.format("%d\t%s", count(#frag)[frag] or 0, frag))
end
