-- varargs: VARARG and select
local function sum(...)
  local s = 0
  for i = 1, select("#", ...) do s = s + (select(i, ...)) end
  return s
end
local t = 0
for _ = 1, 30000 do t = t + sum(1, 2, 3, 4, 5) end
return t
