-- nsieve: sieve of Eratosthenes, boolean array writes.
local function nsieve(m)
  local isPrime = {}
  for i = 2, m do isPrime[i] = true end
  local count = 0
  for i = 2, m do
    if isPrime[i] then
      for k = i + i, m, i do isPrime[k] = false end
      count = count + 1
    end
  end
  return count
end

local n = tonumber(arg and arg[1]) or 9
for i = 0, 2 do
  local m = (1 << (n - i)) * 10000
  print(string.format("Primes up to %8d %8d", m, nsieve(m)))
end
