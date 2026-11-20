#!/usr/bin/env node
// Suppression ratchet (ticket lua-engine-spec/17, decision 6).
//
// Counts [SuppressMessage] attributes per rule in the shipping sources and compares them with
// suppressions-baseline.json. The count may never exceed the baseline, every suppression needs a
// Justification, and '#pragma warning disable' is not an allowed way to suppress at all.
// With --base-ref <git ref>, the committed baseline itself may not exceed the baseline on that ref,
// so a PR cannot raise the ceiling just by editing the file.
//
// Usage: node ci/check-suppressions.js [--root <dir>] [--baseline <file>] [--base-ref <ref>]

'use strict';

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const SKIP_DIRS = new Set(['bin', 'obj', 'node_modules', '.git']);

function* csFiles(dir) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (SKIP_DIRS.has(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) yield* csFiles(full);
    else if (entry.name.endsWith('.cs')) yield full;
  }
}

// Returns { counts: {rule: n}, problems: [string] } for all C# under <srcDir>.
function scan(srcDir) {
  const counts = {};
  const problems = [];
  const attr = /\[\s*(?:assembly:\s*|module:\s*)?(?:System\.Diagnostics\.CodeAnalysis\.)?SuppressMessage(?:Attribute)?\s*\(([^\]]*)\)\s*\]/g;
  const pragma = /^\s*#pragma\s+warning\s+disable\b.*$/gm;
  for (const file of csFiles(srcDir)) {
    const text = fs.readFileSync(file, 'utf8');
    const rel = path.relative(srcDir, file).split(path.sep).join('/');
    for (const m of text.matchAll(attr)) {
      const args = m[1];
      const strings = [...args.matchAll(/"((?:[^"\\]|\\.)*)"/g)].map((s) => s[1]);
      const rule = (strings[1] || '').split(':')[0].trim() || '<unknown>';
      counts[rule] = (counts[rule] || 0) + 1;
      const justification = /Justification\s*=\s*"((?:[^"\\]|\\.)*)"/.exec(args);
      if (!justification || justification[1].trim() === '') {
        problems.push(`${rel}: SuppressMessage for ${rule} has no Justification`);
      }
    }
    for (const m of text.matchAll(pragma)) {
      problems.push(`${rel}: '${m[0].trim()}' is not allowed; use [SuppressMessage] with a Justification`);
    }
  }
  return { counts, problems };
}

function readBaseline(text, label) {
  const parsed = JSON.parse(text);
  if (!parsed || typeof parsed.rules !== 'object') throw new Error(`${label}: expected { "rules": { ... } }`);
  return parsed.rules;
}

// Returns the list of ratchet violations of `counts` against `ceiling`.
function exceeding(counts, ceiling, what) {
  const out = [];
  for (const [rule, n] of Object.entries(counts)) {
    const allowed = ceiling[rule] || 0;
    if (n > allowed) out.push(`${rule}: ${n} ${what}, ceiling is ${allowed}`);
  }
  return out;
}

function check({ root, baselineFile, baseRef }) {
  const failures = [];
  const { counts, problems } = scan(path.join(root, 'src'));
  failures.push(...problems);

  const baselinePath = path.join(root, baselineFile);
  const baseline = readBaseline(fs.readFileSync(baselinePath, 'utf8'), baselineFile);
  failures.push(...exceeding(counts, baseline, 'suppressions in source').map((s) => `${s} (edit ${baselineFile} in the same PR to raise it)`));

  if (baseRef) {
    let baseText = null;
    try {
      baseText = execFileSync('git', ['show', `${baseRef}:${baselineFile}`], { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
    } catch {
      // The baseline did not exist on the base ref yet; nothing to ratchet against.
    }
    if (baseText !== null) {
      failures.push(...exceeding(baseline, readBaseline(baseText, `${baseRef}:${baselineFile}`), 'in baseline').map((s) => `${s} (the suppression baseline may only decrease)`));
    }
  }
  return { counts, failures };
}

function main(argv) {
  const opts = { root: process.cwd(), baselineFile: 'suppressions-baseline.json', baseRef: null };
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--root') opts.root = path.resolve(argv[++i]);
    else if (argv[i] === '--baseline') opts.baselineFile = argv[++i];
    else if (argv[i] === '--base-ref') opts.baseRef = argv[++i];
    else throw new Error(`unknown argument ${argv[i]}`);
  }
  const { counts, failures } = check(opts);
  const total = Object.values(counts).reduce((a, b) => a + b, 0);
  if (failures.length > 0) {
    console.error('Suppression ratchet failed:');
    for (const f of failures) console.error(`  ${f}`);
    return 1;
  }
  console.log(`Suppression ratchet ok (${total} suppressions).`);
  return 0;
}

module.exports = { scan, check };

if (require.main === module) process.exit(main(process.argv.slice(2)));
