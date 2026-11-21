-- Table lib, sort, hash tables, string keys, load(), io, memory, GC probes. "name<TAB>ms" (best of N) to stdout.
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
local function info(name, v)
  if only and not name:find(only, 1, true) then return end
  io.stdout:write(string.format("%s\t%s\n", name, tostring(v)))
end

-- ---------- load() ----------
local function gen(nfun)
  local b = {}
  for i = 1, nfun do
    b[#b + 1] = string.format([[
function f%d(a, b, c)
  local t = { x = a, y = b, z = c, name = "item%d", list = { 1, 2, 3, a + b, b * c } }
  if a > b and b ~= c then
    for i = 1, #t.list do t.list[i] = t.list[i] * 2 + i %% 7 end
  elseif a == %d then
    return t.name .. tostring(a), nil
  else
    while c > 0 do c = c - 1; t.x = t.x + c end
  end
  return t, function(k) return t[k] or %d.5 end
end
]], i, i, i, i)
  end
  b[#b + 1] = "return 1\n"
  return table.concat(b)
end
local src = gen(8000)
info("load source bytes", #src)
bench("load big chunk", function() local f = assert(load(src, "=gen")) end, 5)
local data = {}
for i = 1, (tonumber(arg and arg[2]) or 6000) do data[#data + 1] = string.format('  { id = %d, name = "name%d", score = %d.25, tags = { "a", "b", "c" }, active = true },\n', i, i, i) end
local dsrc = "return {\n" .. table.concat(data) .. "}\n"
info("load data bytes", #dsrc)
bench("load data table chunk (compile)", function() local f = assert(load(dsrc, "=data")) end, 5)
bench("load+run data table chunk", function() local f = assert(load(dsrc, "=data")); local t = f() end, 3)
bench("load tiny chunk x 20000", function() local l = load; for i = 1, 20000 do local f = l("return 1 + 2") end end)
bench("load small fn x 5000", function() local l = load; local s = "local a, b = ...; local t = {}; for i = 1, 10 do t[i] = a * i + b end; return t"; for i = 1, 5000 do local f = l(s) end end)
local comment = string.rep("-- this is a long comment line that the lexer must skip quickly ........\n", 40000) .. "return 1"
info("comment bytes", #comment)
bench("load comments", function() local f = assert(load(comment)) end, 5)
local strlit = "return {" .. string.rep('"a string literal with some length to it, forty-plus bytes long ok",\n', 30000) .. "}"
info("strlit bytes", #strlit)
bench("load string literals", function() local f = assert(load(strlit)) end, 5)

local rep = string.rep([[
do
  local function f(a, b, c)
    local t = { x = a, y = b, z = c, name = 'item', list = { 1, 2, 3, a + b, b * c } }
    if a > b and b ~= c then
      for i = 1, #t.list do t.list[i] = t.list[i] * 2 + i % 7 end
    elseif a == 10 then
      return t.name .. tostring(a), nil
    else
      while c > 0 do c = c - 1; t.x = t.x + c end
    end
    return t, function(k) return t[k] or 1.5 end
  end
end
]], 8000) .. 'return 1'
info('rep source bytes', #rep)
bench('load repeated fn chunk (no const growth)', function() local f = assert(load(rep, '=rep')) end, 5)
local exprs = 'local a, b, c, d = 1, 2, 3, 4\n' .. string.rep('a = (a + b * c - d / 2) % 7 + (a < b and c or d) .. "" + #"xyz"\n', 40000) .. 'return a'
info('expr source bytes', #exprs)
bench('load expression chunk', function() local f = assert(load(exprs, '=exprs')) end, 5)
