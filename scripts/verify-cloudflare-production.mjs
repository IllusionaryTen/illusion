import assert from 'node:assert/strict';
import {readFileSync,writeFileSync} from 'node:fs';
import {parseEnv} from 'node:util';
import {createHash} from 'node:crypto';
import {readState,cloudAdmin} from './cloud-state.mjs';
import {loadRelease} from '../bot/index.mjs';
const state=readState(),release=loadRelease('dist/release.json'),origin=state.origin;let checks=0;
function check(c,m){assert.ok(c,m);checks++;console.log('PASS: '+m);}
const get=path=>fetch(origin+path,{redirect:'error',signal:AbortSignal.timeout(30000)});
const health=await (await get('/health')).json();check(health.app==='Illusion'&&health.version===release.version&&health.liveTransport==='websocket','Public HTTPS relay advertises the deployed cloud version');
const config=await (await get('/v1/public')).json();check(config.policyVersion==='2026-10-19.1'&&config.fonts.length===28&&!config.operator.configured,'Public consent/fonts/policy drafts match the deployed release');
const app=await get('/app/');check(app.ok&&(await app.text()).includes('Illusion'),'Public Mini App HTML is reachable');
check(app.headers.get('Content-Security-Policy')?.includes("script-src 'self'")&&!app.headers.has('Set-Cookie'),'Published app has its security policy and sets no HTTP cookies');
check((await get('/v1/chats')).status===401,'Public relay refuses unauthenticated chat access');
const untrusted=await fetch(origin+'/telegram/webhook',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}',signal:AbortSignal.timeout(20000)});check(untrusted.status===401,'Telegram webhook rejects a request without its secret');
for(const [file,sha]of [[release.file,release.sha256],['Illusion-Connection.illusion',createHash('sha256').update(release.connection.data).digest('hex')],['Illusion-Windows.zip',release.archive.sha256]]){
  const r=await get('/download/'+file),bytes=Buffer.from(await r.arrayBuffer());check(r.ok&&createHash('sha256').update(bytes).digest('hex')===sha,'Public '+file+' matches the local release checksum');
  if(file.endsWith('.illusion'))check(JSON.parse(bytes).relay===origin,'Published invitation points to the cloud relay');
}
const manifest=JSON.parse((await (await get('/download/release.json')).text()).replace(/^\uFEFF/,''));check(manifest.sha256===release.sha256&&manifest.version===release.version,'Published manifest matches the connected Windows build');
const androidRelease=JSON.parse(await (await get('/download/android/release.json')).text()),apk=await get('/download/android/'+androidRelease.file),apkBytes=Buffer.from(await apk.arrayBuffer());check(apk.ok&&androidRelease.version===release.version&&apkBytes.length===androidRelease.bytes&&createHash('sha256').update(apkBytes).digest('hex')===androidRelease.sha256,'Published Android APK matches its release manifest and checksum');
const status=await cloudAdmin('status');check(status.registrationOpen&&status.importHash===state.migrationHash,'Hosted registration is open after the verified account migration');
const token=parseEnv(readFileSync('.env','utf8')).TELEGRAM_BOT_TOKEN;
async function telegram(method){let r;try{r=await fetch('https://api.telegram.org/bot'+token+'/'+method,{method:'POST',headers:{'Content-Type':'application/json'},body:'{}',signal:AbortSignal.timeout(30000)});}catch{throw Error('Telegram settings could not be checked.');}const result=await r.json();if(!result.ok)throw Error('Telegram settings check failed.');return result.result;}
const hook=await telegram('getWebhookInfo');check(hook.url===origin+'/telegram/webhook','Telegram uses the hosted webhook');
const menu=await telegram('getChatMenuButton');check(menu.type==='web_app'&&menu.web_app.url===origin+'/app/','Telegram Open Illusion menu launches the public Mini App');
const commands=await telegram('getMyCommands');check(['app','privacy','download','connect','apk'].every(c=>commands.some(x=>x.command===c))&&!commands.some(x=>['android','version'].includes(x.command)),'Telegram exposes current download commands and omits retired commands');
writeFileSync('artifacts/cloud-production-qa.json',JSON.stringify({checkedAt:new Date().toISOString(),origin,version:release.version,sha256:release.sha256,checks,counts:status.counts,webhookConfigured:true,menuConfigured:true,pendingTelegramUpdates:hook.pending_update_count,telegramLastError:hook.last_error_message||null,notes:'Read-only production checks plus an untrusted webhook rejection. No real user messages were sent. Authenticated message exchange and multipart bot replies were tested in isolated runtimes; live Telegram client interaction remains a manual check.'},null,2)+'\n');
console.log(checks+' production checks passed. No real user messages were sent.');
