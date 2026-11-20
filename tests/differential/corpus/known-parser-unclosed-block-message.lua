-- Reference: "'end' expected (to close 'function' at line N) near X" when a block is not closed on another line.
-- Engine reports "<eof> expected near X" for an unclosed function body followed by more statements.
print(select(2, load("local function g()\n  return 1\nprint(2)\n")))
print(select(2, load("do\n  local x = 1\n")))
print(select(2, load("if true then\n  local x = 1\nprint(2)\n")))
