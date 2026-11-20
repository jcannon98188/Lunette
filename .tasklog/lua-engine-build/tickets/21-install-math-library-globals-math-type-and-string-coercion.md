---
kind: ticket
title: Install math library globals, math.type and string coercion
status: resolved
blocked_by: [4, 6]
tags: [ready-for-agent, enhancement]
created: 2026-11-20
updated: 2026-11-20T10:53:01Z
---

## Question

Gap from ticket 05: MathLib exists only as a name-to-value map called by tests. Install it as the `math` global, add `math.type`, add string-to-number coercion of arguments (and arithmetic/bitwise string coercion per manual 3.4.3), and verify math.random against the reference 5.4.9 binary output for fixed seeds. Make `math.lua` and `bitwise.lua` suite files pass (or waive with reason codes) and add them to required-files.txt.

Acceptance criteria:
- [ ] `math` global installed with all functions including math.type
- [ ] String coercion in arithmetic, bitwise ops and math args
- [ ] math.lua and bitwise.lua pass the suite and are in required-files.txt

## Answer

Math library globals installed, math.type and string-to-number arithmetic coercion implemented.
