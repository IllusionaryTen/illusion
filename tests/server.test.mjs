import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { generateKeyPairSync, randomBytes, randomUUID, publicEncrypt, privateDecrypt, createCipheriv, createDecipheriv, createHmac, sign, verify, constants } from 'node:crypto';
import { createMessenger, signatureText } from '../server/index.mjs';

function identity() {
  const keys = generateKeyPairSync('rsa', { modulusLength: 3072 });
  const jwk = keys.publicKey.export({ format: 'jwk' });
  const xml = `<RSAKeyValue><Modulus>${Buffer.from(jwk.n, 'base64url').toString('base64')}</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>`;
  return { ...keys, xml };
}
const aliceKey = identity(), bobKey = identity(), eveKey = identity();
function encrypt(sender, recipient, text) {
  const clientId = randomUUID();
  const material = randomBytes(64), iv = randomBytes(16);
  const cipher = createCipheriv('aes-256-cbc', material.subarray(0, 32), iv);
  const ciphertext = Buffer.concat([cipher.update(text, 'utf8'), cipher.final()]);
  const keys = {};
  for (const user of [sender, recipient]) keys[user.user.id] = publicEncrypt({ key: user.key.publicKey, oaepHash: 'sha1', padding: constants.RSA_PKCS1_OAEP_PADDING }, material).toString('base64');
  const envelope = { v: 1, iv: iv.toString('base64'), ciphertext: ciphertext.toString('base64'), mac: createHmac('sha256', material.subarray(32)).update(iv).update(ciphertext).digest('base64'), keys };
  envelope.signature = sign('RSA-SHA256', Buffer.from(signatureText(sender.user.id, recipient.user.id, clientId, envelope)), sender.key.privateKey).toString('base64');
  return { recipientId: recipient.user.id, clientId, envelope };
}
function decrypt(message, user, sender) {
  const e = message.envelope;
  assert.ok(verify('RSA-SHA256', Buffer.from(signatureText(message.senderId, message.recipientId, message.clientId, e)), sender.key.publicKey, Buffer.from(e.signature, 'base64')));
  const material = privateDecrypt({ key: user.key.privateKey, oaepHash: 'sha1' }, Buffer.from(e.keys[user.user.id], 'base64'));
  const iv = Buffer.from(e.iv, 'base64'), ciphertext = Buffer.from(e.ciphertext, 'base64');
  assert.equal(createHmac('sha256', material.subarray(32)).update(iv).update(ciphertext).digest('base64'), e.mac);
  const cipher = createDecipheriv('aes-256-cbc', material.subarray(0, 32), iv);
  return Buffer.concat([cipher.update(ciphertext), cipher.final()]).toString('utf8');
}
const dataDir = mkdtempSync(join(tmpdir(), 'illusion-server-test-'));
let app, base, alice, bob, eve, first;
async function start() {
  app = createMessenger({ dataDir });
  await new Promise(r => app.server.listen(0, '127.0.0.1', r));
  base = `http://127.0.0.1:${app.server.address().port}`;
}
async function api(path, { method = 'GET', token, body } = {}) {
  const res = await fetch(base + path, { method, headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), 'Content-Type': 'application/json' }, body: body && JSON.stringify(body) });
  return { status: res.status, body: await res.json() };
}
async function register(username, key) {
  const r = await api('/v1/register', { method: 'POST', body: { username, displayName: username.toUpperCase(), password: 'correct horse battery staple', ageBand: '18+', acceptTerms: true, acknowledgePrivacy: true, policyVersion: '2026-10-19.1', publicKey: key.xml } });
  assert.equal(r.status, 200, JSON.stringify(r.body)); return { ...r.body, key };
}
before(async () => { await start(); alice = await register('alice', aliceKey); bob = await register('bob', bobKey); eve = await register('eve', eveKey); });
after(async () => { await app.close(); if (!dataDir.startsWith(join(tmpdir(), 'illusion-server-test-'))) throw new Error('Unexpected test directory'); rmSync(dataDir, { recursive: true, force: true }); });

test('unauthenticated users cannot list contacts or read messages', async () => {
  assert.equal((await api('/v1/chats')).status, 401);
  assert.equal((await api('/v1/messages?peer=' + bob.user.id)).status, 401);
});
test('registration validates passwords, keys, duplicate names and display names', async () => {
  const input = { username: 'alice', displayName: 'Alice', password: 'correct horse battery staple', ageBand: '18+', acceptTerms: true, acknowledgePrivacy: true, policyVersion: '2026-10-19.1', publicKey: aliceKey.xml };
  assert.equal((await api('/v1/register', { method: 'POST', body: input })).status, 409);
  for (const change of [{ password: 'short' }, { publicKey: 'invalid' }, { displayName: '' }, { username: '../evil' }]) {
    assert.equal((await api('/v1/register', { method: 'POST', body: { ...input, ...change } })).status, 400);
  }
});
test('login checks password and exact username lookup returns public keys only', async () => {
  assert.equal((await api('/v1/login', { method: 'POST', body: { username: 'alice', password: 'totally wrong password' } })).status, 401);
  const login = await api('/v1/login', { method: 'POST', body: { username: 'alice', password: 'correct horse battery staple' } });
  assert.equal(login.status, 200);
  const lookup = await api('/v1/users?username=BOB', { token: alice.token });
  assert.equal(lookup.body.user.id, bob.user.id);
  assert.equal(lookup.body.user.publicKey, bobKey.xml);
  assert.equal(lookup.body.user.passwordHash, undefined);
  assert.equal((await api('/v1/users?username=bo', { token: alice.token })).status, 404);
});
test('two accounts exchange encrypted Unicode messages readable by both participants', async () => {
  const payload = encrypt(alice, bob, 'Hello, dream state! Привет ♡');
  const sent = await api('/v1/messages', { method: 'POST', token: alice.token, body: payload });
  assert.equal(sent.status, 200, JSON.stringify(sent.body)); first = sent.body.message;
  assert.equal(decrypt(first, bob, alice), 'Hello, dream state! Привет ♡');
  assert.equal(decrypt(first, alice, alice), 'Hello, dream state! Привет ♡');
  const rows = await api('/v1/messages?peer=' + alice.user.id, { token: bob.token });
  assert.equal(rows.body.messages.length, 1);
  assert.equal(rows.body.messages[0].envelope.ciphertext, payload.envelope.ciphertext);
  assert.ok(!JSON.stringify(rows.body).includes('Hello, dream state'));
  assert.ok(!app.db.prepare('SELECT envelope FROM messages').get().envelope.includes('Hello, dream state'));
});
test('another account cannot read or discover the private conversation', async () => {
  const rows = await api('/v1/messages?peer=' + alice.user.id, { token: eve.token });
  assert.deepEqual(rows.body.messages, []);
  assert.deepEqual((await api('/v1/chats', { token: eve.token })).body.chats, []);
  assert.throws(() => decrypt(first, eve, alice));
});
test('invalid signatures, plaintext, altered recipients and oversized messages are rejected', async () => {
  const payload = encrypt(alice, bob, 'Authenticated message');
  const bad = structuredClone(payload); bad.envelope.ciphertext = Buffer.alloc(16).toString('base64');
  assert.equal((await api('/v1/messages', { method: 'POST', token: alice.token, body: bad })).status, 400);
  assert.equal((await api('/v1/messages', { method: 'POST', token: alice.token, body: { ...payload, recipientId: eve.user.id } })).status, 400);
  assert.equal((await api('/v1/messages', { method: 'POST', token: alice.token, body: { recipientId: bob.user.id, clientId: randomUUID(), text: 'plaintext' } })).status, 400);
  assert.equal((await api('/v1/messages', { method: 'POST', token: alice.token, body: { padding: 'x'.repeat(31000) } })).status, 413);
});
test('retrying one encrypted message is idempotent and conflicting reuse fails', async () => {
  const payload = encrypt(alice, bob, 'Only once');
  const a = await api('/v1/messages', { method: 'POST', token: alice.token, body: payload });
  const b = await api('/v1/messages', { method: 'POST', token: alice.token, body: payload });
  assert.equal(a.body.message.id, b.body.message.id);
  const changed = encrypt(alice, bob, 'Conflict'); changed.clientId = payload.clientId;
  changed.envelope.signature = sign('RSA-SHA256', Buffer.from(signatureText(alice.user.id, bob.user.id, changed.clientId, changed.envelope)), alice.key.privateKey).toString('base64');
  assert.equal((await api('/v1/messages', { method: 'POST', token: alice.token, body: changed })).status, 409);
});
test('unread markers belong to each user and can be cleared', async () => {
  const chats = await api('/v1/chats', { token: bob.token });
  assert.equal(chats.body.chats[0].unread, 2);
  await api('/v1/read', { method: 'POST', token: bob.token, body: { peer: alice.user.id, lastId: chats.body.chats[0].lastId } });
  assert.equal((await api('/v1/chats', { token: bob.token })).body.chats[0].unread, 0);
});
test('saved messages work and do not count as unread', async () => {
  const payload = encrypt(alice, alice, 'Note to myself');
  const r = await api('/v1/messages', { method: 'POST', token: alice.token, body: payload });
  assert.equal(r.status, 200);
  assert.equal(decrypt(r.body.message, alice, alice), 'Note to myself');
  const chats = await api('/v1/chats', { token: alice.token });
  assert.equal(chats.body.chats.find(c => c.id === alice.user.id).unread, 0);
});
test('history cursors are conversation-scoped and malformed cursors fail', async () => {
  const rows = await api('/v1/messages?peer=' + bob.user.id + '&after=' + first.id, { token: alice.token });
  assert.equal(rows.body.messages.length, 1);
  assert.ok(rows.body.messages.every(m => m.id > first.id));
  assert.equal((await api('/v1/messages?peer=' + bob.user.id + '&before=NaN', { token: alice.token })).status, 400);
});
test('live notifications wake only the participants, and read receipts identify the visible conversation', async () => {
  const aliceCursor = (await api('/v1/events', { token: alice.token })).body.cursor;
  const eveCursor = (await api('/v1/events', { token: eve.token })).body.cursor;
  let woke = false;
  const pending = api('/v1/events?cursor=' + encodeURIComponent(aliceCursor), { token: alice.token }).then(r => { woke = true;return r; });
  await new Promise(r => setTimeout(r, 80));assert.equal(woke, false, 'Unchanged subscription waits');
  const sent = await api('/v1/messages', { method: 'POST', token: bob.token, body: encrypt(bob, alice, 'Live delivery') });
  const event = await pending;
  assert.equal(event.status, 200);assert.equal(event.body.changed, true);assert.notEqual(event.body.cursor, aliceCursor);
  const controller = new AbortController();
  const stranger = fetch(base + '/v1/events?cursor=' + encodeURIComponent(eveCursor), { headers: { Authorization: 'Bearer ' + eve.token }, signal: controller.signal });
  setTimeout(() => controller.abort(), 120);
  await assert.rejects(stranger, { name: 'AbortError' });
  await api('/v1/read', { method: 'POST', token: alice.token, body: { peer: bob.user.id, lastId: sent.body.message.id } });
  const history = await api('/v1/messages?peer=' + alice.user.id, { token: bob.token });
  assert.equal(history.body.readThrough, sent.body.message.id);
  const chats = await api('/v1/chats', { token: alice.token });
  const latest = chats.body.chats.find(c => c.id === bob.user.id).lastMessage;
  assert.equal(decrypt(latest, alice, bob), 'Live delivery');
  assert.equal((await api('/v1/events')).status, 401);
});
test('SQLite persists encrypted history and sessions across restarts', async () => {
  await app.close(); await start();
  const r = await api('/v1/messages?peer=' + bob.user.id, { token: alice.token });
  assert.equal(r.status, 200);
  assert.equal(decrypt(r.body.messages[0], bob, alice), 'Hello, dream state! Привет ♡');
});
test('read receipt opt-out clears local unread counts without advancing the shared receipt', async () => {
  const before = (await api('/v1/messages?peer=' + alice.user.id, { token: bob.token })).body.readThrough;
  const sent = await api('/v1/messages', { method: 'POST', token: bob.token, body: encrypt(bob, alice, 'Read privately') });
  await api('/v1/read', { method: 'POST', token: alice.token, body: { peer: bob.user.id, lastId: sent.body.message.id, shareReceipts: false } });
  const history = await api('/v1/messages?peer=' + alice.user.id, { token: bob.token });
  assert.equal(history.body.readThrough, before);
  const chats = await api('/v1/chats', { token: alice.token });
  assert.equal(chats.body.chats.find(c => c.id === bob.user.id).unread, 0);
});
test('logout revokes the session token', async () => {
  assert.equal((await api('/v1/logout', { method: 'POST', token: eve.token, body: {} })).status, 200);
  assert.equal((await api('/v1/me', { token: eve.token })).status, 401);
});
test('registration can be restricted with an invitation code', async () => {
  const restrictedDir = mkdtempSync(join(tmpdir(), 'illusion-invite-test-'));
  const restricted = createMessenger({ dataDir: restrictedDir, registrationCode: 'invited-friends' });
  try {
    await new Promise(r => restricted.server.listen(0, '127.0.0.1', r));
    const url = `http://127.0.0.1:${restricted.server.address().port}/v1/register`;
    const body = { username: 'friend', displayName: 'Friend', password: 'long enough password', ageBand: '18+', acceptTerms: true, acknowledgePrivacy: true, policyVersion: '2026-10-19.1', publicKey: eveKey.xml };
    const denied = await fetch(url, { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify(body) });assert.equal(denied.status, 403);
    const allowed = await fetch(url, { method: 'POST', headers: {'Content-Type':'application/json'}, body: JSON.stringify({ ...body, registrationCode: 'invited-friends' }) });assert.equal(allowed.status, 200);
  } finally { await restricted.close(); if (!restrictedDir.startsWith(join(tmpdir(), 'illusion-invite-test-'))) throw new Error('Unexpected path');rmSync(restrictedDir, { recursive: true, force: true }); }
});
