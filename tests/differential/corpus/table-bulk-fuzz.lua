-- Randomised table.insert/remove/move on tables with holes and mixed array/hash layouts (deterministic LCG, no math.random).
-- Each operation is also done with an element-wise loop on an identically built twin; the library result must equal the loop's result
-- (values, #t, traversal order), and the printed checksum must equal the reference's.
local seed = 12345
local function rnd(n)
  seed = (seed * 1103515245 + 12345) % 2147483648
  return seed // 65536 % n
end

local function build(spec)
  local t = {}
  for _, kv in ipairs(spec) do t[kv[1]] = kv[2] end
  return t
end

local function randspec()
  local spec = {}
  local n = rnd(24)
  local style = rnd(3)
  for i = 1, n do
    if rnd(5) ~= 0 then spec[#spec + 1] = { i, i * 7 } end
  end
  if style == 1 then
    for _ = 1, rnd(6) do spec[#spec + 1] = { n + 2 + rnd(30), "h" } end
  elseif style == 2 then
    for _ = 1, rnd(4) do spec[#spec + 1] = { -rnd(5), "neg" } end
  end
  return spec
end

local function signature(t)
  local parts = { "#" .. #t }
  for k, v in pairs(t) do parts[#parts + 1] = tostring(k) .. "=" .. tostring(v) end
  return table.concat(parts, ",")
end

local function loopinsert(t, pos, v)
  local e = #t + 1
  for i = e, pos + 1, -1 do t[i] = t[i - 1] end
  t[pos] = v
end

local function loopremove(t, pos)
  local size = #t
  local v = t[pos]
  while pos < size do t[pos] = t[pos + 1]; pos = pos + 1 end
  t[pos] = nil
  return v
end

local function loopmove(a1, f, e, t, a2)
  a2 = a2 or a1
  if e >= f then
    if t > e or t <= f or a1 ~= a2 then
      for i = 0, e - f do a2[t + i] = a1[f + i] end
    else
      for i = e - f, 0, -1 do a2[t + i] = a1[f + i] end
    end
  end
  return a2
end

local sum, bad = 0, 0
local function check(label, a, b)
  local sa, sb = signature(a), signature(b)
  if sa ~= sb then
    bad = bad + 1
    print("MISMATCH", label, sa, sb)
  end
  for i = 1, #sa do sum = (sum * 31 + sa:byte(i)) % 1000000007 end
end

for round = 1, 600 do
  local spec = randspec()
  local a, b = build(spec), build(spec)
  local op = rnd(3)
  if op == 0 then
    local n = #a
    local pos = 1 + rnd(n + 1)
    table.insert(a, pos, "I")
    loopinsert(b, pos, "I")
    check("insert " .. round .. " pos " .. pos, a, b)
  elseif op == 1 then
    local n = #a
    local pos = n == 0 and 0 or 1 + rnd(n)
    local ra = table.remove(a, pos)
    local rb = loopremove(b, pos)
    assert(ra == rb)
    check("remove " .. round .. " pos " .. pos, a, b)
  else
    local f = 1 + rnd(12)
    local e = f - 1 + rnd(14)
    local t = 1 + rnd(30) - 5
    if rnd(2) == 0 then
      table.move(a, f, e, t)
      loopmove(b, f, e, t)
      check("move " .. round, a, b)
    else
      local dspec = randspec()
      local da, db = build(dspec), build(dspec)
      table.move(a, f, e, t, da)
      loopmove(b, f, e, t, db)
      check("move2 " .. round, da, db)
    end
  end
end
print("bad", bad, "checksum", sum)
