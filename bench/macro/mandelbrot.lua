-- mandelbrot: float loops and bit operations; prints an escape-count checksum rather than a bitmap.
local N = tonumber(arg and arg[1]) or 400
local checksum = 0
for y = 0, N - 1 do
  local ci = 2.0 * y / N - 1.0
  local bits, nbits = 0, 0
  for x = 0, N - 1 do
    local cr = 2.0 * x / N - 1.5
    local zr, zi, zrq, ziq = 0.0, 0.0, 0.0, 0.0
    local inside = 1
    for _ = 1, 50 do
      zi = 2.0 * zr * zi + ci
      zr = zrq - ziq + cr
      zrq = zr * zr
      ziq = zi * zi
      if zrq + ziq > 4.0 then inside = 0; break end
    end
    bits = (bits << 1) | inside
    nbits = nbits + 1
    if nbits == 8 then
      checksum = (checksum * 31 + bits) & 0xFFFFFFFF
      bits, nbits = 0, 0
    end
  end
  if nbits > 0 then checksum = (checksum * 31 + (bits << (8 - nbits))) & 0xFFFFFFFF end
end
print(checksum)
