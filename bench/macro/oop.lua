-- oop: metatable-based classes: multi-level __index inheritance with method calls, __newindex, __call, vector class with __add/__mul/__eq.
local lap_clock, lap_t = os.clock, os.clock() -- per-section time (ms) goes to stderr; stdout stays the checksum
local function lap(name) local t = lap_clock(); io.stderr:write(string.format("  %-34s %8.1f ms\n", name, (t - lap_t) * 1000)); lap_t = lap_clock() end
local N = tonumber(arg and arg[1]) or 800000

local function class(parent)
  local c = {}
  c.__index = c
  if parent then setmetatable(c, { __index = parent, __call = function(cls, ...) local o = setmetatable({}, cls); o:init(...); return o end })
  else setmetatable(c, { __call = function(cls, ...) local o = setmetatable({}, cls); o:init(...); return o end }) end
  return c
end

-- 1. four-level inheritance; the method is defined at the root, so every call walks the chain
local A = class(); function A:init(x) self.x = x end; function A:get() return self.x end; function A:bump() self.x = self.x + 1 end
local B = class(A); function B:extra() return 1 end
local C = class(B); function C:more() return 2 end
local D = class(C); function D:last() return 3 end
local d = D(1)
local s1 = 0
for _ = 1, N do d:bump(); s1 = s1 + d:get() + d:extra() end

lap("1. four-level inheritance; the met")
-- 2. override and call into the parent
local E = class(D); function E:get() return D.get(self) + 1 end
local e = E(5)
local s2 = 0
for _ = 1, N do s2 = s2 + e:get() end

lap("2. override and call into the pare")
-- 3. __newindex proxy: validated field writes forwarded to a backing table
local store = {}
local proxy = setmetatable({}, { __newindex = function(_, k, v) store[k] = v end, __index = store })
for i = 1, N do proxy.a = i; proxy.b = proxy.a + 1 end
local s3 = proxy.a + proxy.b

lap("3. __newindex proxy")
-- 4. __call: callable objects
local adder = setmetatable({ n = 3 }, { __call = function(self, x) return x + self.n end })
local s4 = 0
for i = 1, N do s4 = s4 + adder(i) end

lap("4. __call")
-- 5. vector class: __add, __mul, __eq, __unm, __tostring-free
local V = {}
V.__index = V
local function vec(x, y) return setmetatable({ x = x, y = y }, V) end
V.__add = function(a, b) return vec(a.x + b.x, a.y + b.y) end
V.__mul = function(a, k) return vec(a.x * k, a.y * k) end
V.__unm = function(a) return vec(-a.x, -a.y) end
V.__eq = function(a, b) return a.x == b.x and a.y == b.y end
function V:dot(o) return self.x * o.x + self.y * o.y end
local p, q = vec(1, 2), vec(3, 4)
local acc = vec(0, 0)
local eqs = 0
for i = 1, N // 2 do
  acc = acc + p * 2
  acc = acc + (-q)
  if acc == q then eqs = eqs + 1 end
end
local s5 = acc.x + acc.y + acc:dot(p) + eqs

lap("5. vector class")
print(s1, s2, s3, s4, s5)
