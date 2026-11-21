-- table.insert(t, pos, v), table.remove(t, pos) and table.move on plain and metatable-backed tables: holes, overlaps, errors,
-- borders and traversal order must match the reference (ticket lua-engine-build/60: bulk array copies must not be observable).
local function dump(label, t, n)
  local parts = {}
  for i = -1, (n or #t) + 2 do parts[#parts + 1] = tostring(t[i]) end
  local keys = {}
  for k in pairs(t) do keys[#keys + 1] = tostring(k) end
  print(label, "#=" .. #t, table.concat(parts, ","), "order=" .. table.concat(keys, ","))
end
local function try(label, f, ...)
  local r = table.pack(pcall(f, ...))
  local out = {}
  for i = 1, r.n do
    local v = r[i]
    out[#out + 1] = type(v) == "table" and "table" or (tostring(v):gsub("^.-:%d+: ", ""))
  end
  print(label, table.concat(out, " | "))
end
local function seq(n) local t = {} for i = 1, n do t[i] = i end return t end

-- insert in the middle, front, end, beyond capacity
for _, n in ipairs { 0, 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 33 } do
  for _, pos in ipairs { 1, 2, n // 2 + 1, n, n + 1 } do
    if pos >= 1 and pos <= n + 1 then
      local t = seq(n)
      table.insert(t, pos, "x")
      dump("ins " .. n .. "@" .. pos, t)
    end
  end
end
-- insert with a constructor-sized array (capacity equals length) and with preallocated slack
do
  local t = { 1, 2, 3, 4 }
  for i = 1, 6 do table.insert(t, 1, -i) end
  dump("ins-front-grow", t)
  local u = { 1, 2, 3, 4, 5, 6, 7, 8 }
  u[8] = nil
  table.insert(u, 3, "y")
  dump("ins-slack", u)
end
-- remove from front, middle, end, and the pos == #t + 1 and #t == 0 forms
for _, n in ipairs { 0, 1, 2, 3, 5, 8, 9, 16, 17 } do
  for _, pos in ipairs { 0, 1, 2, n // 2 + 1, n, n + 1 } do
    local t = seq(n)
    local ok, v = pcall(table.remove, t, pos)
    print("rem " .. n .. "@" .. pos, ok, v)
    dump("rem-after", t)
  end
  local t = seq(n)
  print("rem-default " .. n, table.remove(t))
  dump("rem-default-after", t)
end
-- holes
do
  local t = { 1, 2, nil, 4, 5, nil, 7 }
  table.insert(t, 1, "h")
  dump("hole-ins", t, 9)
  local u = { 1, nil, 3, nil, 5, 6, 7, 8 }
  print("hole-rem", table.remove(u, 1), #u)
  dump("hole-rem-after", u, 9)
  local w = { nil, nil, 3 }
  print("hole-rem2", table.remove(w, 1), #w)
  dump("hole-rem2-after", w, 4)
  local m = table.move({ 1, nil, 3, nil, 5 }, 1, 5, 2)
  dump("hole-move", m, 7)
  local m2 = table.move({ 1, nil, 3, nil, 5 }, 2, 5, 1)
  dump("hole-move-back", m2, 7)
end
-- argument errors keep their messages
try("ins-oob-hi", table.insert, seq(3), 5, "x")
try("ins-oob-lo", table.insert, seq(3), 0, "x")
try("ins-oob-neg", table.insert, seq(3), -1, "x")
try("ins-nargs", table.insert, seq(3), 1, 2, 3)
try("ins-nargs1", table.insert, seq(3))
try("ins-pos-str", table.insert, seq(3), "a", 1)
try("ins-pos-float", table.insert, seq(3), 2.0, "f")
try("ins-pos-float2", table.insert, seq(3), 2.5, "f")
try("rem-oob-hi", table.remove, seq(3), 6)
try("rem-oob-lo", table.remove, seq(3), -1)
try("rem-pos-str", table.remove, seq(3), "x")
try("rem-nil-pos", table.remove, seq(3), nil)
try("mv-wrap", table.move, seq(3), 1, math.maxinteger, 2)
try("mv-wrap2", table.move, seq(3), 1, 3, math.maxinteger)
try("mv-nottable", table.move, 1, 1, 2, 3)
try("mv-nottable2", table.move, seq(2), 1, 2, 3, 5)
try("mv-empty", table.move, seq(3), 2, 1, 1)
-- move: overlap in both directions, other table, other destination offsets
for f = 1, 6 do
  for e = f - 1, 7 do
    for _, t in ipairs { 0, 1, 2, 3, 5, 7, 9, 12 } do
      local a = seq(7)
      local r = table.move(a, f, e, t)
      dump(("mv %d,%d->%d"):format(f, e, t), a)
      assert(r == a)
    end
  end
end
do
  local src, dst = seq(10), { "a", "b", "c" }
  local r = table.move(src, 3, 8, 2, dst)
  assert(r == dst)
  dump("mv-other", dst)
  dump("mv-src-intact", src)
  local d2 = {}
  table.move(seq(100), 1, 100, 1, d2)
  dump("mv-into-empty", d2)
  local d3 = {}
  table.move(seq(5), 1, 5, 10, d3)
  dump("mv-sparse-dest", d3, 16)
  local d4 = {}
  table.move(seq(5), 1, 5, -2, d4)
  dump("mv-neg-dest", d4, 5)
  local s5 = { [0] = "z", [-1] = "y", 1, 2, 3 }
  table.move(s5, -1, 3, 4)
  dump("mv-neg-src", s5, 9)
  local d6 = {}
  table.move({ n = 1, 1, 2, 3, [10] = 10 }, 1, 10, 1, d6)
  dump("mv-hash-src", d6, 12)
  local big = seq(1000)
  table.move(big, 1, 999, 2)
  table.move(big, 2, 1000, 1)
  dump("mv-big", big, 3)
  print(#big)
end
-- same table through two aliases (tt == t1), and a different empty table as destination equal by value only
do
  local a = seq(5)
  table.move(a, 1, 5, 3, a)
  dump("mv-alias", a)
  local b, c = seq(5), seq(5)
  table.move(b, 1, 4, 2, c)
  dump("mv-twin", c)
end
-- metatables: every element access honours __index/__newindex/__len, so the bulk path must not apply
do
  local log = {}
  local proxy = setmetatable({}, {
    __index = function(_, k) log[#log + 1] = "r" .. k return k * 10 end,
    __newindex = function(t, k, v) log[#log + 1] = "w" .. k .. "=" .. tostring(v) rawset(t, k, v) end,
    __len = function() return 4 end,
  })
  table.insert(proxy, 2, "p")
  print(table.concat(log, " "))
  log = {}
  print(table.remove(proxy, 1))
  print(table.concat(log, " "))
  log = {}
  table.move(proxy, 1, 3, 7)
  print(table.concat(log, " "))
  local base = { 1, 2, 3 }
  local inh = setmetatable({}, { __index = base })
  local r = table.move(inh, 1, 3, 1, {})
  dump("mv-inherit", r)
  local nw = setmetatable({}, { __newindex = function(t, k, v) rawset(t, k, v * 2) end })
  table.move({ 1, 2, 3 }, 1, 3, 1, nw)
  dump("mv-newindex", nw)
  table.insert(nw, 1, 100)
  dump("ins-newindex", nw)
  local ro = setmetatable({ 1, 2, 3 }, { __newindex = function() error("ro") end })
  try("ins-ro", table.insert, ro, 1, 0)
  try("rem-ro", table.remove, ro, 1)
  dump("ro-after", ro)
  local lenonly = setmetatable({ 1, 2, 3 }, { __len = function() return 2 end })
  table.insert(lenonly, 1, "L")
  dump("ins-lenonly", lenonly, 4)
  print(table.remove(lenonly, 1))
  dump("rem-lenonly", lenonly, 4)
end
-- border and traversal after many front inserts/removals (array grows and shrinks)
do
  local q = {}
  for i = 1, 200 do table.insert(q, 1, i) end
  print(#q, q[1], q[200])
  local s = 0
  while #q > 3 do s = s + table.remove(q, 1) end
  print(s, #q)
  dump("queue", q)
  for i = 1, 50 do q[#q + 1] = i end
  print(#q)
  for i = 1, 20 do table.remove(q, 2) end
  dump("queue2", q, 40)
end
-- values that are tables, strings, floats and booleans are moved as they are
do
  local o = {}
  local t = { o, "s", 1.5, true, false, 2 ^ 53 }
  table.insert(t, 3, o)
  print(t[1] == o, t[3] == o, t[4], t[5], t[6], t[7], #t)
  table.remove(t, 1)
  print(t[1], t[2] == o, t[3], t[4], t[5], #t)
end
