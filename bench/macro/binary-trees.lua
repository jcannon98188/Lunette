-- binary-trees (Computer Language Benchmarks Game): table allocation and GC pressure.
local N = tonumber(arg and arg[1]) or 14

local function bottom_up(depth)
  if depth == 0 then return {} end
  depth = depth - 1
  return { bottom_up(depth), bottom_up(depth) }
end

local function item_check(tree)
  if tree[1] then return 1 + item_check(tree[1]) + item_check(tree[2]) end
  return 1
end

local mindepth = 4
local maxdepth = mindepth + 2
if maxdepth < N then maxdepth = N end

do
  local stretch = maxdepth + 1
  print(string.format("stretch tree of depth %d\t check: %d", stretch, item_check(bottom_up(stretch))))
end

local longlived = bottom_up(maxdepth)

for depth = mindepth, maxdepth, 2 do
  local iterations = 2 ^ (maxdepth - depth + mindepth)
  local check = 0
  for _ = 1, iterations do
    check = check + item_check(bottom_up(depth))
  end
  print(string.format("%d\t trees of depth %d\t check: %d", math.floor(iterations), depth, check))
end

print(string.format("long lived tree of depth %d\t check: %d", maxdepth, item_check(longlived)))
