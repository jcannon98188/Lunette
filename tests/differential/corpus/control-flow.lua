-- Integer and float for loops, goto, varargs, closures, multiple assignment, integer-keyed floats.
local buf = {}
local function write(...) for i = 1, select("#", ...) do buf[#buf + 1] = tostring((select(i, ...))) end end
local function endline() print(table.concat(buf)); buf = {} end
local function count(a, b, c)
  local n, last = 0, nil
  for i = a, b, c do n = n + 1; last = i; if n > 10 then break end end
  return n, last
end
print(count(1, 3, 1), count(3, 1, -1), count(1, 0, 1), count(1.0, 3, 1), count(1, 3.5, 1), count(1, 2, 0.5))
print(count(math.maxinteger - 2, math.maxinteger, 1), count(math.mininteger, math.mininteger + 2, 1))
print(count(math.maxinteger, math.maxinteger - 2, -1), count(1, math.huge, 1), count(1, -math.huge, -1))
print(count(math.mininteger, math.maxinteger, math.maxinteger), count(1, 3, math.maxinteger))
print(pcall(count, 1, 3, 0))
print(pcall(count, "a", 3, 1))
for i = 3, 1 do print("never") end
for i = 1.0, 3 do write(i, " ") end endline()
for i = 1, 3 do local j = i; i = i * 10; write(i, " ") end endline()
do
  local i = 1
  ::top::
  if i <= 3 then write("g", i, " "); i = i + 1; goto top end
  endline()
end
for i = 1, 3 do
  for j = 1, 3 do
    if j == 2 then goto next_i end
    write(i, ":", j, " ")
  end
  ::next_i::
end
endline()
local function va(...) return select("#", ...), (select(2, ...)), ... end
print(va(), va(nil), va(1, nil, nil))
print((va(1, 2, 3)))
local function tail(n) if n == 0 then return "done" end return tail(n - 1) end
print(tail(100000))
local fns = {}
for i = 1, 3 do fns[i] = function() i = i + 1; return i end end
print(fns[1](), fns[1](), fns[2](), fns[3]())
local x, y, z = 1, 2
x, y = y, x
print(x, y, z)
local t = {}
t[1.0], t[2^53] = "a", "b"
print(t[1], next(t) == 1, t[2^53 | 0], math.type(next(t)))
local k = 0
while true do k = k + 1; if k >= 5 then break end end
repeat local done = k > 5; k = k + 1 until done
print(k)
print(select(-1, 1, 2, 3), select(-2, 1, 2, 3))
print(pcall(function() return (select(0)) end))
print(type(print))
print(string.format("%s", 1 // 1), 2^31 | 0, "a" < "b" and "yes" or "no", nil == false, 0 == -0)
local s = 0
for _, v in ipairs({1, 2, nil, 4}) do s = s + v end
print(s)
local keys = {}
for k in pairs({10, 20, 30}) do keys[#keys + 1] = k end
print(table.concat(keys, ","))
local sorted = {}
for k in pairs({a = 1, b = 2, c = 3, d = 4}) do sorted[#sorted + 1] = k end
table.sort(sorted)
print(table.concat(sorted, ","))
print(next({}, nil), pcall(function() return (next({}, "missing")) end))
print(tostring(1e15 // 1), 7 // 0.0, -7 // 0.0, 0/0 ~= 0/0, math.huge // 1, -math.huge // 1)
print(#arg, arg[0])
