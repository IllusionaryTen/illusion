import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { generateKeyPairSync, randomBytes, randomUUID, publicEncrypt, createCipheriv, createHmac, sign, privateDecrypt, createDecipheriv } from 'node:crypto';
import { createMessenger, signatureText } from '../server/index.mjs';
const key = generateKeyPairSync('rsa', { modulusLength: 3072 });
const jwk = key.publicKey.export({ format: 'jwk' });
const xml = `<RSAKeyValue><Modulus>${Buffer.from(jwk.n,'base64url').toString('base64')}</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>`;
const dir = mkdtempSync(join(tmpdir(),'illusion-privacy-'));
let app, base, alice, bob, eve, clock=Date.now(), kept;
async function api(user,path,method='GET',body) { const r=await fetch(base+path,{method,headers:{'Content-Type':'application/json',...(user?{Authorization:'Bearer '+user.token}:{})},body:body&&JSON.stringify(body)});return {status:r.status,body:await r.json()}; }
async function start(){app=createMessenger({dataDir:dir,now:()=>clock});await new Promise(r=>app.server.listen(0,'127.0.0.1',r));base='http://127.0.0.1:'+app.server.address().port;}
async function register(username){return (await api(null,'/v1/register','POST',{username,password:'a sufficiently strong password',displayName:username,publicKey:xml,ageBand:'18+',acceptTerms:true,acknowledgePrivacy:true,policyVersion:'2026-10-19.1'})).body;}
function encrypted(text,{viewOnce=false,ttlSeconds=0}={}){
  const material=randomBytes(64),iv=randomBytes(16),cipher=createCipheriv('aes-256-cbc',material.subarray(0,32),iv),ciphertext=Buffer.concat([cipher.update(text),cipher.final()]);
  const keys={};for(const u of [alice,bob])keys[u.user.id]=publicEncrypt({key:key.publicKey,oaepHash:'sha1'},material).toString('base64');
  const envelope={v:2,iv:iv.toString('base64'),ciphertext:ciphertext.toString('base64'),mac:createHmac('sha256',material.subarray(32)).update(iv).update(ciphertext).digest('base64'),keys,signature:'',policy:{viewOnce,ttlSeconds}};
  const clientId=randomUUID();envelope.signature=sign('RSA-SHA256',Buffer.from(signatureText(alice.user.id,bob.user.id,clientId,envelope)),key.privateKey).toString('base64');
  return {payload:{recipientId:bob.user.id,clientId,envelope},material:material.toString('base64')};
}
async function send(text,p){const packet=encrypted(text,p);const result=await api(alice,'/v1/messages','POST',packet.payload);assert.equal(result.status,200,JSON.stringify(result));return {...packet,message:result.body.message};}
before(async()=>{await start();alice=await register('alice');bob=await register('bob');eve=await register('eve');});
after(async()=>{await app.close();assert.ok(dir.startsWith(join(tmpdir(),'illusion-privacy-')));rmSync(dir,{recursive:true,force:true});});
test('new conversations default to no expiry; v2 still stores ciphertext only',async()=>{
  assert.equal((await api(alice,'/v1/chat-settings?peer='+bob.user.id)).body.ttlSeconds,0);
  kept=await send('private forever by default');assert.equal(kept.message.expiresAt,0);
  assert.ok(!app.db.prepare('SELECT envelope FROM messages WHERE id=?').get(kept.message.id).envelope.includes('private forever'));
});
test('view-once history and sidebar never disclose wrapped keys or ciphertext',async()=>{
  const s=await send('only on deliberate open',{viewOnce:true});
  assert.equal(s.message.envelope,null);
  for(const user of [alice,bob]){const rows=(await api(user,'/v1/messages?peer='+(user===alice?bob.user.id:alice.user.id))).body.messages;assert.equal(rows.find(m=>m.id===s.message.id).envelope,null);const chats=(await api(user,'/v1/chats')).body.chats;assert.equal(chats[0].lastMessage.envelope,null);}
  assert.equal((await api(eve,'/v1/open','POST',{id:s.message.id})).status,410);
  assert.equal((await api(alice,'/v1/open','POST',{id:s.message.id})).status,410);
  const opens=await Promise.all([api(bob,'/v1/open','POST',{id:s.message.id}),api(bob,'/v1/open','POST',{id:s.message.id})]);assert.deepEqual(opens.map(r=>r.status).sort(),[200,410]);
  const e=opens.find(r=>r.status===200).body.message.envelope;
  const material=privateDecrypt({key:key.privateKey,oaepHash:'sha1'},Buffer.from(e.keys[bob.user.id],'base64'));
  const decipher=createDecipheriv('aes-256-cbc',material.subarray(0,32),Buffer.from(e.iv,'base64'));assert.equal(Buffer.concat([decipher.update(Buffer.from(e.ciphertext,'base64')),decipher.final()]).toString(),'only on deliberate open');
  assert.equal(app.db.prepare('SELECT envelope FROM messages WHERE id=?').get(s.message.id).envelope,'{}');
  const retry=await api(alice,'/v1/messages','POST',s.payload);assert.equal(retry.status,200);assert.equal(retry.body.message.envelope,null);assert.equal((await api(bob,'/v1/open','POST',{id:s.message.id})).status,410);
});
test('policy is signed and cannot be downgraded or spoofed',async()=>{
  const s=encrypted('protected policy',{viewOnce:true});s.payload.envelope.policy.viewOnce=false;assert.equal((await api(alice,'/v1/messages','POST',s.payload)).status,400);
  const bad=encrypted('bad timer');bad.payload.envelope.policy.ttlSeconds=15;assert.equal((await api(alice,'/v1/messages','POST',bad.payload)).status,400);
});
test('shared timer applies to future messages; expiry erases bodies and survives restart',async()=>{
  assert.equal((await api(bob,'/v1/chat-settings','PUT',{peer:alice.user.id,ttlSeconds:60})).status,200);
  assert.equal((await api(alice,'/v1/chat-settings?peer='+bob.user.id)).body.ttlSeconds,60);
  assert.equal((await api(alice,'/v1/messages','POST',encrypted('stale policy').payload)).status,409);
  const s=await send('expires offline',{ttlSeconds:60});const once=await send('expired before opening',{ttlSeconds:60,viewOnce:true});
  clock+=61000;
  let history=(await api(bob,'/v1/messages?peer='+alice.user.id)).body;assert.ok(history.removedIds.includes(s.message.id));assert.equal(history.messages.find(m=>m.id===s.message.id).envelope,null);assert.equal((await api(bob,'/v1/open','POST',{id:once.message.id})).status,410);
  assert.equal(app.db.prepare('SELECT envelope FROM messages WHERE id=?').get(s.message.id).envelope,'{}');assert.notEqual(app.db.prepare('SELECT envelope FROM messages WHERE id=?').get(kept.message.id).envelope,'{}');
  await app.close();await start();history=(await api(bob,'/v1/messages?peer='+alice.user.id)).body;assert.ok(history.removedIds.includes(s.message.id));
  await api(alice,'/v1/chat-settings','PUT',{peer:bob.user.id,ttlSeconds:0});
});
test('legacy encrypted-media envelopes remain readable without exposing the caption or file',async()=>{
  const photo=JSON.stringify({format:'illusion.message.v2',text:'private photo caption',media:{kind:'photo',mime:'image/jpeg',data:Buffer.alloc(100000,35).toString('base64')}});
  const s=await send(photo);assert.ok(s.message.envelope.ciphertext.length>100000);assert.ok(!JSON.stringify(s.message).includes('private photo caption'));
});
test('new photo and video payloads stay outside the encrypted text envelope and view-once erases their bytes',async()=>{
  const bytes=Buffer.concat([Buffer.alloc(4),Buffer.from('ftyp'),Buffer.alloc(32,7)]),data=bytes.toString('base64'),packet=encrypted('caption is still encrypted');packet.payload.media={kind:'video',mime:'video/mp4',data};
  const sent=await api(alice,'/v1/messages','POST',packet.payload);assert.equal(sent.status,200);assert.deepEqual(sent.body.message.media,{kind:'video',mime:'video/mp4',data});assert.ok(!JSON.stringify(sent.body.message.envelope).includes(data));assert.equal(JSON.parse(app.db.prepare('SELECT media FROM messages WHERE id=?').get(sent.body.message.id).media).data,data);
  const received=await api(bob,'/v1/messages?peer='+alice.user.id);assert.deepEqual(received.body.messages.find(row=>row.id===sent.body.message.id).media,{kind:'video',mime:'video/mp4',data});
  const chats=(await api(bob,'/v1/chats')).body.chats;assert.equal(chats.find(chat=>chat.id===alice.user.id).lastMessage.media,null);assert.equal(chats.find(chat=>chat.id===alice.user.id).lastMessage.attachment,true);
  const once=encrypted('temporary caption',{viewOnce:true});once.payload.media={kind:'video',mime:'video/mp4',data};const queued=await api(alice,'/v1/messages','POST',once.payload);assert.equal(queued.body.message.media,null);assert.equal((await api(bob,'/v1/messages?peer='+alice.user.id)).body.messages.find(row=>row.id===queued.body.message.id).media,null);
  const opened=await api(bob,'/v1/open','POST',{id:queued.body.message.id});assert.equal(opened.status,200);assert.deepEqual(opened.body.message.media,{kind:'video',mime:'video/mp4',data});assert.equal(app.db.prepare('SELECT media,mediaBytes FROM messages WHERE id=?').get(queued.body.message.id).media,'');assert.equal(app.db.prepare('SELECT mediaBytes FROM messages WHERE id=?').get(queued.body.message.id).mediaBytes,0);
  const invalid=encrypted('bad attachment');invalid.payload.media={kind:'video',mime:'image/png',data};assert.equal((await api(alice,'/v1/messages','POST',invalid.payload)).status,400);
});
test('profiles persist, expose only public fields, and reject external images and unsafe styles',async()=>{
  const profile={bio:'hello dream state',avatar:'',banner:'',accent:'#436FFF',textColor:'#FFFFFF',font:'Georgia',effect:'Prism'};
  const result=await api(alice,'/v1/profile','PUT',{displayName:'Alice Dream',profile});assert.equal(result.status,200);assert.equal(result.body.user.profile.bio,profile.bio);
  const lookup=await api(bob,'/v1/users?username=alice');assert.equal(lookup.body.user.profile.font,'Georgia');assert.equal(lookup.body.user.passwordHash,undefined);
  for(const change of [{avatar:'https://tracker.test/picture.png'},{banner:'data:image/svg+xml;base64,AAAA'},{accent:'url(http://x)'},{font:'unknown'},{bio:'x'.repeat(301)},{privateKey:'leak'}])assert.equal((await api(alice,'/v1/profile','PUT',{displayName:'Alice',profile:{...profile,...change}})).status,400);
});
test('reports require participants; evidence is opt-in and validates only selected message material',async()=>{
  const s=await send('reported selected text');
  assert.equal((await api(eve,'/v1/reports','POST',{targetId:alice.user.id,messageId:s.message.id,reason:'spam',note:''})).status,404);
  const hidden=await api(bob,'/v1/reports','POST',{targetId:alice.user.id,messageId:s.message.id,reason:'spam',note:'unwanted'});assert.equal(hidden.status,200);assert.equal(app.db.prepare('SELECT evidence FROM reports WHERE id=?').get(hidden.body.reportId).evidence,null);
  const evidence={envelope:s.message.envelope,material:s.material};const shared=await api(bob,'/v1/reports','POST',{targetId:alice.user.id,messageId:s.message.id,reason:'harassment',note:'',evidence});assert.equal(shared.status,200,JSON.stringify(shared));assert.equal(JSON.parse(app.db.prepare('SELECT evidence FROM reports WHERE id=?').get(shared.body.reportId).evidence).text,'reported selected text');
  const wrong=await api(bob,'/v1/reports','POST',{targetId:alice.user.id,messageId:s.message.id,reason:'spam',note:'',evidence:{...evidence,material:randomBytes(64).toString('base64')}});assert.equal(wrong.status,400);
  assert.equal((await api(bob,'/v1/reports')).status,404);
});
test('opened view-once content can be explicitly reported while no ciphertext remains on relay',async()=>{
  const packet=await send(JSON.stringify({format:'illusion.message.v2',text:'only this caption is evidence',media:{kind:'video',data:'private video bytes'}}),{viewOnce:true});
  const opened=await api(bob,'/v1/open','POST',{id:packet.message.id});assert.equal(opened.status,200);
  const r=await api(bob,'/v1/reports','POST',{targetId:alice.user.id,messageId:packet.message.id,reason:'other',note:'',evidence:{envelope:opened.body.message.envelope,material:packet.material}});assert.equal(r.status,200,JSON.stringify(r));
  const evidence=app.db.prepare('SELECT evidence FROM reports WHERE id=?').get(r.body.reportId).evidence;assert.equal(JSON.parse(evidence).text,'only this caption is evidence');assert.ok(!evidence.includes('private video bytes'));assert.equal(app.db.prepare('SELECT envelope FROM messages WHERE id=?').get(packet.message.id).envelope,'{}');
});
test('profile reports snapshot public details; blocking stops both directions; moderator bans revoke use',async()=>{
  const result=await api(bob,'/v1/reports','POST',{targetId:alice.user.id,reason:'other',note:'profile concern'});assert.equal(result.status,200);assert.equal(JSON.parse(app.db.prepare('SELECT evidence FROM reports WHERE id=?').get(result.body.reportId).evidence).profile.bio,'hello dream state');
  assert.equal((await api(bob,'/v1/block','PUT',{peer:alice.user.id,blocked:true})).status,200);assert.equal((await api(alice,'/v1/messages','POST',encrypted('blocked').payload)).status,403);
  await api(bob,'/v1/block','PUT',{peer:alice.user.id,blocked:false});assert.equal((await api(alice,'/v1/messages','POST',encrypted('unblocked').payload)).status,200);
  app.db.prepare('INSERT INTO bans VALUES(?,?)').run(eve.user.id,clock);assert.equal((await api(eve,'/v1/me')).status,403);assert.equal((await api(null,'/v1/login','POST',{username:'eve',password:'a sufficiently strong password'})).status,403);
});

test('audio and text attachments are validated before storage',async()=>{
  const wav=Buffer.concat([Buffer.from('RIFF'),Buffer.alloc(4),Buffer.from('WAVE'),Buffer.alloc(64,1)]);
  const mp3=Buffer.concat([Buffer.from('ID3'),Buffer.alloc(64,2)]);
  const post=async(media)=>{const packet=encrypted('attachment');packet.payload.media=media;return api(alice,'/v1/messages','POST',packet.payload);};
  assert.equal((await post({kind:'audio',mime:'audio/wav',data:wav.toString('base64')})).status,200);
  assert.equal((await post({kind:'audio',mime:'audio/mpeg',data:mp3.toString('base64')})).status,200);
  const doc={kind:'file',mime:'text/markdown',data:Buffer.from('# note\nhello').toString('base64')};
  assert.equal((await post(doc)).status,200);
  const received=await api(bob,'/v1/messages?peer='+alice.user.id);
  assert.ok(received.body.messages.some(row=>row.media&&row.media.kind==='file'&&row.media.mime==='text/markdown'));
  assert.equal((await post({kind:'audio',mime:'audio/wav',data:Buffer.from('not a wav at all').toString('base64')})).status,400);
  assert.equal((await post({kind:'file',mime:'application/pdf',data:doc.data})).status,400);
  const control=Buffer.from([0x68,0x00,0x69]);
  assert.equal((await post({kind:'file',mime:'text/plain',data:control.toString('base64')})).status,400);
  assert.equal((await post({kind:'file',mime:'text/plain',data:Buffer.alloc(200*1024+10,0x61).toString('base64')})).status,413);
  assert.equal((await post({kind:'audio',mime:'audio/mpeg',data:'!!!notbase64'})).status,400);
  assert.equal((await post({kind:'photo',mime:'image/png',data:Buffer.from('89504e470d0a1a0a','hex').toString('base64')})).status,400);
});

test('sessions can be listed and revoked without exposing tokens',async()=>{
  const second=await api(null,'/v1/login','POST',{username:'alice',password:'a sufficiently strong password'});
  assert.equal(second.status,200);
  const list=await api(alice,'/v1/sessions');
  assert.equal(list.status,200);
  assert.ok(list.body.sessions.length>=2);
  assert.ok(list.body.sessions.every(row=>/^[a-f0-9]{64}$/.test(row.id)&&!JSON.stringify(row).includes(second.body.token)));
  const other=list.body.sessions.find(row=>!row.current);
  const revoke=await api(alice,'/v1/sessions/'+other.id,'DELETE');
  assert.equal(revoke.status,200);assert.equal(revoke.body.current,false);
  assert.equal((await api(second.body,'/v1/me')).status,401);
  const removed=await api(alice,'/v1/sessions','DELETE');
  assert.equal(removed.status,200);
  assert.equal((await api(alice,'/v1/sessions')).body.sessions.length,1);
  assert.equal((await api(alice,'/v1/sessions/not-a-session','DELETE')).status,400);
});
