import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { connectionProfile, loadConnection } from '../scripts/connection-profile.mjs';

test('connection profiles normalize HTTPS origins and contain no host secrets', () => {
  const profile = connectionProfile('https://relay.example.test:443/', 'Illusion');
  assert.deepEqual(profile, { format: 'illusion.connection', version: 1, name: 'Illusion', relay: 'https://relay.example.test' });
  assert.equal(connectionProfile().relay, '');
  assert.equal(connectionProfile('http://127.0.0.1:3789').relay, 'http://127.0.0.1:3789');
});
test('connection profiles reject insecure internet endpoints and embedded credentials', () => {
  for (const relay of ['http://relay.example.test', 'ftp://relay.example.test', 'https://user:password@relay.example.test', 'https://relay.example.test/api', 'https://relay.example.test/?token=secret', 'https://relay.example.test/#fragment']) assert.throws(() => connectionProfile(relay));
  assert.throws(() => connectionProfile('https://relay.example.test', 'a\nnetwork'));
});
test('connection file import rejects unknown fields rather than accepting a secret-bearing config', () => {
  const dir = mkdtempSync(join(tmpdir(), 'illusion-connection-test-'));
  try {
    const path = join(dir, 'connection.illusion'), profile = connectionProfile('https://relay.example.test');
    writeFileSync(path, JSON.stringify(profile));assert.deepEqual(loadConnection(path), profile);
    writeFileSync(path, JSON.stringify({ ...profile, TELEGRAM_BOT_TOKEN: 'test-fixture' }));assert.throws(() => loadConnection(path), /Invalid/);
  } finally {if (!dir.startsWith(join(tmpdir(), 'illusion-connection-test-'))) throw new Error('Unexpected path');rmSync(dir, { recursive: true, force: true });}
});
