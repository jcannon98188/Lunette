-- Per-construct cost probes for the perf effort (bench/PROFILE.md). Each probe runs a statement N times inside a
-- numeric for loop; the report is nanoseconds per iteration net of the empty loop. Run the same file under Lunette
-- and under PUC and compare the columns:
--   dotnet src/Lunette.Cli/bin/Release/net11.0/Lunette.Cli.dll bench/profile-probes.lua
--   .reference/lua bench/profile-probes.lua
-- Constructs are written as source text and compiled with load() so each probe is a straight loop body with locals.
local N = tonumber(arg and arg[1]) or 5000000

local probes = {
  { "empty loop", "" },
  { "local = local (Move)", "x = a" },
  { "x = 1 (LoadK)", "x = 1" },
  { "int add locals", "x = a + b" },
  { "int add const", "x = a + 1" },
  { "float mul locals", "x = f * g" },
  { "float mul const", "x = f * 2.5" },
  { "float div", "x = f / g" },
  { "int mod const", "x = a % 7" },
  { "compare+branch (a < b)", "if a < b then x = 1 end" },
  { "compare const branch (a == 3)", "if a == 3 then x = 1 end" },
  { "not/and/or", "x = a and b or 0" },
  { "upvalue get", "x = up" },
  { "upvalue set", "up = a" },
  { "global get (math)", "x = math" },
  { "global set", "G = a" },
  { "table get int (t[3])", "x = t[3]" },
  { "table get field (t.k)", "x = t.k" },
  { "table set int (t[3]=a)", "t[3] = a" },
  { "table set field (t.k=a)", "t.k = a" },
  { "table array append t[#t+1]", "t2[#t2 + 1] = a" },
  { "table constructor {}", "x = {}" },
  { "table constructor {1,2,3}", "x = {1, 2, 3}" },
  { "table constructor {x=1,y=2}", "x = {x = 1, y = 2}" },
  { "call empty Lua fn", "fn0()" },
  { "call Lua fn 1 arg 1 ret", "x = fn1(a)" },
  { "call Lua fn 3 args 1 ret", "x = fn3(a, b, a)" },
  { "call builtin (math.abs)", "x = abs(a)" },
  { "call builtin (select '#')", "x = select('#', a, b)" },
  { "closure creation", "x = function() return a end" },
  { "method call obj:m()", "x = obj:m()" },
  { "string concat 2", "x = s .. s" },
  { "string len #s", "x = #s" },
  { "string.sub", "x = ssub(s, 2, 4)" },
  { "string.byte", "x = sbyte(s, 2)" },
  { "string.format('%d')", "x = sformat('%d', a)" },
  { "tostring(int)", "x = tostring(a)" },
  { "pairs 8-element table", "for k, v in pairs(t8) do end" },
  { "ipairs 8-element table", "for k, v in ipairs(t8) do end" },
  { "numeric for 8 iterations", "for j = 1, 8 do end" },
}

local env = _G -- the probe chunks run with the real globals (no __index chain), like ordinary scripts
env.a, env.b, env.f, env.g, env.s = 5, 7, 1.5, 2.5, "hello world"
env.t = { 1, 2, 3, 4, 5, k = 9 }
env.t2 = {}
env.t8 = { 1, 2, 3, 4, 5, 6, 7, 8 }
env.up = 1
env.fn0 = function() end
env.fn1 = function(x) return x end
env.fn3 = function(x, y, z) return x end
env.abs = math.abs
env.ssub, env.sbyte, env.sformat = string.sub, string.byte, string.format
env.obj = { m = function(self) return 1 end }
env.x = 0

local function measure(src)
  -- locals for the probe variables so reads and writes are register operations like in typical hot code
  local prelude = "local a, b, f, g, s, t, t2, t8, up, fn0, fn1, fn3, abs, ssub, sbyte, sformat, obj = a, b, f, g, s, t, t2, t8, up, fn0, fn1, fn3, abs, ssub, sbyte, sformat, obj\nlocal x = 0\n"
  local chunk = assert(load(prelude .. "local N = ...\nlocal clock = os.clock\nlocal c0 = clock()\nfor i = 1, N do " .. src .. " end\nreturn clock() - c0", "=probe", "t", env))
  chunk(1000) -- warm up (JIT tiers) before the timed run
  chunk(N // 4)
  local best = math.huge
  for _ = 1, 3 do
    local dt = chunk(N)
    if dt < best then best = dt end
  end
  return best / N * 1e9
end

local base
io.write(string.format("%-34s %10s %10s\n", "probe", "ns/iter", "net ns"))
for _, p in ipairs(probes) do
  local ns = measure(p[2])
  if not base then base = ns end
  io.write(string.format("%-34s %10.1f %10.1f\n", p[1], ns, ns - base))
end
