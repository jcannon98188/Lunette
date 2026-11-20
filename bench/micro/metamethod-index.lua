-- metamethods: __index chain and method call
local Base = {}; Base.__index = Base
function Base:get() return self.v end
local Derived = setmetatable({}, Base); Derived.__index = Derived
local o = setmetatable({ v = 3 }, Derived)
local s = 0
for _ = 1, 200000 do s = s + o:get() end
return s
