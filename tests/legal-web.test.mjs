import { test,before,after } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync,rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { generateKeyPairSync } from 'node:crypto';
import { createMessenger } from '../server/index.mjs';
import { fulfillDeletion,legalVersion,fonts } from '../server/legal.mjs';
const dir=mkdtempSync(join(tmpdir(),'illusion-legal-'));let app,base,account;
const key=generateKeyPairSync('rsa',{modulusLength:3072}).publicKey.export({format:'jwk'});
const registration={username:'legal_user',displayName:'Legal test',password:'a strong legal testing password',publicKey:'<RSAKeyValue><Modulus>'+Buffer.from(key.n,'base64url').toString('base64')+'</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>',ageBand:'18+',acceptTerms:true,acknowledgePrivacy:true,policyVersion:legalVersion};
async function api(path,method='GET',body,token){const r=await fetch(base+path,{method,headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},body:body&&JSON.stringify(body)});return {r,body:await r.json()};}
before(async()=>{app=createMessenger({dataDir:dir});await new Promise(r=>app.server.listen(0,'127.0.0.1',r));base='http://127.0.0.1:'+app.server.address().port;});
after(async()=>{await app.close();assert.ok(dir.startsWith(join(tmpdir(),'illusion-legal-')));rmSync(dir,{recursive:true,force:true});});
test('Mini App and policies are public, same-origin and cookie/SDK-free',async()=>{
  const r=await fetch(base+'/app/');const html=await r.text();assert.equal(r.status,200);assert.equal(r.headers.get('set-cookie'),null);assert.match(r.headers.get('content-security-policy'),/script-src 'self'/);assert.match(html,/aria-live="polite"/);assert.ok(!html.includes('https://'));assert.match(html,/Continue with essentials/);
  const info=(await api('/v1/public')).body;assert.equal(info.minimumAge,18);assert.equal(info.optionalTracking,false);assert.equal(info.price,0);assert.ok(info.fonts.length>=25);assert.equal(info.operator.configured,false);
  for(const id of ['privacy','terms','refunds','cookies','licenses','accessibility']){const p=await fetch(base+'/policies/'+id);assert.equal(p.status,200);assert.match(await p.text(),/Illusion/);}
  assert.equal((await fetch(base+'/app/../../.env')).status,401);assert.equal((await fetch(base+'/v1/legal/constructor')).status,401);
});
test('registration rejects missing/prechecked-spoofed/stale consent and every underage band',async()=>{
  for(const change of [{acceptTerms:false},{acknowledgePrivacy:false},{policyVersion:'old'},{ageBand:'Under 13'},{ageBand:'13–15'},{ageBand:'16–17'},{acceptTerms:'true'}])assert.equal((await api('/v1/register','POST',{...registration,...change})).r.status,400);
  const result=await api('/v1/register','POST',registration);assert.equal(result.r.status,200,JSON.stringify(result.body));account=result.body;
  const consent=app.db.prepare('SELECT * FROM consents WHERE userId=?').get(account.user.id);assert.equal(consent.ageBand,'18+');assert.equal(consent.version,legalVersion);assert.equal(consent.birthDate,undefined);
});
test('expanded fonts are shared with profile validation',async()=>{for(const font of ['Bahnschrift','Garamond','Tahoma','Segoe Print']){assert.ok(fonts.includes(font));const r=await api('/v1/profile','PUT',{displayName:'Legal test',profile:{font}},account.token);assert.equal(r.r.status,200);}});
test('deletion requests require authentication, matching name and password, and are idempotent',async()=>{
  const b={confirmUsername:registration.username,password:registration.password};assert.equal((await api('/v1/deletion-request','POST',b)).r.status,401);assert.equal((await api('/v1/deletion-request','POST',{...b,password:'incorrect password'},account.token)).r.status,403);
  const one=await api('/v1/deletion-request','POST',b,account.token),two=await api('/v1/deletion-request','POST',b,account.token);assert.equal(one.r.status,200);assert.equal(one.body.requestId,two.body.requestId);assert.equal(one.body.status,'pending');assert.ok(app.db.prepare('SELECT id FROM users WHERE id=?').get(account.user.id));
  assert.throws(()=>fulfillDeletion(app.db,one.body.requestId,'wrong-user'),/match/);fulfillDeletion(app.db,one.body.requestId,registration.username);assert.equal(app.db.prepare('SELECT id FROM users WHERE id=?').get(account.user.id),undefined);assert.equal((await api('/v1/me','GET',null,account.token)).r.status,401);assert.equal(app.db.prepare('SELECT userId FROM profiles WHERE userId=?').get(account.user.id),undefined);assert.equal(app.db.prepare('SELECT status FROM deletion_requests WHERE id=?').get(one.body.requestId).status,'completed');
});
