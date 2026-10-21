import {test} from 'node:test';
import assert from 'node:assert/strict';
import {DatabaseSync} from 'node:sqlite';
import {createHash} from 'node:crypto';
import {telegramWebhook,botReply} from '../bot/webhook.mjs';
function fixture(){
  const db=new DatabaseSync(':memory:');db.exec('CREATE TABLE bot_updates(id INTEGER PRIMARY KEY,status TEXT,createdAt INTEGER)');
  const calls=[],release={file:'Illusion-0.6.1.exe',version:'0.6.1',sha256:'a'.repeat(64)},bytes=new Uint8Array([77,90,1,2]),apkBytes=new Uint8Array([80,75,3,4]),apkRelease={file:'Illusion-0.8.1.apk',version:'0.8.1',bytes:apkBytes.length,sha256:createHash('sha256').update(apkBytes).digest('hex')};
  const env={TELEGRAM_BOT_TOKEN:'123:synthetic-only',TELEGRAM_WEBHOOK_SECRET:'synthetic-webhook-secret',BOT_USERNAME:'IllusionBot',ASSETS:{fetch:async req=>{const path=new URL(req.url).pathname;if(path==='/download/release.json')return Response.json(release);if(path==='/download/android/release.json')return Response.json(apkRelease);if(path.endsWith('.apk'))return new Response(apkBytes);return new Response(bytes);}}};
  const readJson=async r=>r.json(),fetcher=async(url,options)=>{calls.push({url,options});return Response.json({ok:true});};
  const update=id=>({update_id:id,message:{text:'/download',chat:{type:'private',id:42}}});
  const request=(body,secret=env.TELEGRAM_WEBHOOK_SECRET)=>new Request('https://illusion.example/telegram/webhook',{method:'POST',headers:{'Content-Type':'application/json','X-Telegram-Bot-Api-Secret-Token':secret},body:JSON.stringify(body)});
  return {db,calls,env,readJson,fetcher,update,request,apkBytes,apkRelease};
}
test('cloud webhook rejects untrusted requests before reading or forwarding data',async()=>{
  const f=fixture();try{assert.equal((await telegramWebhook(f.request(f.update(1),'wrong'),f)).status,401);assert.equal(f.calls.length,0);assert.equal(f.db.prepare('SELECT COUNT(*) AS n FROM bot_updates').get().n,0);}finally{f.db.close();}
});
test('cloud bot uploads actual executable bytes and suppresses duplicate updates',async()=>{
  const f=fixture();try{assert.equal((await telegramWebhook(f.request(f.update(2)),f)).status,200);assert.equal(f.calls.length,1);const form=f.calls[0].options.body;assert.ok(form instanceof FormData);assert.equal(form.get('document').name,'Illusion-0.6.1.exe');assert.deepEqual(new Uint8Array(await form.get('document').arrayBuffer()),new Uint8Array([77,90,1,2]));assert.equal(form.get('chat_id'),'42');assert.equal((await telegramWebhook(f.request(f.update(2)),f)).status,200);assert.equal(f.calls.length,1);assert.deepEqual(Object.keys(f.db.prepare('SELECT * FROM bot_updates').get()),['id','status','createdAt']);}finally{f.db.close();}
});
test('overlapping webhook deliveries acquire a single reply claim before awaiting assets',async()=>{
  const f=fixture();let release;const gate=new Promise(r=>release=r),original=f.env.ASSETS.fetch;f.env.ASSETS.fetch=async req=>{await gate;return original(req);};
  try{const first=telegramWebhook(f.request(f.update(3)),f);await new Promise(r=>setImmediate(r));const duplicate=await telegramWebhook(f.request(f.update(3)),f);assert.equal(duplicate.status,503);release();assert.equal((await first).status,200);assert.equal(f.calls.length,1);}finally{release();f.db.close();}
});
test('Telegram failures can retry without retaining message text or chat IDs',async()=>{
  const f=fixture();try{assert.equal((await telegramWebhook(f.request(f.update(4)),{...f,fetcher:async()=>Response.json({ok:false},{status:503})})).status,503);assert.equal(f.db.prepare('SELECT COUNT(*) AS n FROM bot_updates').get().n,0);assert.equal((await telegramWebhook(f.request(f.update(4)),f)).status,200);}finally{f.db.close();}
});
test('cloud bot ignores groups and other bot mentions and creates same-origin Mini App links',()=>{
  const f=fixture();try{const u=f.update(5);u.message.chat.type='group';assert.equal(botReply(u,'https://illusion.example',{},'IllusionBot'),null);u.message.chat.type='private';u.message.text='/download@AnotherBot';assert.equal(botReply(u,'https://illusion.example',{},'IllusionBot'),null);u.message.text='/APP';const reply=botReply(u,'https://illusion.example',{},'IllusionBot');assert.equal(reply.body.reply_markup.inline_keyboard[0][0].web_app.url,'https://illusion.example/app/');}finally{f.db.close();}
});
test('retired commands have no dedicated handler; APK remains downloadable',()=>{
  const f=fixture();try{for(const command of ['/android','/version']){const retired=botReply({message:{text:command,chat:{type:'private',id:42}}},'https://illusion.example',{},'IllusionBot',f.apkRelease);assert.equal(retired.method,'sendMessage');assert.match(retired.body.text,/\/apk/);assert.doesNotMatch(retired.body.text,new RegExp(command.replace('/','\\/')));}const apk=botReply({message:{text:'/apk',chat:{type:'private',id:42}}},'https://illusion.example',{},'IllusionBot',f.apkRelease);assert.equal(apk.method,'sendDocument');assert.equal(apk.body.document,'https://illusion.example/download/android/Illusion-0.8.1.apk');}finally{f.db.close();}
});
test('cloud bot verifies and uploads the exact Android APK bytes',async()=>{
  const f=fixture();try{const u=f.update(6);u.message.text='/apk';assert.equal((await telegramWebhook(f.request(u),f)).status,200);const form=f.calls[0].options.body;assert.ok(form instanceof FormData);assert.equal(form.get('document').name,'Illusion-0.8.1.apk');assert.deepEqual(new Uint8Array(await form.get('document').arrayBuffer()),f.apkBytes);}finally{f.db.close();}
});
