-- Metamethods, error values and error messages (variable-name info included).
local mt = {}
mt.__add = function(a, b) return "add" end
mt.__concat = function(a, b) return "concat" end
mt.__len = function() return 7 end
mt.__eq = function() return true end
mt.__lt = function() return true end
mt.__le = function() return false end
mt.__call = function(self, ...) return "call", ... end
mt.__unm = function() return "unm" end
mt.__idiv = function() return "idiv" end
mt.__band = function() return "band" end
mt.__shl = function() return "shl" end
mt.__bnot = function() return "bnot" end
mt.__close = function() print("closed") end
mt.__tostring = function() return "OBJ" end
mt.__name = "MyType"
local a, b = setmetatable({}, mt), setmetatable({}, mt)
print(a + 1, 1 + a, a .. "x", "x" .. a, #a, a == b, a ~= b, a < b, a <= b, a > b, a(1, 2), -a, a // 1, a & 1, a << 1, ~a, tostring(a))
do local c <close> = a end
local idx = setmetatable({}, {__index = function(t, k) return k .. "!" end, __newindex = function(t, k, v) rawset(t, k, v * 2) end})
idx.x = 21
print(idx.x, idx.y, rawget(idx, "y"))
print(getmetatable("abc").__index == string, getmetatable(setmetatable({}, {__metatable = "locked"})))
print(pcall(function() return (setmetatable(setmetatable({}, {__metatable = 1}), {})) end))
local chain = setmetatable({}, {__index = setmetatable({a = 1}, {__index = {b = 2}})})
print(chain.a, chain.b, chain.c)
print(pcall(function() local x; return x.y end))
print(pcall(function() local x = {}; return x.y.z end))
print(pcall(function() return undefinedglobal.field end))
print(pcall(function() undefinedfunc() end))
print(pcall(function() local t = {} t.method() end))
print(pcall(function() return #5 end))
print(pcall(function() return {} + 1 end))
print(pcall(function() return {} .. "x" end))
print(pcall(function() return {} < {} end))
print(pcall(function() return 1 < {} end))
print(pcall(function() return nil > 1 end))
print(pcall(function() local t = setmetatable({}, {__newindex = "nope"}) t.x = 1 end))
print(pcall(function() for i = 1, "x" do end end))
print(pcall(function() for i = 1, 10, 0 do end end))
print(pcall(function() return (error()) end))
print(pcall(function() return (error(nil)) end))
print(pcall(function() return (error("msg", 0)) end))
print(pcall(function() return (error("msg", 1)) end))
print(pcall(function() return (error("msg", 2)) end))
print(pcall(function() return (error({code = 1})) end))
print(select(2, pcall(function() return (error(setmetatable({}, {__tostring = function() return "custom" end}))) end)) ~= nil)
print(xpcall(function() error("deep") end, function(m) return "handled: " .. m end))
print(xpcall(function(...) return ... end, print, 1, 2, 3))
print(pcall(function() return (assert(false)) end))
print(pcall(function() return (assert(false, "amsg")) end))
print(pcall(function() return (assert(nil, {1})) end))
print(pcall(function() return (assert()) end))
print(select("#", assert(1, 2, 3)))
print(pcall(function() return (setmetatable(1, {})) end))
print(pcall(function() return (tostring()) end))
print(tostring(print):match("^builtin") or tostring(print):match("^function"))
print(select(2, load("x = = 1")))
print(select(2, load("for")))
print(select(2, load("x = 'unfinished")))
print(select(2, load("goto nowhere")))
print(select(2, load("local x <const> = 1; x = 2")))
print(select(2, load("return 1 +", "=chunk")))
print(select(2, load("return ...", "@file.lua", "t", {})))
print(load("return 1", "c", "b"))
print(pcall(load("error('in chunk')", "=mychunk")))
print(pcall(load("error('in chunk')", "@some/file.lua")))
print(pcall(load("error('in chunk')", "line one\nline two")))
local co = coroutine.wrap(function() error("co err") end)
print(pcall(co))
print(coroutine.resume(coroutine.create(function() return 1, 2 end)))
local dead = coroutine.create(function() end)
coroutine.resume(dead)
print(coroutine.resume(dead))
print(coroutine.status(dead), coroutine.isyieldable(), select("#", coroutine.running()))
print(pcall(coroutine.yield))
