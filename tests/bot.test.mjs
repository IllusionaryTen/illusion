import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, rmSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { createHash } from 'node:crypto';
import { commandFor, loadRelease, loadAndroidRelease, runBot, miniAppUrl } from '../bot/index.mjs';

test('download bot responds only to direct messages addressed to this bot', () => {
  const update = { message: { text: '/download@IllusionBot', chat: { id: 1, type: 'private' } } };
  assert.equal(commandFor(update, 'illusionbot').command, '/download');
  assert.equal(commandFor(update, 'otherbot'), null);
  assert.equal(commandFor({ message: { text: '/download', chat: { id: 1, type: 'group' } } }), null);
  assert.equal(commandFor({ callback_query: {} }), null);
});
test('release validation, actual multipart upload construction, cached file IDs and offsets', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'illusion-bot-test-'));
  try {
    const data = Buffer.from('MZ-illusion-test-fixture');
    const sha256 = createHash('sha256').update(data).digest('hex');
    const manifest = join(dir, 'release.json'), statePath = join(dir, 'state.json');
    const release = { file: 'Illusion.exe', version: '0.1.0', bytes: data.length, sha256 };
    writeFileSync(join(dir, 'Illusion.exe'), data);writeFileSync(manifest, JSON.stringify(release));
    assert.equal(loadRelease(manifest).sha256, sha256);
    const calls = [];
    let run = 0;
    const telegramFetch = async (url, options) => {
      const method = url.split('/').at(-1);calls.push({ method, options });
      let result;
      if (method === 'getMe') result = { username: 'IllusionBot' };
      else if (method === 'getWebhookInfo') result = { url: '' };
      else if (method === 'getUpdates') { run++;result = [{ update_id: run, message: { text: '/download', chat: { id: run, type: 'private' } } }]; }
      else if (method === 'sendDocument') result = { document: { file_id: 'cached-file-id' } };
      else throw new Error('Unexpected API call: ' + method);
      return { json: async () => ({ ok: true, result }) };
    };
    const config = { token: '123456:test-token', manifest, statePath, telegramFetch, maxPolls: 1 };
    await runBot(config);
    const upload = calls.find(c => c.method === 'sendDocument');
    assert.ok(upload.options.body instanceof FormData);
    const file = upload.options.body.get('document');
    assert.deepEqual(Buffer.from(await file.arrayBuffer()), data);
    assert.equal(upload.options.body.get('chat_id'), '1');
    assert.ok(upload.options.body.get('caption').includes(sha256));
    assert.equal(JSON.parse(readFileSync(statePath)).offset, 2);
    calls.length = 0;
    await runBot(config);
    const cached = JSON.parse(calls.find(c => c.method === 'sendDocument').options.body);
    assert.equal(cached.document, 'cached-file-id');
    assert.equal(JSON.parse(calls.find(c => c.method === 'getUpdates').options.body).offset, 2);
    writeFileSync(join(dir, 'Illusion.exe'), 'tampered');
    assert.throws(() => loadRelease(manifest), /checksum or size/);
    writeFileSync(manifest, JSON.stringify({ ...release, file: '../outside.exe' }));
    assert.throws(() => loadRelease(manifest), /next to the manifest/);
  } finally { if (!dir.startsWith(join(tmpdir(), 'illusion-bot-test-'))) throw new Error('Unexpected test path');rmSync(dir, { recursive: true, force: true }); }
});
test('missing bot token fails before network access', async () => {
  await assert.rejects(runBot({ token: '' }), /TELEGRAM_BOT_TOKEN/);
});
test('Mini App links use only configured HTTPS origins, preserving local desktop downloads',()=>{
  assert.equal(miniAppUrl({}),null);assert.equal(miniAppUrl({connection:{profile:{relay:'http://127.0.0.1:3789'}}}),null);assert.equal(miniAppUrl({connection:{profile:{relay:'https://relay.example.test'}}}),'https://relay.example.test/app/');assert.throws(()=>miniAppUrl({connection:{profile:{relay:'https://user:secret@relay.example.test'}}}),/HTTPS relay origin/);
});
test('rejected Telegram credentials give actionable setup instructions without exposing the token', async () => {
  const token = '123456:invalid-token-fixture';
  await assert.rejects(runBot({ token, telegramFetch: async () => ({ json: async () => ({ ok: false, error_code: 401, description: 'Unauthorized' }) }) }), error => {
    assert.match(error.message, /Configure-Telegram\.cmd/);
    assert.match(error.message, /401/);
    assert.ok(!error.message.includes(token));
    return true;
  });
});
test('Telegram /connect distributes only a validated public connection profile', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'illusion-bot-connect-test-'));
  try {
    const data = Buffer.from('MZ-test-app'), profile = Buffer.from(JSON.stringify({ format: 'illusion.connection', version: 1, name: 'Illusion friends', relay: 'https://relay.example.test' }));
    const digest = b => createHash('sha256').update(b).digest('hex');
    const manifest = join(dir, 'release.json');
    writeFileSync(join(dir, 'Illusion.exe'), data);writeFileSync(join(dir, 'Connection.illusion'), profile);
    writeFileSync(manifest, JSON.stringify({ version: '0.3.0', file: 'Illusion.exe', bytes: data.length, sha256: digest(data), connection: { file: 'Connection.illusion', bytes: profile.length, sha256: digest(profile), configured: true } }));
    const sent = [];
    await runBot({ token: '123456:test-token', manifest, statePath: join(dir, 'state.json'), maxPolls: 1, telegramFetch: async (url, options) => {
      const method = url.split('/').at(-1);let result;
      if (method === 'getMe') result = { username: 'IllusionBot' };
      else if (method === 'getWebhookInfo') result = { url: '' };
      else if (method === 'setChatMenuButton' || method === 'setMyCommands') result=true;
      else if (method === 'getUpdates') result = [{ update_id: 1, message: { text: '/connect', chat: { id: 1, type: 'private' } } }];
      else if (method === 'sendDocument') {sent.push(options.body);result = { document: { file_id: 'profile-file' } };}
      else throw new Error('Unexpected request: ' + method);
      return { json: async () => ({ ok: true, result }) };
    } });
    assert.equal(sent.length, 1);const delivered = JSON.parse(await sent[0].get('document').text());
    assert.equal(delivered.relay, 'https://relay.example.test');assert.deepEqual(Object.keys(delivered).sort(), ['format', 'name', 'relay', 'version']);
    writeFileSync(join(dir, 'Connection.illusion'), '{}');assert.throws(() => loadRelease(manifest), /checksum or size/);
  } finally {if (!dir.startsWith(join(tmpdir(), 'illusion-bot-connect-test-'))) throw new Error('Unexpected path');rmSync(dir, { recursive: true, force: true });}
});
test('Android APK loader checks the release manifest against the exact artifact bytes', () => {
  const dir=mkdtempSync(join(tmpdir(),'illusion-android-release-'));try{const data=Buffer.from([0x50,0x4b,0x03,0x04,0x01]),file='Illusion-0.8.1.apk';writeFileSync(join(dir,file),data);const manifest=join(dir,'android-release.json');writeFileSync(manifest,JSON.stringify({version:'0.8.1',file,bytes:data.length,sha256:createHash('sha256').update(data).digest('hex')}));assert.deepEqual(loadAndroidRelease(manifest).data,data);writeFileSync(manifest,JSON.stringify({version:'0.8.1',file,bytes:data.length,sha256:'0'.repeat(64)}));assert.throws(()=>loadAndroidRelease(manifest),/checksum or size/);}finally{assert.ok(dir.startsWith(join(tmpdir(),'illusion-android-release-')));rmSync(dir,{recursive:true,force:true});}
});
test('Telegram Mini App configures menu and sends HTTPS web_app launch buttons without SDK or tokens',async()=>{
  const dir=mkdtempSync(join(tmpdir(),'illusion-bot-miniapp-'));try{
    const data=Buffer.from('MZ-webapp-fixture'),profile=Buffer.from(JSON.stringify({format:'illusion.connection',version:1,name:'Illusion',relay:'https://relay.example.test'})),digest=b=>createHash('sha256').update(b).digest('hex');
    writeFileSync(join(dir,'Illusion.exe'),data);writeFileSync(join(dir,'Connection.illusion'),profile);const manifest=join(dir,'release.json');writeFileSync(manifest,JSON.stringify({version:'0.5.0',file:'Illusion.exe',bytes:data.length,sha256:digest(data),connection:{file:'Connection.illusion',bytes:profile.length,sha256:digest(profile),configured:true}}));const calls=[];
    await runBot({token:'123456:fixture-token',manifest,statePath:join(dir,'state.json'),maxPolls:1,telegramFetch:async(url,options)=>{const method=url.split('/').at(-1),body=JSON.parse(options.body);calls.push({method,body});let result=true;if(method==='getMe')result={username:'IllusionBot'};if(method==='getWebhookInfo')result={url:''};if(method==='getUpdates')result=[{update_id:1,message:{text:'/app',chat:{type:'private',id:1}}}];return {json:async()=>({ok:true,result})};}});
    const menu=calls.find(c=>c.method==='setChatMenuButton');assert.equal(menu.body.menu_button.web_app.url,'https://relay.example.test/app/');const message=calls.find(c=>c.method==='sendMessage');assert.equal(message.body.reply_markup.inline_keyboard[0][0].web_app.url,'https://relay.example.test/app/');assert.ok(!JSON.stringify(message).includes('fixture-token'));
    const commands=calls.find(c=>c.method==='setMyCommands').body.commands.map(c=>c.command);assert.ok(commands.includes('apk'));assert.ok(!commands.includes('android'));assert.ok(!commands.includes('version'));assert.equal(message.body.reply_markup.inline_keyboard[1][0].url,'https://relay.example.test/policies/privacy');
  }finally{assert.ok(dir.startsWith(join(tmpdir(),'illusion-bot-miniapp-')));rmSync(dir,{recursive:true,force:true});}
});
