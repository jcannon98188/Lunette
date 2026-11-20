-- closures: upvalue reads and writes through a call
local function counter() local n = 0; return function() n = n + 1; return n end end
local c = counter()
local s = 0
for _ = 1, 300000 do s = s + c() end
return s
