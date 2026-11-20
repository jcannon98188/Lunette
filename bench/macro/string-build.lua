-- string-build: `..` in loops (short and growing), table.concat, string.format, tostring and tonumber measured separately.
local lap_clock, lap_t = os.clock, os.clock() -- per-section time (ms) goes to stderr; stdout stays the checksum
local function lap(name) local t = lap_clock(); io.stderr:write(string.format("  %-34s %8.1f ms\n", name, (t - lap_t) * 1000)); lap_t = lap_clock() end
local N = tonumber(arg and arg[1]) or 300000

-- 1. `..` of short pieces (fresh string each time)
local c1 = 0
for i = 1, N do
  local s = "k" .. i .. ":" .. (i & 255)
  c1 = c1 + #s
end

lap("1. `..` of short pieces")
-- 2. accumulating a growing string with `..` (quadratic copy; kept small)
local acc = ""
for i = 1, 6000 do acc = acc .. "line " .. i .. "\n" end
local c2 = #acc

lap("2. accumulating a growing string w")
-- 3. table.concat of many pieces, with and without separator
local parts = {}
for i = 1, N do parts[i] = i end
local j1 = table.concat(parts, ",")
local j2 = table.concat(parts)
local c3 = #j1 + #j2

lap("3. table.concat of many pieces")
-- 4. string.format with the common specifiers
local c4 = 0
for i = 1, N // 2 do
  c4 = c4 + #string.format("%d %s %5.2f %x", i, "ab", i / 7, i)
end

lap("4. string.format with the common s")
-- 5. tostring (integers, floats) separately
local c5 = 0
for i = 1, N do c5 = c5 + #tostring(i) end
for i = 1, N // 2 do c5 = c5 + #tostring(i + 0.5) end

lap("5. tostring")
-- 6. tonumber (integers, floats, hex) separately
local c6 = 0
for i = 1, N do c6 = c6 + tonumber("12345") + tonumber("0x1F") end
for i = 1, N // 2 do c6 = c6 + tonumber("3.25") + tonumber("1e3") end

lap("6. tonumber")
-- 7. number to string to number round trip
local c7 = 0
for i = 1, N // 2 do c7 = c7 + tonumber(tostring(i)) end

lap("7. number to string to number roun")
-- 8. string.rep / sub / byte / char assembly
local c8 = 0
for i = 1, N // 4 do
  local s = string.rep("ab", 8) .. string.char(65 + i % 26)
  c8 = c8 + s:byte(-1) + #s:sub(2, 9)
end

lap("8. string.rep / sub / byte / char")
print(c1, c2, c3, c4, c5, c6, c7, c8)
