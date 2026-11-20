-- opcode: TFORCALL via pairs/ipairs
local t = {}
for i = 1, 20000 do t[i] = i; t["k" .. i] = i end
local s = 0
for _ = 1, 5 do
  for _, v in pairs(t) do s = s + v end
  for _, v in ipairs(t) do s = s + v end
end
return s
