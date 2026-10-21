import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

export function connectionProfile(relay = '', name = 'Illusion') {
  if (typeof name !== 'string' || !name.trim() || name.trim().length > 48 || /[\u0000-\u001f\u007f]/.test(name)) throw new Error('Network name must be 1–48 characters without control characters.');
  let normalized = '';
  if (relay) {
    let url;
    try { url = new URL(relay.trim()); } catch { throw new Error('Enter a valid public HTTPS relay address.'); }
    const local = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname);
    if (!(url.protocol === 'https:' || (url.protocol === 'http:' && local)) || url.username || url.password || url.search || url.hash || url.pathname !== '/') throw new Error('Relay must use HTTPS, without credentials, a path, query or fragment. HTTP loopback is for local testing only.');
    normalized = url.origin;
  }
  return { format: 'illusion.connection', version: 1, name: name.trim(), relay: normalized };
}
export function loadConnection(path) {
  const data = JSON.parse(readFileSync(path, 'utf8').replace(/^\uFEFF/, ''));
  if (!data || data.format !== 'illusion.connection' || data.version !== 1 || Object.keys(data).some(k => !['format', 'version', 'name', 'relay'].includes(k))) throw new Error('Invalid connection profile.');
  return connectionProfile(data.relay, data.name);
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  const args = process.argv.slice(2);
  const value = key => { const index = args.indexOf(key);return index < 0 ? undefined : args[index + 1]; };
  try {
    const profile = connectionProfile(value('--relay') ?? process.env.DEFAULT_RELAY_URL ?? '', value('--name') ?? process.env.DEFAULT_NETWORK_NAME ?? 'Illusion');
    const out = value('--out');if (!out) throw new Error('Missing --out path.');
    writeFileSync(out, JSON.stringify(profile, null, 2) + '\n');
    console.log(profile.relay ? `Embedded network: ${profile.name} (${profile.relay})` : 'No public relay configured. Invitations and Advanced local testing are available.');
  } catch (error) {console.error(error.message);process.exitCode = 1;}
}
