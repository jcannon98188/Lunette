-- String library, patterns and string.format.
local buf = {}
local function write(...) for i = 1, select("#", ...) do buf[#buf + 1] = tostring((select(i, ...))) end end
local function endline() print(table.concat(buf)); buf = {} end
print(("hello"):upper(), ("HeLLo"):lower(), ("abc"):rep(3, ","), ("abc"):reverse(), ("abc"):len())
print(("hello"):sub(2, 3), ("hello"):sub(-3), ("hello"):sub(0), ("hello"):sub(10), ("hello"):sub(-100, 2), ("hello"):sub(2, -2))
print(("abc"):byte(1, -1), string.char(72, 105))
print(("hello world"):find("o w"), ("hello"):find("l", 1, true), ("hello"):find("xyz"), ("hello"):find(""), ("hello"):find("", 10))
print(("hello"):match("(h)(e)"), ("key=val"):match("(%w+)=(%w+)"), ("abc"):match("()b()"))
print(("hello world"):gsub("o", "0"), ("abc"):gsub("", "-"), ("abc"):gsub("%w", "%0%0"), ("abc"):gsub("b", {b = "X"}))
print(("hello"):gsub("l", function(c) return nil end), ("x y z"):gsub("%s", "_", 1))
for w in ("one two  three"):gmatch("%a+") do write(w, ";") end endline()
for k, v in ("a=1, b=2"):gmatch("(%w+)=(%w+)") do write(k, v, ";") end endline()
print(("THE (quick) fox"):find("%((%a+)%)"), ("f(a(b)c)d"):match("%b()"), ("THE END"):match("%f[%a]%a+$"))
print(string.format("%5d|%-5d|%05d|%+d|% d", 42, 42, 42, 42, 42))
print(string.format("%x %X %o %c %i", 255, 255, 8, 65, 7))
print(string.format("%s %10s %-10s| %.2s", "a", "b", "c", "xyz"))
print(string.format("%g %g %g %g", 1e20, 0.1, 100, 1e-5), string.format("%e", 12345.678), string.format("%.3f", 2/3))
print(string.format("%a", 1.0), string.format("%5.1f|%-8.3f|", 3.14159, 2.5))
print(string.format("%s %s %s", nil, true, {} ~= nil), string.format("%d%%", 50))
print(tostring(nil), tostring(true), tostring(12), tostring(1.5), tostring("s"))
print(("x"):rep(0), ("x"):rep(-1), ("ab"):rep(3), ("ab"):rep(2, "--"))
print(("abc") < ("abd"), ("a") < ("ab"), ("") < ("a"), ("Z") < ("a"), "10" < "9")
print(pcall(function() return 1 < "2" end))
print(("%d items"):format(3), #("\u{10FFFF}"), "\x41\65\z
       B", "tab\tq")
print(string.pack("<i4", 100):byte(1, -1))
print(string.unpack("<i4", string.pack("<i4", -2)))
print(string.packsize("i4i8"))
