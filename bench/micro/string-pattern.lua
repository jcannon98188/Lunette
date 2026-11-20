-- stdlib string: find, match, gmatch, gsub (patterns)
local s = string.rep("the quick brown fox 123 jumps over 456 lazy dogs ", 200)
local n = 0
for _ = 1, 20 do
  n = n + select(2, s:gsub("%d+", "#"))
  for _ in s:gmatch("%a+") do n = n + 1 end
  n = n + (s:find("lazy dogs $") or 0) + #(s:match("(%d+) lazy") or "")
end
return n
