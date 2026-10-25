import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';

const tracked = execFileSync('git', ['ls-files'], { encoding: 'utf8' }).trim().split(/\r?\n/).filter(Boolean);
const allowed = new Set(['.env.example', 'tests/fixtures/legacy-key.json']);
const forbidden = [
  /^\.env(\.|$)/,
  /\.illusionkey$/i,
  /(^|\/)data\//,
  /\.sqlite(-|$)/i,
  /\.db(-|$)/i,
  /(^|\/)(cloud-state|\.bot-state)\.json$/i,
  /\.pem$/i,
  /\.p12$/i,
  /\.pfx$/i,
  /(^|\/)keys?\//i,
  /(^|\/)pins?\//i
];

test('the public repository tracks no private files', () => {
  for (const file of tracked) {
    if (allowed.has(file)) continue;
    for (const rule of forbidden) assert.ok(!rule.test(file), 'private file tracked: ' + file);
  }
});

test('.gitignore keeps credentials and local state out of git', () => {
  const ignore = readFileSync('.gitignore', 'utf8');
  for (const pattern of ['.env', '*.illusionkey', 'data/', '*.sqlite', '.bot-state.json']) {
    assert.ok(ignore.includes(pattern), '.gitignore missing ' + pattern);
  }
});

test('tracked files contain no credential-shaped values', () => {
  const patterns = [
    /[0-9]{8,10}:[A-Za-z0-9_-]{30,}/,
    /-----BEGIN [A-Z ]*PRIVATE KEY-----/,
    /(?:ADMIN_TOKEN|TELEGRAM_BOT_TOKEN|GMAIL_REFRESH_TOKEN|EMAIL_CODE_KEY)=[A-Za-z0-9_-]{24,}/
  ];
  for (const file of tracked) {
    if (allowed.has(file)) continue;
    let text;
    try { text = readFileSync(file, 'utf8'); } catch { continue; }
    for (const pattern of patterns) assert.ok(!pattern.test(text), 'possible credential in ' + file);
  }
});
