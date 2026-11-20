# Vendored Lua test suite

`tests/lua-suite/` is the unmodified contents of `lua-5.4.9-tests.tar.gz` from https://www.lua.org/tests/
(MIT licence, notice in `lua-suite/all.lua`). Never edit it in place: deviations are overlays in `tests/suite/waivers.toml`.

SHA-256 of the tarball:

```
7d971845f545ffc09fbb3128a86b2c6524161c70d0fdf0154a16e8c00c343fca  lua-5.4.9-tests.tar.gz
```

The Reference binary (PUC Lua 5.4.9) is built in CI by `ci/build-reference.sh` from the source tarball pinned there.
