-- Reference: "invalid conversion specification: '%100d'"; engine: "invalid conversion '%100d' to 'format'".
print(#string.format("%099d", 1), pcall(string.format, "%100d", 1))
