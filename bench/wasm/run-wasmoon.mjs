// Runs a Lua script under wasmoon (Lua compiled to WASM with emscripten, run in Node).
// usage: node run-wasmoon.mjs <wasmoon-install-dir> <script.lua>    (install dir = the directory holding node_modules/wasmoon)
//        node run-wasmoon.mjs <wasmoon-install-dir> --version      prints the Lua version wasmoon embeds
import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';

const [dir, script] = process.argv.slice(2);
if (!dir || !script) { console.error('usage: run-wasmoon.mjs <wasmoon-install-dir> <script.lua|--version>'); process.exit(2); }
const require = createRequire(resolve(dir) + '/');
const { LuaFactory } = require('wasmoon');

const lua = await new LuaFactory().createEngine();
// print goes through process.stdout; collect and write once to keep the timing about the engine, not console churn.
let out = '';
lua.global.set('print', (...a) => { out += a.map((x) => String(x)).join('\t') + '\n'; });
try {
  if (script === '--version') { await lua.doString('print(_VERSION)'); }
  else {
    // The macro programs only call print and read the optional global `arg` (absent here, so defaults apply).
    await lua.doString(readFileSync(script, 'utf8'));
  }
} finally {
  process.stdout.write(out);
  lua.global.close();
}
