# Vendored Lua test suite

`tests/lua-suite/` is the unmodified contents of `lua-5.4.9-tests.tar.gz` from https://www.lua.org/tests/
(MIT licence, notice in `lua-suite/all.lua`). Never edit it in place: deviations are overlays in `tests/suite/waivers.toml`.

SHA-256 of the tarball:

```
7d971845f545ffc09fbb3128a86b2c6524161c70d0fdf0154a16e8c00c343fca  lua-5.4.9-tests.tar.gz
```

The Reference binary (PUC Lua 5.4.9) is built in CI by `ci/build-reference.sh` from the source tarball pinned there.

## Provenance of the source-tarball checksum

lua.org publishes no checksum for `lua-5.4.9.tar.gz` (the ftp index lists only name, date and size: 373429 bytes), so the
value pinned in `ci/build-reference.sh` rests on two independent observations:

- SHA-256 of `https://www.lua.org/ftp/lua-5.4.9.tar.gz` as downloaded and hashed locally.
- The nixpkgs pin for Lua 5.4.9 (`pkgs/development/interpreters/lua-5/default.nix`, `hash = "sha256-IzW2xYKlJlT5RhK/ENL0ZygF0FMpqmVosdjNnlxvuOY="`);
  that SRI base64 decodes to the same hex digest.

```
2335b6c582a52654f94612bf10d2f4672805d05329aa6568b1d8cd9e5c6fb8e6  lua-5.4.9.tar.gz
```

Verified 2026-11-20. The tests tarball hash above was likewise re-fetched from https://www.lua.org/tests/ on that date and matched.
It is a trust-on-first-use pin cross-checked against a third party, not a vendor-signed value.
