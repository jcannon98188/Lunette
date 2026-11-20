-- Reference: "invalid value (table) at index 2 in table for 'concat'"; engine omits the type.
print(pcall(table.concat, {1, {}, 3}))
