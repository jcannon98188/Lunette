-- opcodes: integer and float arithmetic, comparison, bitwise
local i, f, b = 0, 0.5, 0
for k = 1, 300000 do
  i = i + k * 2 - (k // 3) + (k % 7)
  f = f + math.sqrt(k) * 0.5 - math.floor(f / 3)
  b = b ~ (((k << 3) | (k >> 2)) & 0xFFFF)
end
return i + b + math.floor(f)
