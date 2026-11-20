-- Reference: "attempt to add a 'string' with a 'number'" (string metamethods). Engine reports 'perform arithmetic on a string value'.
print(pcall(function() return "a" + 1 end))
