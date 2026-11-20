-- pcall-error: success-path pcall cost, error thrown and caught (string, table, error with level), nested pcall, xpcall with handler.
local lap_clock, lap_t = os.clock, os.clock() -- per-section time (ms) goes to stderr; stdout stays the checksum
local function lap(name) local t = lap_clock(); io.stderr:write(string.format("  %-34s %8.1f ms\n", name, (t - lap_t) * 1000)); lap_t = lap_clock() end
local N = tonumber(arg and arg[1]) or 1000000

-- 1. success path
local function ok(a, b) return a + b end
local s1 = 0
for i = 1, N do
  local _, r = pcall(ok, i, 1)
  s1 = s1 + r
end

lap("1. success path")
-- 2. error with a string message (position info is added by error())
local function fail_str(i) error("boom " .. (i & 7)) end
local s2 = 0
for i = 1, N // 10 do
  local _, m = pcall(fail_str, i)
  s2 = s2 + #m
end

lap("2. error with a string message")
-- 3. error with a table value (no message formatting)
local E = { code = 7 }
local function fail_tab() error(E) end
local s3 = 0
for _ = 1, N // 5 do
  local _, e = pcall(fail_tab)
  s3 = s3 + e.code
end

lap("3. error with a table value")
-- 4. runtime error raised by the VM (index a nil value)
local function fail_vm(t) return t.x.y end
local s4 = 0
for _ = 1, N // 10 do
  local ok2, m = pcall(fail_vm, {})
  if not ok2 then s4 = s4 + 1 end
end

lap("4. runtime error raised by the VM")
-- 5. nested pcall: error crosses several protected frames, caught at the second one
local function inner() error(E) end
local function mid() local ok3 = pcall(inner); if not ok3 then error(E) end end
local s5 = 0
for _ = 1, N // 10 do
  local _, e = pcall(mid)
  s5 = s5 + e.code
end

lap("5. nested pcall")
-- 6. deep unwinding: error thrown at recursion depth 30
local function deep(n) if n == 0 then error(E) end return deep(n - 1) + 1 end
local s6 = 0
for _ = 1, N // 20 do
  local _, e = pcall(deep, 30)
  s6 = s6 + e.code
end

lap("6. deep unwinding")
-- 7. xpcall with a message handler
local function handler(m) return "h:" .. tostring(m) end
local s7 = 0
for i = 1, N // 10 do
  local _, m = xpcall(fail_str, handler, i)
  s7 = s7 + #m
end

lap("7. xpcall with a message handler")
print(s1, s2, s3, s4, s5, s6, s7)
