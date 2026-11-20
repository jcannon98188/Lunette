-- Length of tables with holes: constructor patterns and assignment patterns. The reference's luaH_getn is deterministic for these.
local function show(label, t) print(label, #t) end
show("c1", {1, nil, 3})
show("c2", {nil, 2})
show("c3", {nil, nil, 3})
show("c4", {1, 2, nil, 4})
show("c5", {1, nil, nil, 4})
show("c6", {nil, nil, nil, 4})
show("c7", {1, 2, 3, nil, 5, nil})
show("c8", {1, 2, 3, nil, nil, 6})
show("c9", {1, 2, 3, 4, nil, nil, nil, 8})
show("c10", {nil, nil, nil, nil, nil, nil, nil, 8})
show("c11", {n = 1, 1, nil, 3})
show("c12", {1, 2, 3, nil})
show("c13", {1, 2, 3, nil, nil})
show("c14", {nil, nil, nil, nil})
show("c15", {[1] = 1, [2] = 2, [4] = 4})
show("c16", {[1] = 1, [2] = 2, [3] = 3, [5] = 5})
show("c17", {[2] = 2})
show("c18", {[1] = 1, [3] = 3})
show("c19", {10, 20, 30, [5] = 50})
show("c20", {10, 20, 30, [4] = 40, [6] = 60})
for n = 1, 12 do
  local t = {}
  for i = 1, n do t[i] = i end
  t[n] = nil
  show("pop" .. n, t)
  t[math.max(1, n // 2)] = nil
  show("mid" .. n, t)
end
for n = 1, 12 do
  local t = {}
  for i = 1, n do t[i] = i end
  t[1] = nil
  show("head" .. n, t)
end
for n = 2, 10 do
  local t = {}
  for i = 1, n do t[i] = i end
  for i = 2, n, 2 do t[i] = nil end
  show("evens" .. n, t)
end
for n = 2, 10 do
  local t = {}
  for i = n, 1, -1 do t[i] = i end
  show("rev" .. n, t)
end
for n = 1, 9 do
  local t = {}
  t[n] = n
  show("only" .. n, t)
end
do
  local t = {}
  for i = 1, 20 do t[i] = i end
  for i = 20, 11, -1 do t[i] = nil end
  show("shrink", t)
  for i = 10, 5, -1 do t[i] = nil end
  show("shrink2", t)
end
do
  local t = {1, 2, 3}
  t[5] = 5
  show("gap1", t)
  t[4] = 4
  show("gap2", t)
  t[#t + 1] = 6
  show("gap3", t)
end
do
  local t = {}
  for i = 1, 8 do t[i] = true end
  t[9] = nil
  t[8] = nil
  t[4] = nil
  show("mixed", t)
  table.insert(t, "x")
  show("mixed+ins", t)
end
