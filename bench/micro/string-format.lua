-- stdlib string: format with several conversions, tostring/tonumber
local n = 0
for i = 1, 50000 do
  n = n + #string.format("%d:%5.2f:%s:%x", i, i / 3, "abc", i) + #tostring(i) + tonumber("12" .. (i % 10))
end
return n
