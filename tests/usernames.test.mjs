import { test, beforeEach, afterEach } from 'node:test';
import assert from 'node:assert/strict';
import { generateKeyPairSync } from 'node:crypto';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createMessenger } from '../server/index.mjs';

const jwk = generateKeyPairSync('rsa', { modulusLength: 3072 }).publicKey.export({ format: 'jwk' });
const publicKey = `<RSAKeyValue><Modulus>${Buffer.from(jwk.n, 'base64url').toString('base64')}</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>`;
const password = 'username test password';
let app, base, dataDir;
beforeEach(async () => {
  dataDir = mkdtempSync(join(tmpdir(), 'illusion-username-test-'));
  app = createMessenger({ dataDir });
  await new Promise(r => app.server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${app.server.address().port}`;
});
afterEach(async () => {
  await app.close();
  if (!dataDir.startsWith(join(tmpdir(), 'illusion-username-test-'))) throw new Error('Unexpected test directory');
  rmSync(dataDir, { recursive: true, force: true });
});
async function post(path, username) {
  const response = await fetch(base + path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username, password, displayName: 'Username test', ageBand: '18+', acceptTerms: true, acknowledgePrivacy: true, policyVersion: '2026-10-19.1', publicKey }) });
  return { status: response.status, body: await response.json() };
}
async function lookup(username, token) {
  const response = await fetch(base + '/v1/users?username=' + encodeURIComponent(username), { headers: { Authorization: 'Bearer ' + token } });
  return { status: response.status, body: await response.json() };
}
test('2–24 ASCII usernames support Latin letters, digits, underscores and hyphens through registration, login and lookup', async () => {
  for (const username of ['ab', 'A-', '7_', '-_', 'Latin_User-123', 'z'.repeat(24)]) {
    const registered = await post('/v1/register', username);
    assert.equal(registered.status, 200, JSON.stringify({ username, ...registered }));
    assert.equal(registered.body.user.username, username.toLowerCase());
    const login = await post('/v1/login', username.toUpperCase());
    assert.equal(login.status, 200, username);
    const found = await lookup(username.toUpperCase(), registered.body.token);
    assert.equal(found.status, 200);
    assert.equal(found.body.user.id, registered.body.user.id);
  }
  assert.equal((await post('/v1/register', 'LATIN_user-123')).status, 409, 'Case-insensitive uniqueness');
});
test('usernames reject short or long names, spaces, other punctuation and non-Latin characters', async () => {
  for (const username of ['a', 'a'.repeat(25), 'a b', 'a.b', 'a@b', 'a/b', 'аб', 'éa', '你好', '😀a', 'Ka', 'a\nb', null, 12]) {
    const result = await post('/v1/register', username);
    assert.equal(result.status, 400, JSON.stringify({ username, ...result }));
    assert.match(result.body.error, /2–24.*Latin letters/);
  }
  assert.equal(app.db.prepare('SELECT count(*) AS count FROM users').get().count, 0);
});
test('non-Latin characters cannot alias Latin usernames during login or lookup', async () => {
  const registered = await post('/v1/register', 'ka');
  assert.equal(registered.status, 200);
  assert.equal((await post('/v1/login', 'Ka')).status, 400);
  assert.equal((await lookup('Ka', registered.body.token)).status, 400);
  assert.equal((await lookup('KA', registered.body.token)).body.user.id, registered.body.user.id);
});
