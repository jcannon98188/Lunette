-- Reference writes Lua strings byte for byte; the engine CLI mangles bytes above 127 (UTF-8 console encoding).
print(string.format("%q", 'he said "hi"\n\0\1\200'), string.format("%q", 1/3), string.format("%q", math.mininteger))
print(utf8.char(72, 228, 8364, 128512), utf8.len("häl"), #utf8.char(8364), utf8.codepoint("€", 1))
