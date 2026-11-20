-- Reference: bad argument #1 to 'ipairs' (table expected, got no value) in 5.4.9 is luaL_checkany: '(value expected)'.
print(pcall(ipairs))
