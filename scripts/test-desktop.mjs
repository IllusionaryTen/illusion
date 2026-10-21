import { createMessenger } from '../server/index.mjs';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawn } from 'node:child_process';
import { createServer, request } from 'node:http';
import assert from 'node:assert/strict';

const dataDir = mkdtempSync(join(tmpdir(), 'illusion-native-qa-'));
const app = createMessenger({ dataDir });
let redirectedRequests = 0;
let offline = false;
let dropNextAck = false;
let droppedAcks = 0;
const liveResponses = new Set();
const proxy = createServer((req, res) => {
  if (offline) { res.writeHead(503, { 'Content-Type': 'application/json' });res.end('{"error":"Simulated connection outage"}');return; }
  liveResponses.add(res);
  const upstream = request({ hostname: '127.0.0.1', port: app.server.address().port, path: req.url, method: req.method, headers: req.headers }, incoming => {
    if (res.destroyed) { incoming.destroy();return; }
    if (dropNextAck && req.method === 'POST' && req.url === '/v1/messages' && incoming.statusCode === 200) {dropNextAck = false;droppedAcks++;incoming.resume();res.destroy();return;}
    res.writeHead(incoming.statusCode, incoming.headers);incoming.pipe(res);
  });
  upstream.on('error', () => { if (!res.destroyed && !res.writableEnded) {if (!res.headersSent)res.writeHead(503, { 'Content-Type': 'application/json' });res.end('{"error":"Upstream unavailable"}');} });
  res.on('close', () => { liveResponses.delete(res);upstream.destroy(); });
  req.pipe(upstream);
});
const redirect = createServer((req, res) => {
  if (req.url === '/lose-ack') {dropNextAck = true;res.end('{}');}
  else if (req.url === '/offline' || req.url === '/online') {
    offline = req.url === '/offline';
    if (offline) for (const active of liveResponses) {if (!active.headersSent)active.writeHead(503, { 'Content-Type': 'application/json' });active.end('{"error":"Simulated connection outage"}');}
    res.end('{}');
  }
  else if (req.url === '/redirect') { res.writeHead(307, { Location: '/destination', 'Content-Type': 'application/json' });res.end(JSON.stringify({ error: 'Redirect fixture' })); }
  else { redirectedRequests++;res.end('{}'); }
});
try {
  await new Promise(r => app.server.listen(0, '127.0.0.1', r));
  await new Promise(r => proxy.listen(0, '127.0.0.1', r));
  await new Promise(r => redirect.listen(0, '127.0.0.1', r));
  const child = spawn(resolve('dist/DesktopTests.exe'), [`http://127.0.0.1:${proxy.address().port}`, `http://127.0.0.1:${redirect.address().port}`], { stdio: 'inherit', windowsHide: true });
  const timeout = setTimeout(() => { console.error('Native UI test exceeded its time limit.');child.kill(); }, 180_000);
  process.exitCode = await new Promise((res, reject) => { child.once('exit', code => res(code ?? 1));child.once('error', reject); });
  clearTimeout(timeout);
  assert.equal(redirectedRequests, 0, 'Native client must not follow redirects.');
  if (process.exitCode === 0) assert.equal(droppedAcks, 1, 'Native test must exercise a lost delivery acknowledgement.');
} finally {
  proxy.closeAllConnections();await new Promise(r => proxy.close(r));
  await app.close();
  await new Promise(r => redirect.close(r));
  if (!dataDir.startsWith(join(tmpdir(), 'illusion-native-qa-'))) throw new Error('Unexpected test directory');
  rmSync(dataDir, { recursive: true, force: true });
}
