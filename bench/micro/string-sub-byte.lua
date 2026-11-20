-- stdlib string: sub, byte, char, upper, rep
local s = string.rep("hello world ", 100)
local n = 0
for i = 1, 100000 do
  local j = i % 1000 + 1
  n = n + s:byte(j) + #s:sub(j, j + 5) + #string.char(65 + i % 26)
end
return n + #s:upper()
