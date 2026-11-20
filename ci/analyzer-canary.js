#!/usr/bin/env node
// Proves the analyzers really fail the build: drops a file that violates every rule into src/Lunette,
// builds, and requires each rule id in the errors. The canary is always removed afterwards.
//
// Usage: node ci/analyzer-canary.js

'use strict';

const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const root = path.resolve(__dirname, '..');
const canary = path.join(root, 'src', 'Lunette', 'ZzAnalyzerCanary.cs');

const source = `
namespace Lunette;

// Matched by simple name (LD003); nested so it does not clash with the real Lunette.LuaString.
internal static class Canary
{
    internal sealed class LuaString
    {
        internal byte[] Bytes = new byte[4];
    }
}

internal sealed class Box { }

[LuaHotPath]
internal static class HotCanary
{
    public static object Alloc() => new Box();                                  // LD001
    public static void Throws() { throw new InvalidOperationException(); }      // LD002
    public static byte Index(Canary.LuaString s) => s.Bytes[0];                        // LD003
}

internal static class LoopCanary
{
    [LuaInterpreterLoop]
    public static void Loop() { try { } finally { } }                           // LD006
}

internal static class StringCanary
{
    [LuaStringPath]
    public static void Text() { _ = System.Text.Encoding.UTF8; }                // LD004
}

internal static class UnsafeCanary
{
    public static int Cast(ref uint u) => System.Runtime.CompilerServices.Unsafe.As<uint, int>(ref u); // LD005
}

internal static class BannedCanary
{
    public static int Count(int[] a) => System.Linq.Enumerable.Count(a);        // RS0030 (banned API)
    public static object Make() => Activator.CreateInstance(typeof(Box))!;      // RS0030 (banned API)
}
`;

const expected = ['LD001', 'LD002', 'LD003', 'LD004', 'LD005', 'LD006', 'RS0030'];

let exitCode = 0;
try {
  fs.writeFileSync(canary, source);
  const build = spawnSync('dotnet', ['build', path.join('src', 'Lunette', 'Lunette.csproj'), '--no-incremental', '-nologo'], { cwd: root, encoding: 'utf8' });
  const output = `${build.stdout}\n${build.stderr}`;
  if (build.status === 0) {
    console.error('canary build unexpectedly succeeded: violations do not fail the build');
    exitCode = 1;
  } else {
    const missing = expected.filter((id) => !new RegExp(`error ${id}\\b`).test(output));
    if (missing.length > 0) {
      console.error(`canary build failed, but these rules did not report an error: ${missing.join(', ')}`);
      console.error(output);
      exitCode = 1;
    } else {
      console.log(`analyzer canary ok: ${expected.join(', ')} all fail the build`);
    }
  }
} finally {
  fs.rmSync(canary, { force: true });
}
process.exit(exitCode);
