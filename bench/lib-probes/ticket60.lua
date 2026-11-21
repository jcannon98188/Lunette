-- Ticket 60 probes: bulk table.insert/remove/move and the small math/utf8/string library calls. Same file under PUC and Lunette.
-- usage: lua ticket60.lua [name-filter] ; prints "name<TAB>ms" (best of 3 in-process repetitions, so JIT warm-up is excluded).
local clock = os.clock
local only = arg and arg[1]
local function bench(name, f, reps)
  if only and not name:find(only, 1, true) then return end
  local best = 1e9
  for r = 1, (reps or 3) do
    local t = clock(); f(); local d = clock() - t
    if d < best then best = d end
  end
  io.stdout:write(string.format("%s\t%.2f\n", name, best * 1000))
end

-- ---------- table ----------
bench("insert front 2e4", function() local t = {}; local ins = table.insert; for i = 1, 20000 do ins(t, 1, i) end end)
bench("insert middle 2e4", function() local t = {}; local ins = table.insert; for i = 1, 20000 do ins(t, i // 2 + 1, i) end end)
bench("remove front 2e4", function() local t = {}; for i = 1, 20000 do t[i] = i end; local rm = table.remove; for i = 1, 20000 do rm(t, 1) end end)
bench("remove pop 1e6", function() local t = {}; for i = 1, 1000000 do t[i] = i end; local rm = table.remove; for i = 1, 1000000 do rm(t) end end)
bench("insert append 1e6", function() local t = {}; local ins = table.insert; for i = 1, 1000000 do ins(t, i) end end)
bench("move 1e6 into {}", function() local t = {}; for i = 1, 1000000 do t[i] = i end; local u = table.move(t, 1, 1000000, 1, {}) end)
bench("move shift 1e3 x 2e3", function() local t = {}; for i = 1, 1000 do t[i] = i end; local mv = table.move; for i = 1, 2000 do mv(t, 1, 999, 2); mv(t, 2, 1000, 1) end end)
bench("queue 100 push/pop", function()
  local q = {}; local ins, rm = table.insert, table.remove
  for i = 1, 100 do q[i] = i end
  for i = 1, 300000 do ins(q, i); rm(q, 1) end
end)

-- ---------- math / utf8 / string small calls ----------
local N = 3000000
local u = "h\xC3\xA4llo w\xE2\x82\xACrld \xF0\x9F\x98\x80!"
local line = "the quick brown fox"
bench("math.type", function() local f = math.type; for i = 1, N do local s = f(i) end end)
bench("math.tointeger", function() local f = math.tointeger; local x = 0; for i = 1, N do x = x + f(i + 0.0) end end)
bench("math.fmod", function() local f = math.fmod; local x = 0; for i = 1, N do x = x + f(i, 7) end end)
bench("math.fmod float", function() local f = math.fmod; local x = 0; for i = 1, N do x = x + f(i + 0.5, 7.25) end end)
bench("math.random()", function() local f = math.random; local x = 0; for i = 1, N do x = x + f() end end)
bench("math.random(n)", function() local f = math.random; local x = 0; for i = 1, N do x = x + f(100) end end)
bench("math.random(m,n)", function() local f = math.random; local x = 0; for i = 1, N do x = x + f(5, 100) end end)
bench("utf8.char 1", function() local f = utf8.char; for i = 1, N do local s = f(8364) end end)
bench("utf8.char 3", function() local f = utf8.char; for i = 1, N do local s = f(228, 8364, 0x1F600) end end)
bench("utf8.len", function() local f = utf8.len; for i = 1, N do local n = f(u) end end)
bench("utf8.codepoint 1", function() local f = utf8.codepoint; for i = 1, N do local n = f(u, 2) end end)
bench("utf8.offset", function() local f = utf8.offset; for i = 1, N do local n = f(u, 10) end end)
bench("utf8.codes", function() local f = utf8.codes; for i = 1, N // 10 do for p, c in f(u) do end end end)
bench("string.byte 1", function() local f = string.byte; for i = 1, N do local a = f(line, 5) end end)
bench("string.byte 1,4", function() local f = string.byte; for i = 1, N do local a, b, c, d = f(line, 1, 4) end end)
bench("string.byte neg", function() local f = string.byte; for i = 1, N do local a = f(line, -1) end end)
bench("string.char 1", function() local f = string.char; for i = 1, N do local s = f(72) end end)
bench("string.char 5", function() local f = string.char; for i = 1, N do local s = f(72, 101, 108, 108, 111) end end)
