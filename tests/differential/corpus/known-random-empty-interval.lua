-- Reference reports the empty interval as bad argument #1; the engine says #2.
print(pcall(function() local r = math.random(2, 1) return r end))
