'use strict';

const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync } = require('child_process');
const { check } = require('./check-suppressions');

function fixture(source, baseline) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'supp-'));
  fs.mkdirSync(path.join(root, 'src'));
  fs.writeFileSync(path.join(root, 'src', 'A.cs'), source);
  fs.writeFileSync(path.join(root, 'suppressions-baseline.json'), JSON.stringify({ rules: baseline }));
  return root;
}

const run = (root, extra = {}) => check({ root, baselineFile: 'suppressions-baseline.json', baseRef: null, ...extra });
const ok = '[SuppressMessage("Lunette", "LD001:x", Justification = "measured: cold path")] class A {}';

test('no suppressions passes against an empty baseline', () => {
  assert.deepStrictEqual(run(fixture('class A {}', {})).failures, []);
});

test('a suppression within the baseline passes', () => {
  assert.deepStrictEqual(run(fixture(ok, { LD001: 1 })).failures, []);
});

test('a suppression above the baseline fails', () => {
  const { failures } = run(fixture(ok, {}));
  assert.strictEqual(failures.length, 1);
  assert.match(failures[0], /LD001: 1 suppressions in source, ceiling is 0/);
});

test('a suppression without a Justification fails', () => {
  const { failures } = run(fixture('[SuppressMessage("Lunette", "LD002")] class A {}', { LD002: 1 }));
  assert.match(failures.join('\n'), /no Justification/);
});

test('a pragma disable fails', () => {
  const { failures } = run(fixture('#pragma warning disable LD001\nclass A {}', {}));
  assert.match(failures.join('\n'), /pragma/);
});

test('files under bin and obj are ignored', () => {
  const root = fixture('class A {}', {});
  fs.mkdirSync(path.join(root, 'src', 'obj'));
  fs.writeFileSync(path.join(root, 'src', 'obj', 'G.cs'), ok);
  assert.deepStrictEqual(run(root).failures, []);
});

test('the baseline may not rise above the base ref', () => {
  const root = fixture('class A {}', { LD001: 0 });
  const git = (...args) => execFileSync('git', ['-c', 'user.name=t', '-c', 'user.email=t@t', ...args], { cwd: root, stdio: 'ignore' });
  git('init', '-q');
  git('add', '.');
  git('commit', '-q', '-m', 'base');
  fs.writeFileSync(path.join(root, 'src', 'A.cs'), ok);
  fs.writeFileSync(path.join(root, 'suppressions-baseline.json'), JSON.stringify({ rules: { LD001: 1 } }));
  const { failures } = run(root, { baseRef: 'HEAD' });
  assert.strictEqual(failures.length, 1);
  assert.match(failures[0], /may only decrease/);
});
