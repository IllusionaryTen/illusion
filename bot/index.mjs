import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { resolve, dirname, basename } from 'node:path';
import { createHash } from 'node:crypto';
import { pathToFileURL } from 'node:url';
import { loadConnection } from '../scripts/connection-profile.mjs';

export function loadRelease(manifestPath) {
  const path = resolve(manifestPath);
  const release = JSON.parse(readFileSync(path, 'utf8').replace(/^\uFEFF/, ''));
  if (typeof release.file !== 'string' || basename(release.file) !== release.file) throw new Error('Release file must be next to the manifest.');
  const filePath = resolve(dirname(path), release.file);
  const bytes = readFileSync(filePath);
  if (bytes.length > 50_000_000) throw new Error('Release exceeds the Telegram Bot API 50 MB limit.');
  const sha256 = createHash('sha256').update(bytes).digest('hex');
  if (sha256 !== release.sha256 || bytes.length !== release.bytes) throw new Error('Release checksum or size does not match. Rebuild the desktop app.');
  let connection = null;
  if (release.connection) {
    const info = release.connection;
    if (typeof info.file !== 'string' || basename(info.file) !== info.file) throw new Error('Connection file must be next to the release manifest.');
    const profilePath = resolve(dirname(path), info.file), profileBytes = readFileSync(profilePath);
    if (profileBytes.length > 6000 || profileBytes.length !== info.bytes || createHash('sha256').update(profileBytes).digest('hex') !== info.sha256) throw new Error('Connection invitation checksum or size does not match. Rebuild the release.');
    const profile = loadConnection(profilePath);
    if (Boolean(profile.relay) !== info.configured) throw new Error('Connection readiness does not match the release manifest.');
    connection = { profile, data: profileBytes, file: info.file };
  }
  return { ...release, filePath, data: bytes, connection };
}
export function loadAndroidRelease(manifestPath = './dist/android-release.json') {
  if (!existsSync(manifestPath)) return null;
  const path = resolve(manifestPath), release = JSON.parse(readFileSync(path, 'utf8').replace(/^\uFEFF/, ''));
  if (!/^\d+\.\d+\.\d+$/.test(release.version) || release.file !== `Illusion-${release.version}.apk` || basename(release.file) !== release.file || !Number.isSafeInteger(release.bytes) || release.bytes <= 0 || release.bytes > 50_000_000 || !/^[a-f0-9]{64}$/.test(release.sha256)) throw new Error('Android release manifest is invalid.');
  const filePath = resolve(dirname(path), release.file), data = readFileSync(filePath), sha256 = createHash('sha256').update(data).digest('hex');
  if (data.length > 50_000_000 || data.length !== release.bytes || sha256 !== release.sha256) throw new Error('Android APK checksum or size does not match. Rebuild the Android app.');
  return { ...release, filePath, data };
}
export function commandFor(update, botUsername = '') {
  const message = update.message;
  if (!message || message.chat?.type !== 'private' || typeof message.text !== 'string') return null;
  const first = message.text.trim().split(/\s/)[0];
  const [command, target] = first.split('@');
  if (target && target.toLowerCase() !== botUsername.toLowerCase()) return null;
  return { chatId: message.chat.id, command: command.toLowerCase() };
}
export function miniAppUrl(release){const origin=release.connection?.profile.relay;if(!origin)return null;const url=new URL(origin);if(url.protocol!=='https:')return null;if(url.username||url.password||url.pathname!=='/'||url.search||url.hash)throw new Error('The Telegram Mini App needs a public HTTPS relay origin.');return url.origin+'/app/';}
export async function runBot({ token = process.env.TELEGRAM_BOT_TOKEN, manifest = process.env.RELEASE_MANIFEST || './dist/release.json', statePath = './.bot-state.json', telegramFetch = fetch, maxPolls = Infinity } = {}) {
  if (!token || !/^\d+:[\w-]+$/.test(token)) throw new Error('Set TELEGRAM_BOT_TOKEN in .env. Create a bot with @BotFather first.');
  let state = {};
  if (existsSync(statePath)) { try { state = JSON.parse(readFileSync(statePath, 'utf8')); } catch { throw new Error('Bot state file is invalid. Restore it or remove it before restarting.'); } }
  // State belongs to this bot only; never reuse another bot's file ID.
  const botId = token.split(':')[0];
  if (state.botId !== botId) state = { botId, offset: 0 };
  async function api(method, body, multipart = false) {
    let response;
    try {
      response = await telegramFetch(`https://api.telegram.org/bot${token}/${method}`, {
        method: 'POST', headers: multipart ? undefined : { 'Content-Type': 'application/json' },
        body: multipart ? body : JSON.stringify(body), signal: AbortSignal.timeout(method === 'getUpdates' ? 40_000 : 120_000)
      });
    } catch { throw new Error('Telegram could not be reached. Check the network.'); }
    const result = await response.json();
    if (!result.ok) {
      if (result.error_code === 401) throw new Error('Telegram rejected the bot token (401). Run Configure-Telegram.cmd with the current API token from @BotFather. Paste it only once; the prompt hides your input.');
      const err = new Error(`Telegram ${method} failed (${result.error_code}): ${result.description || 'Unknown error'}`);
      err.retryAfter = result.parameters?.retry_after || 0;
      throw err;
    }
    return result.result;
  }
  function save() { writeFileSync(statePath, JSON.stringify(state, null, 2)); }
  const identity = await api('getMe', {});
  const webhook = await api('getWebhookInfo', {});
  if (webhook.url) throw new Error('This bot has a webhook configured. Use a separate bot or deliberately remove its webhook before running this downloader.');
  let release = loadRelease(manifest);
  const launchUrl=miniAppUrl(release);
  if(launchUrl){await api('setChatMenuButton',{menu_button:{type:'web_app',text:'Open Illusion',web_app:{url:launchUrl}}});await api('setMyCommands',{commands:[{command:'app',description:'Open Illusion inside Telegram'},{command:'apk',description:'Download Android APK'},{command:'privacy',description:'Privacy and service policies'},{command:'download',description:'Windows desktop version'},{command:'connect',description:'Desktop connection invitation'}]});}
  console.log(`Illusion download bot @${identity.username} is running. Release ${release.version} · ${release.bytes} bytes.`);
  let stopping = false;
  const stop = () => { stopping = true; };
  for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, stop);
  const recent = new Map();
  let polls = 0;
  while (!stopping && polls++ < maxPolls) {
    try {
      const updates = await api('getUpdates', { offset: state.offset || 0, timeout: 25, allowed_updates: ['message'] });
      for (const update of updates) {
        if (stopping) break;
        const action = commandFor(update, identity.username);
        if (action) {
          const { chatId, command } = action;
          const now = Date.now();
          for (const [id, expiry] of recent) if (expiry < now) recent.delete(id);
          const throttleKey = String(chatId);
          if ((recent.get(throttleKey) || 0) < now) {
            recent.set(throttleKey, now + 3000);
            release = loadRelease(manifest);
            if(command==='/apk'){
              const apk=loadAndroidRelease();
              if(!apk)await api('sendMessage',{chat_id:chatId,text:'The Android APK has not been built yet. Use /app to open Illusion in Telegram.'});
              else{const form=new FormData();form.append('chat_id',String(chatId));form.append('caption',`Illusion ${apk.version} · Android APK\nPreview build · Android 6.0+\nSHA-256: ${apk.sha256}\nInstall only if you trust this app.`);form.append('document',new Blob([apk.data],{type:'application/vnd.android.package-archive'}),basename(apk.filePath));await api('sendDocument',form,true);}
            } else if(command==='/app' || command==='/privacy'){
              const url=miniAppUrl(release);
              if(!url)await api('sendMessage',{chat_id:chatId,text:'Illusion Mini App is built, but its public HTTPS connection is not online yet. The host must finish Start-Public.cmd on a network where Cloudflare connects. No separate paid website is required.'});
              else await api('sendMessage',{chat_id:chatId,text:command==='/privacy'?'Read the Privacy policy, Terms, refund/storage notices and operator details inside Illusion. Deletion requests are available after signing in.':'Open Illusion inside Telegram. Your Illusion account and device keys stay separate from Telegram identity. This is an unaudited preview.',reply_markup:{inline_keyboard:[[{text:'Open Illusion',web_app:{url}}],[{text:'Privacy policy',url:new URL('/policies/privacy',url).href}]]}});
            } else if (command === '/download') {
              const setup = release.connection?.profile.relay ? `Connection included: ${release.connection.profile.name}. Open the app and sign in; all chats use it automatically.` : 'A public network has not been configured yet. Import a connection invitation from your host, or use Advanced for local testing.';
              const caption = `Illusion ${release.version}\nPrivate. Customizable. Free.\n\nWindows 10/11 · portable preview\n${setup}\nSHA-256: ${release.sha256}\n\nAndroid: /apk · Telegram Mini App or home-screen install.\n\n${release.notes || ''}`;
              let sent;
              if (state.sha256 === release.sha256 && state.fileId) {
                sent = await api('sendDocument', { chat_id: chatId, document: state.fileId, caption });
              } else {
                const form = new FormData();
                form.append('chat_id', String(chatId));
                form.append('caption', caption);
                form.append('document', new Blob([release.data], { type: 'application/octet-stream' }), basename(release.filePath));
                sent = await api('sendDocument', form, true);
              }
              state.fileId = sent.document.file_id; state.sha256 = release.sha256;
            } else if (command === '/connect') {
              if (release.connection?.profile.relay) {
                const form = new FormData();form.append('chat_id', String(chatId));
                form.append('caption', `Connection for ${release.connection.profile.name}. In Illusion, choose Import connection invitation and select this file. One connection supports all your chats. Use only connection files from a host you trust.`);
                form.append('document', new Blob([release.connection.data], { type: 'application/json' }), release.connection.file);
                await api('sendDocument', form, true);
              } else await api('sendMessage', { chat_id: chatId, text: 'The host has not configured a public Illusion relay yet. The host needs to run Configure-Connection.cmd with the address of a running HTTPS relay and rebuild. This bot does not provide chat hosting by itself.' });
            } else {
              const url=miniAppUrl(release);await api('sendMessage', { chat_id: chatId, text: '◈ Illusion\nPrivate. Customizable. Free.\n\n/app — Open the Telegram Mini App\n/apk — Download the Android APK\n/privacy — Policies and data controls\n/download — Windows desktop version\n/connect — Desktop invitation\n\nThe Mini App needs the host’s public HTTPS relay online. This custom encryption is an unaudited preview. No payments, ads or marketing emails are enabled.',...(url?{reply_markup:{inline_keyboard:[[{text:'Open Illusion',web_app:{url}}],[{text:'Download Android APK',url:new URL('/download/android/'+(loadAndroidRelease()?.file||'Illusion-1.6.1.apk'),url).href}]]}}:{}) });
            }
          }
        }
        state.offset = update.update_id + 1; save();
      }
    } catch (e) {
      // Error messages above deliberately omit request URLs because they contain the bot token.
      console.error(e.message);
      if (!stopping) await new Promise(r => setTimeout(r, Math.min(60_000, Math.max(5000, (e.retryAfter || 0) * 1000))));
    }
  }
  save();
  for (const signal of ['SIGINT', 'SIGTERM']) process.removeListener(signal, stop);
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  runBot().catch(e => { console.error(e.message); process.exitCode = 1; });
}
