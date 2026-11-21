local s = "hello"
print(s:byte(-1), s:byte(-5), s:byte(-6), s:byte(0), s:byte(6), s:byte(5), s:byte(1, 3))
print(select('#', s:byte(1, 0)), select('#', s:byte(3, 100)), select('#', s:byte(-3, -1)), select('#', s:byte(2, -10)))
print(s:byte(1, -1))
print(select('#', ("x"):rep(20):byte(1, 20)), pcall(string.byte, s, 1.5), pcall(string.byte, s, "2"))
local a, b, c, d = s:byte(2, 3)
print(a, b, c, d)
print((s:byte(1, 4)))
local function tail(...) return string.byte(...) end
print(tail(s, 1, 3))
print(string.char(104, 105), string.char(), #string.char(0, 255), pcall(string.char, 256), pcall(string.char, -1), pcall(string.char, 65.5), string.char(65.0, "66"))
print(string.char(1, 2, 3, 4, 5, 6, 7, 8, 9) == "\1\2\3\4\5\6\7\8\9", string.char(72) == "H")
print(math.type(1), math.type(1.5), math.type("1"), math.type(nil), math.type({}), pcall(math.type))
print(math.tointeger(3), math.tointeger(3.0), math.tointeger(3.5), math.tointeger("8"), math.tointeger("x"), math.tointeger({}), math.tointeger(2^63), pcall(math.tointeger))
print(math.fmod(7, 3), math.fmod(-7, 3), math.fmod(7, -3), math.fmod(-7, -3), math.fmod(7, -1), math.fmod(math.mininteger, -1))
print(math.fmod(7.5, 2), math.fmod(-7, 3.5), math.fmod(7, 0.0), math.fmod(1, math.huge), math.fmod(math.huge, 2) ~= math.fmod(math.huge, 2))
print(pcall(math.fmod, 1, 0), pcall(math.fmod, 1), pcall(math.fmod, "a", 2), math.fmod("7", 3))
print(math.fmod(math.maxinteger, 2), math.fmod(5, 5.0), math.fmod(-6, 2), math.fmod(-6.0, 2))
local t = { math.type(1), math.type(2.0) }
print(t[1], t[2], math.type(1) == "integer", select('#', math.type(1)))

