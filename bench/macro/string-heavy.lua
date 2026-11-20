-- string-heavy: concatenation, string.format, gsub with string/table/function replacements, find, rep, upper.
local N = tonumber(arg and arg[1]) or 60000

local buf = {}
for i = 1, N do
  buf[#buf + 1] = string.format("item-%d:%s;", i, i % 3 == 0 and "fizz" or "x")
end
local joined = table.concat(buf)

local acc = ""
for i = 1, N // 20 do acc = acc .. i .. "," end

local swapped, n1 = joined:gsub("item", "elem")
local counted, n2 = joined:gsub("%d+", function(d) return #d end)
local mapped, n3 = joined:gsub("fizz", { fizz = "FIZZ" })

local finds = 0
local pos = 1
while true do
  local s, e = joined:find("fizz", pos, true)
  if not s then break end
  finds = finds + 1
  pos = e + 1
end

local words = 0
for _ in joined:gmatch("item%-%d+") do words = words + 1 end

local up = joined:upper():lower():reverse():len()
local rep = (("ab"):rep(N)):len()

print(#joined, #acc, #swapped, n1, #counted, n2, #mapped, n3, finds, words, up, rep)
