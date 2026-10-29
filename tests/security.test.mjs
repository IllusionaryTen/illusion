import {test,beforeEach,afterEach} from 'node:test';
import assert from 'node:assert/strict';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {DatabaseSync} from 'node:sqlite';
import {request} from 'node:http';
import {scryptSync,createHash,randomUUID,generateKeyPairSync} from 'node:crypto';
import {createMessenger} from '../server/index.mjs';
import {passwordMatches,passwordHash} from '../server/passwords.mjs';
import {durableLimiter} from '../server/limits.mjs';
import {storageBudget,storageLimit} from '../server/storage-budget.mjs';
import {imageDimensions,messageContent,validateViewerMedia} from '../web/security.mjs';
const jwk=generateKeyPairSync('rsa',{modulusLength:3072}).publicKey.export({format:'jwk'}),publicKey='<RSAKeyValue><Modulus>'+Buffer.from(jwk.n,'base64url').toString('base64')+'</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>';
const password='synthetic security test password',registration={username:'security_a',displayName:'Security A',password,publicKey,ageBand:'18+',acceptTerms:true,acknowledgePrivacy:true,policyVersion:'2026-10-19.1'};let app,origin,dir;
beforeEach(async()=>{dir=mkdtempSync(join(tmpdir(),'illusion-security-'));app=createMessenger({dataDir:dir});await new Promise(r=>app.server.listen(0,'127.0.0.1',r));origin='http://127.0.0.1:'+app.server.address().port;});
afterEach(async()=>{await app.close();assert.ok(dir.startsWith(join(tmpdir(),'illusion-security-')));rmSync(dir,{recursive:true,force:true});});
async function api(path,body,user){const r=await fetch(origin+path,{method:body?'POST':'GET',headers:{'Content-Type':'application/json',...(user?{Authorization:'Bearer '+user.token}:{})},body:body?JSON.stringify(body):undefined});return {status:r.status,body:await r.json()};}
test('scrypt accepts legacy credentials and upgrades them on successful login without changing identity',async()=>{
  const account=(await api('/v1/register',registration)).body,user=app.db.prepare('SELECT * FROM users WHERE id=?').get(account.user.id);assert.ok(user.passwordHash.startsWith('scrypt$32768$8$3$'));
  const old=scryptSync(password,user.salt,64).toString('hex');app.db.prepare('UPDATE users SET passwordHash=? WHERE id=?').run(old,user.id);
  const login=await api('/v1/login',{username:user.username,password});assert.equal(login.status,200);assert.equal(login.body.user.publicKey,publicKey);assert.ok(app.db.prepare('SELECT passwordHash FROM users WHERE id=?').get(user.id).passwordHash.startsWith('scrypt$32768$8$3$'));
  assert.equal(await passwordMatches('wrong password',user.salt,old),false);assert.equal(await passwordMatches(password,user.salt,'scrypt$999999999$8$3$bad'),false);
});
test('concurrent password work is bounded and successful operations release admission',async()=>{
  await api('/v1/register',registration);const results=await Promise.all([api('/v1/login',{username:registration.username,password}),api('/v1/login',{username:registration.username,password})]);assert.deepEqual(results.map(r=>r.status).sort(),[200,503]);assert.equal((await api('/v1/login',{username:registration.username,password})).status,200);
});
test('login sessions have a fixed active cap and evicted tokens cannot authenticate',async()=>{
  const user=(await api('/v1/register',registration)).body;for(let i=0;i<11;i++)assert.equal((await api('/v1/login',{username:registration.username,password})).status,200);assert.equal(app.db.prepare('SELECT COUNT(*) AS n FROM sessions WHERE userId=?').get(user.user.id).n,10);assert.equal((await api('/v1/me',null,user)).status,401);
});
test('cross-site simple JSON submissions are rejected before account creation',async()=>{
  const r=await fetch(origin+'/v1/register',{method:'POST',headers:{'Content-Type':'text/plain'},body:JSON.stringify(registration)});assert.equal(r.status,415);assert.equal(app.db.prepare('SELECT COUNT(*) AS n FROM users').get().n,0);
});
test('public key XML cannot carry entities, extra elements or noncanonical wrappers',async()=>{
  for(const key of ['<!DOCTYPE x [<!ENTITY x SYSTEM "file:///synthetic-never-read">]>'+publicKey,publicKey.replace('</RSAKeyValue>','<D>AAAA</D></RSAKeyValue>'),'prefix'+publicKey])assert.equal((await api('/v1/register',{...registration,publicKey:key})).status,400);
});
test('durable account limits survive reinitialization, hide raw identifiers and expire',()=>{
  const db=new DatabaseSync(':memory:');try{const limiter=durableLimiter(db);assert.equal(limiter.hit('profile:synthetic-user',2,1000,100),true);assert.equal(limiter.hit('profile:synthetic-user',2,1000,100),true);const fresh=durableLimiter(db);assert.equal(fresh.hit('profile:synthetic-user',2,1000,101),false);assert.match(db.prepare('SELECT key FROM request_limits').get().key,/^[a-f0-9]{64}$/);assert.equal(fresh.hit('profile:synthetic-user',2,1000,1101),true);fresh.sweep(2102);assert.equal(db.prepare('SELECT COUNT(*) AS n FROM request_limits').get().n,0);}finally{db.close();}
});
function png(w,h){const b=Buffer.alloc(33);Buffer.from('89504e470d0a1a0a','hex').copy(b);b.writeUInt32BE(13,8);b.write('IHDR',12);b.writeUInt32BE(w,16);b.writeUInt32BE(h,20);return b;}
test('images with tiny compressed headers but extreme pixel dimensions are rejected before decoding',async()=>{
  assert.deepEqual(imageDimensions(png(1,8192)),{width:1,height:8192});for(const [w,h]of [[0,10],[1,1000000],[8192,8192],[4294967295,1]])assert.throws(()=>imageDimensions(png(w,h)),/pixels/);
  const user=(await api('/v1/register',registration)).body,r=await fetch(origin+'/v1/profile',{method:'PUT',headers:{Authorization:'Bearer '+user.token,'Content-Type':'application/json'},body:JSON.stringify({displayName:'Security',profile:{avatar:'data:image/png;base64,'+png(1,1000000).toString('base64')}})});assert.equal(r.status,400);
});
test('decrypted attachments are checked before client decoders see them',()=>{
  assert.throws(()=>validateViewerMedia('photo',png(1,1000000)));
  assert.throws(()=>validateViewerMedia('photo',Buffer.from('not an image')));
  assert.ok(validateViewerMedia('photo',png(1,1)));
  assert.throws(()=>validateViewerMedia('video',Buffer.alloc(16)));
  const mp4=Buffer.alloc(12);mp4.write('ftyp',4);assert.ok(validateViewerMedia('video',mp4));
  assert.throws(()=>validateViewerMedia('audio',Buffer.from('not audio')));
  const wav=Buffer.alloc(12);wav.write('RIFF',0);wav.write('WAVE',8);assert.ok(validateViewerMedia('audio',wav));
});
test('decrypted content cannot bypass text, quote or media bounds using valid JSON',()=>{
  for(const c of [{text:'x'.repeat(2001)},{text:{script:'bad'}},{text:'ok',reply:{name:'a',text:'x'.repeat(321)}},{text:'ok',media:{kind:'html',mime:'text/html',data:'AAAA'}}])assert.throws(()=>messageContent(JSON.stringify({format:'illusion.message.v2',...c})));
  assert.throws(()=>messageContent('x'.repeat(12001)));assert.equal(messageContent(JSON.stringify({format:'illusion.message.v2',text:'<img src=x onerror=alert(1)>'})).text,'<img src=x onerror=alert(1)>');
});
test('history/sidebar/expiry budgets apply before ciphertext materialization',async()=>{
  const account=(await api('/v1/register',registration)).body,encoded=JSON.stringify({v:2,ciphertext:'A'.repeat(1400000)}),hash=createHash('sha256').update(encoded).digest('hex'),insert=app.db.prepare('INSERT INTO messages(senderId,recipientId,clientId,envelope,createdAt,envelopeHash,envelopeBytes) VALUES(?,?,?,?,?,?,?)');for(let i=0;i<12;i++)insert.run(account.user.id,account.user.id,randomUUID(),encoded,Date.now(),hash,encoded.length);
  let loaded=0;const prepare=app.db.prepare.bind(app.db);app.db.prepare=sql=>{const statement=prepare(sql);return {run:statement.run.bind(statement),all(...args){const rows=statement.all(...args);for(const row of rows)loaded+=row.envelope?.length||0;return rows;},get(...args){const row=statement.get(...args);loaded+=row?.envelope?.length||0;return row;}};};
  let r=await api('/v1/messages?peer='+account.user.id,null,account);assert.equal(r.status,200);assert.ok(loaded<8000000);assert.ok(r.body.hasMore);assert.ok(r.body.messages.length<12);
  loaded=0;r=await api('/v1/chats',null,account);assert.equal(r.status,200);assert.equal(loaded,0);assert.equal(r.body.chats[0].lastMessage.envelope,null);
  app.db.prepare('UPDATE messages SET expiresAt=?').run(Date.now()-1);loaded=0;r=await api('/v1/messages?peer='+account.user.id,null,account);assert.equal(r.status,200);assert.equal(loaded,0);assert.equal(app.db.prepare("SELECT COUNT(*) AS n FROM messages WHERE removed='expired'").get().n,12);
});
test('storage quota includes profile/report/metadata growth and still permits freeing space',async()=>{
  const user=(await api('/v1/register',registration)).body,budget=storageBudget(app.db),before=budget.used();app.db.prepare('INSERT INTO profiles VALUES(?,?)').run(user.user.id,JSON.stringify({bio:'x'.repeat(300)}));assert.ok(budget.used()>before+300);app.db.prepare('DELETE FROM profiles WHERE userId=?').run(user.user.id);assert.equal(budget.used(),before);
  app.db.prepare("UPDATE storage_usage SET bytes=? WHERE name='messages'").run(storageLimit);const r=await fetch(origin+'/v1/profile',{method:'PUT',headers:{'Content-Type':'application/json',Authorization:'Bearer '+user.token},body:JSON.stringify({displayName:'Security',profile:{}})});assert.equal(r.status,507);assert.equal((await api('/v1/me',null,user)).status,200);assert.equal((await api('/v1/logout',{},user)).status,200);
});
test('one slow upload cannot queue more bodies for its account and admission releases after disconnect',async()=>{
  const a=(await api('/v1/register',registration)).body,b=(await api('/v1/register',{...registration,username:'security_b'})).body;
  const slow=request(origin+'/v1/messages',{method:'POST',headers:{'Content-Type':'application/json',Authorization:'Bearer '+a.token}},res=>res.resume());slow.on('error',()=>{});slow.write('{');await new Promise(r=>setTimeout(r,60));
  const profile=async user=>(await fetch(origin+'/v1/profile',{method:'PUT',headers:{'Content-Type':'application/json',Authorization:'Bearer '+user.token},body:JSON.stringify({displayName:'Synthetic',profile:{}})})).status;
  try{assert.equal(await profile(a),503);assert.equal(await profile(b),200);}finally{slow.destroy();}
  await new Promise(r=>setTimeout(r,60));assert.equal(await profile(a),200);
});
