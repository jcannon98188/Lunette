-- Reference (x86 Linux printf) prints this NaN as -nan; the engine prints nan.
print(math.fmod(math.huge, 1))
