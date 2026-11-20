-- Reference: math.randomseed(1.5) raises 'number has no integer representation'; engine accepts it.
math.randomseed(1.5)
print(math.random(1, 6), math.random(1, 6), math.random(1, 6))
