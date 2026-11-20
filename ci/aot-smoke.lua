-- Smoke script run under the Native AOT CLI: core features that must work with no dynamic code.
assert(load("return 1 + 1"))() -- compiling a chunk at run time uses the interpreter, not Reflection.Emit
assert(string.format("%5.2f|%q", 3.14159, "a\nb") == " 3.14|\"a\\\nb\"")
assert(("hello world"):gsub("o", "0") == "hell0 w0rld")
local co = coroutine.wrap(function(a) local b = coroutine.yield(a + 1); return b * 2 end)
assert(co(1) == 2 and co(5) == 10)
local ok, err = pcall(error, { code = 7 })
assert(not ok and err.code == 7)
local t = setmetatable({}, { __index = function(_, k) return k .. "!" end })
assert(t.x == "x!")
for i = 1, 3 do if i == 2 then goto continue end ::continue:: end
assert(math.type(2^53) == "float" and math.tointeger(2^53) == 9007199254740992)
print("aot-smoke ok")
