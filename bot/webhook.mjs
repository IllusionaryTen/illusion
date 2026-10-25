import {secretMatches} from '../shared/secrets.mjs';
export function botReply(update,origin,release,username='',androidRelease=null){
  const message=update.message;if(message?.chat?.type!=='private'||typeof message.text!=='string')return null;
  const [rawCommand,target]=message.text.trim().split(/\s/)[0].split('@'),command=rawCommand.toLowerCase();if(target&&target.toLowerCase()!==username.toLowerCase())return null;
  const chat_id=message.chat.id,url=origin+'/app/';
  if(command==='/apk'&&androidRelease)return {method:'sendDocument',body:{chat_id,document:origin+'/download/android/'+androidRelease.file,caption:'Illusion '+androidRelease.version+' · Android APK\nPreview build · Android 6.0+\nSHA-256: '+androidRelease.sha256+'\nInstall only if you trust this app. Android may ask you to allow this source.'}};
  if(command==='/apk')return {method:'sendMessage',body:{chat_id,text:'The Android APK is not published yet. Use Illusion inside Telegram or install the web app from your browser.',reply_markup:{inline_keyboard:[[{text:'Open Illusion in Telegram',web_app:{url}}],[{text:'Android install instructions',url:origin+'/android/'}]]}}};
  if(command==='/download')return {method:'sendDocument',body:{chat_id,document:origin+'/download/'+release.file,caption:'Illusion '+release.version+' · Windows\nPrivate. Customizable. Free.\nSHA-256: '+release.sha256+'\nCloud connection included.'}};
  if(command==='/connect')return {method:'sendDocument',body:{chat_id,document:origin+'/download/Illusion-Connection.illusion',caption:'Import this connection invitation in Illusion. The cloud relay serves all your chats; the host laptop can be off.'}};
  return {method:'sendMessage',body:{chat_id,text:command==='/privacy'?'Read Illusion’s draft policies and data controls. Operator details are still pending.':'◈ Illusion\nPrivate. Customizable. Free.\n\n/app — Open Illusion in Telegram\n/apk — Download Android APK\n/download — Windows desktop app\n/connect — Desktop connection invitation\n\nThis preview uses custom, unaudited encryption. Accounts are for adults 18+.',reply_markup:{inline_keyboard:[[{text:'Open Illusion',web_app:{url}}],[{text:'Download Android APK',url:origin+'/download/android/'+(androidRelease?.file||'Illusion-0.8.8.apk')}],[{text:'Privacy policy',url:origin+'/policies/privacy'}]]}}};
}
export async function telegramWebhook(request,{env,db,readJson,fetcher=fetch}){
  const supplied=request.headers.get('X-Telegram-Bot-Api-Secret-Token')||'';
  if(!env.TELEGRAM_BOT_TOKEN||!secretMatches(supplied,env.TELEGRAM_WEBHOOK_SECRET))return new Response('Not authorized',{status:401});
  let update;
  try{update=await readJson(request,100000);}catch{return new Response('Invalid update',{status:400});}
  if(!update||typeof update!=='object'||!Number.isSafeInteger(update.update_id)||update.update_id<0)return new Response('Invalid update',{status:400});
  const old=db.prepare('SELECT * FROM bot_updates WHERE id=?').get(update.update_id);
  if(old?.status==='done')return new Response('ok');
  if(old?.status==='processing'&&old.createdAt>Date.now()-60000)return new Response('Retry later',{status:503});
  // Claim synchronously before any fetch yields, so duplicate deliveries cannot
  // both send the same reply while the asset request is in flight.
  db.prepare("INSERT OR REPLACE INTO bot_updates VALUES(?,'processing',?)").run(update.update_id,Date.now());
  try{
    const origin=new URL(request.url).origin,manifest=await env.ASSETS.fetch(new Request(origin+'/download/release.json'));
    if(!manifest.ok)throw Error('Release not ready');
    const release=await manifest.json();if(!/^Illusion-\d+\.\d+\.\d+\.exe$/.test(release.file)||!/^[a-f0-9]{64}$/.test(release.sha256))throw Error('Release not ready');
    let androidRelease=null;const androidManifest=await env.ASSETS.fetch(new Request(origin+'/download/android/release.json'));
    if(androidManifest.ok){try{const candidate=await androidManifest.json();if(/^Illusion-[0-9.]+\.apk$/.test(candidate.file)&&/^\d+\.\d+\.\d+$/.test(candidate.version)&&candidate.file==='Illusion-'+candidate.version+'.apk'&&Number.isSafeInteger(candidate.bytes)&&candidate.bytes>0&&candidate.bytes<=50_000_000&&/^[a-f0-9]{64}$/.test(candidate.sha256))androidRelease=candidate;}catch{}}
    const reply=botReply(update,origin,release,env.BOT_USERNAME||'',androidRelease);
    if(!reply){db.prepare("UPDATE bot_updates SET status='done' WHERE id=?").run(update.update_id);return new Response('ok');}
    let body=JSON.stringify(reply.body),headers={'Content-Type':'application/json'};
    if(reply.method==='sendDocument'){
      const asset=await env.ASSETS.fetch(new Request(reply.body.document));if(!asset.ok)throw Error('Download not ready');
      if(androidRelease&&reply.body.document===origin+'/download/android/'+androidRelease.file){const bytes=await asset.arrayBuffer(),digest=Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',bytes)),x=>x.toString(16).padStart(2,'0')).join('');if(bytes.byteLength!==androidRelease.bytes||digest!==androidRelease.sha256)throw Error('APK integrity check failed');body=new FormData();for(const [key,value]of Object.entries(reply.body))if(key!=='document')body.append(key,String(value));body.append('document',new Blob([bytes],{type:'application/vnd.android.package-archive'}),androidRelease.file);headers=undefined;}else{
      // Telegram restricts document types fetched by URL. Upload the actual
      // executable/invitation as multipart bytes instead.
      body=new FormData();for(const [key,value]of Object.entries(reply.body))if(key!=='document')body.append(key,String(value));
      body.append('document',await asset.blob(),new URL(reply.body.document).pathname.split('/').at(-1));headers=undefined;}
    }
    const response=await fetcher('https://api.telegram.org/bot'+env.TELEGRAM_BOT_TOKEN+'/'+reply.method,{method:'POST',headers,body,signal:AbortSignal.timeout(10000)});
    const result=await response.json();if(!response.ok||!result.ok)throw Error('Telegram request failed');
    db.prepare("UPDATE bot_updates SET status='done' WHERE id=?").run(update.update_id);
    return new Response('ok');
  }catch{db.prepare('DELETE FROM bot_updates WHERE id=?').run(update.update_id);return new Response('Telegram temporarily unavailable',{status:503});}
}
