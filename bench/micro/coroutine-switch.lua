-- coroutines: resume/yield round trip
local co = coroutine.wrap(function() local i = 0; while true do i = i + 1; coroutine.yield(i) end end)
local s = 0
for _ = 1, 100000 do s = s + co() end
return s
