-- metamethods: __add and __eq dispatch
local mt = {}
mt.__add = function(a, b) return setmetatable({ v = a.v + b.v }, mt) end
mt.__eq = function(a, b) return a.v == b.v end
local a, b = setmetatable({ v = 1 }, mt), setmetatable({ v = 2 }, mt)
local n = 0
for _ = 1, 100000 do
  local c = a + b
  if c == b then n = n + 1 end
end
return n
