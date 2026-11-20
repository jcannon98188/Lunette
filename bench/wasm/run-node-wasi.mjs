// Runs the WASI build of PUC Lua under Node (V8: Liftoff then TurboFan) via node:wasi.
// usage: node run-node-wasi.mjs <lua.wasm> <script.lua> [args...]
process.removeAllListeners('warning'); process.emitWarning = () => {};
import { readFileSync } from 'node:fs';
import { WASI } from 'node:wasi';
import { dirname, basename, resolve } from 'node:path';

const [wasmPath, scriptPath, ...rest] = process.argv.slice(2);
if (!wasmPath || !scriptPath) { console.error('usage: run-node-wasi.mjs <lua.wasm> <script.lua> [args...]'); process.exit(2); }
const script = resolve(scriptPath);
const wasi = new WASI({
  version: 'preview1',
  args: ['lua', '/' + basename(script), ...rest],
  preopens: { '/': dirname(script) },
  returnOnExit: true,
});
const module = await WebAssembly.compile(readFileSync(wasmPath));
const instance = await WebAssembly.instantiate(module, wasi.getImportObject());
process.exitCode = wasi.start(instance);
