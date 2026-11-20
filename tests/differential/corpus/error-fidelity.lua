-- Argument errors name the function from its call site; builtins reached through pcall use their global path;
-- builtins run as metamethods report the faulting line; parser errors name the opener of an unclosed bracket.
local sf = string.format
print(pcall(function() return sf("%d", {}) end))
print(pcall(function() local a = string.rep; return a() end))
print(pcall(function() local t = {f = string.rep}; return t.f() end))
print(pcall(function() local t = {f = string.rep}; return t:f() end))
print(pcall(function() return ("x"):rep({}) end))
print(pcall(function() return setmetatable({}, {__index = string.rep}).x end))
print(pcall(function() return setmetatable({}, {__add = math.floor}) + 1 end))
print(pcall(math.random, {}))
print(pcall(math.random, 1, 2, 3))
print(pcall(string.rep))
print(pcall(math.floor, {}))
local t = setmetatable({}, {__add = math.floor})
print(pcall(function()
  local a = 1
  local b = 2
  return t + a
end))

print(load("x = (1 + \n 2"))
print(load("f(1,\n 2"))
print(load("x = {1,\n 2"))
print(load("local t = {\n 1, 2\n y = 3"))
print(load("x = {1, 2 3}"))
print(load("function f(\n a, b\n x end"))

local ev = {}
local function hk(e)
  local i = debug.getinfo(2, "nS")
  ev[#ev + 1] = e .. ":" .. tostring(i.name) .. ":" .. i.what
end
debug.sethook(hk, "cr")
pcall(string.len, "x")
debug.sethook()
print(table.concat(ev, " "))
