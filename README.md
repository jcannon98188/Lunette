<img src="assets/icon.svg" width="64" height="64" alt="">

# Lunette

Created in [T3 Code](https://t3.codes).

## Host features not tied to a ticket

- **Interrupt / Ctrl-C.** `LuaState.RequestInterrupt` (sticky until `ClearInterrupt`) and `RequestInterruptOnce` (one "interrupted" Lua error, catchable by `pcall`) stop a running script at its next poll point from any thread. The standalone CLI uses the one-shot form for Ctrl-C, as lua.c does. Intentional, though no ticket asked for it; unlike a budget or memory abort it is an ordinary Lua error and leaves the state usable.
