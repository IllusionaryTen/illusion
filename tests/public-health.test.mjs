import { test } from 'node:test';
import assert from 'node:assert/strict';
import { probePublicRelay } from '../scripts/public-health.mjs';

const origin = 'https://test.trycloudflare.com';
const expected = { version: '0.5.0', instance: 'fresh-local-instance' };
const health = { app: 'Illusion', ...expected };
function reply(status, body) { return new Response(typeof body === 'string' ? body : JSON.stringify(body), { status }); }

test('public probe requires the current relay through HTTPS without credentials or redirects', async () => {
  const result = await probePublicRelay(origin, expected, async (url, options) => {
    assert.equal(url, origin + '/health');
    assert.equal(options.redirect, 'error');
    assert.equal(options.headers, undefined);
    assert.ok(options.signal instanceof AbortSignal);
    return reply(200, health);
  });
  assert.equal(result.ready, true);
});

test('public probe never accepts another instance, version, service or non-200 response', async () => {
  for (const [status, body] of [[200, { ...health, instance: 'stale' }], [200, { ...health, version: '0.4.0' }], [200, { ...health, app: 'Other' }], [500, health]]) {
    assert.equal((await probePublicRelay(origin, expected, async () => reply(status, body))).ready, false);
  }
  assert.equal((await probePublicRelay(origin, {}, async () => reply(200, health))).ready, false);
});

test('public probe distinguishes Cloudflare errors from a malformed health response', async () => {
  const cf = await probePublicRelay(origin, expected, async () => reply(530, '<span class="error-code">1033</span>'));
  assert.equal(cf.reason, 'HTTP 530 (Cloudflare 1033)');
  const html = await probePublicRelay(origin, expected, async () => reply(200, '<html>not a relay</html>'));
  assert.match(html.reason, /not relay health JSON/);
});

test('public probe exposes safe DNS and timeout codes without echoing request details', async () => {
  for (const code of ['ENOTFOUND', 'UND_ERR_CONNECT_TIMEOUT']) {
    const result = await probePublicRelay(origin, expected, async () => { throw Object.assign(new Error('private request details'), { cause: { code } }); });
    assert.equal(result.ready, false);
    assert.ok(result.reason.includes(code));
    assert.ok(!result.reason.includes('private request details'));
  }
});

test('a real AbortSignal timeout reports its name rather than legacy DOMException code 23', async () => {
  const result = await probePublicRelay(origin, expected, async () => { throw new DOMException('private details', 'TimeoutError'); });
  assert.equal(result.reason, 'HTTPS request failed: TimeoutError (request timed out)');
});
