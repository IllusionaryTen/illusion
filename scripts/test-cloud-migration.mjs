import assert from 'node:assert/strict';
import {mkdtempSync,rmSync,writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {createHash,randomUUID} from 'node:crypto';
import {createMessenger} from '../server/index.mjs';
import {migrationTables} from '../cloudflare/migration.mjs';
import {newIdentity,encrypt,decrypt} from '../web/crypto.mjs';
import {localWorker,testAdmin,openTestRegistration} from './cloudflare-test-runtime.mjs';
const dir=mkdtempSync(join(tmpdir(),'illusion-migration-')),app=createMessenger({dataDir:join(dir,'source')}),mf=localWorker(join(dir,'target'));let checks=0;
function check(c,m){assert.ok(c,m);checks++;console.log('PASS: '+m);}
const password='migration preserves the account password';
function snapshot(){const tables=Object.fromEntries(migrationTables.map(t=>[t,app.db.prepare('SELECT * FROM '+t).all()]));return {format:'illusion.cloud-migration.v1',tables,sourceHash:createHash('sha256').update(JSON.stringify(tables)).digest('hex')};}
async function api(path,body,token){const r=await mf.dispatchFetch('http://localhost'+path,{method:body?'POST':'GET',headers:{'Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},body:body?JSON.stringify(body):undefined});return {status:r.status,body:await r.json()};}
try{
  await new Promise(r=>app.server.listen(0,'127.0.0.1',r));const origin='http://127.0.0.1:'+app.server.address().port,key=await newIdentity();
  const r=await fetch(origin+'/v1/register',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username:'migration_user',displayName:'Migration',password,publicKey:key.publicKey,ageBand:'18+',acceptTerms:true,acknowledgePrivacy:true,policyVersion:'2026-10-19.1'})}),account=await r.json();assert.equal(r.status,200);
  const text=JSON.stringify({format:'illusion.message.v2',text:'preserved history',media:{kind:'video',mime:'video/mp4',data:'x'.repeat(160000)}}),clientId=randomUUID(),envelope=await encrypt(text,account.user,account.user,clientId,key,{viewOnce:false,ttlSeconds:0});
  const sent=await fetch(origin+'/v1/messages',{method:'POST',headers:{'Content-Type':'application/json',Authorization:'Bearer '+account.token},body:JSON.stringify({recipientId:account.user.id,clientId,envelope})});assert.equal(sent.status,200);
  const original=snapshot(),bad=structuredClone(original);bad.tables.messages[0].envelope=bad.tables.messages[0].envelope.replace('illusion','altered');bad.tables.messages[0].clientId=randomUUID();bad.sourceHash=createHash('sha256').update(JSON.stringify(bad.tables)).digest('hex');
  check((await api('/admin/import',bad,testAdmin)).status===400,'Migration rejects an invalid signed envelope');
  check((await api('/admin/status',null,testAdmin)).body.counts.users===0,'Failed migration rolls back all account rows');
  const imported=await api('/admin/import',original,testAdmin);check(imported.status===200&&imported.body.counts.users===1&&imported.body.counts.messages===1,'Existing accounts and encrypted history import together');
  check((await api('/admin/import',original,testAdmin)).body.alreadyImported===true,'An identical migration retry is idempotent');
  check((await api('/v1/me',null,account.token)).status===401,'Local reusable session tokens are excluded from migration');
  const login=await api('/v1/login',{username:'migration_user',password});check(login.status===200&&login.body.user.id===account.user.id,'Existing password, account ID and public key survive migration');
  check(login.body.user.publicKey===key.publicKey,'Imported account keeps the exact encryption identity');
  const history=(await api('/v1/messages?peer='+account.user.id,null,login.body.token)).body.messages;assert.deepEqual(await decrypt(history[0],account.user,key,account.user),{...JSON.parse(text),reply:null});check(true,'Migrated chunked history decrypts with the original device key');
  check(history[0].id===app.db.prepare('SELECT id FROM messages').get().id,'Migration preserves message identifiers');
  const storage=await mf.unsafeGetDurableObjectStorage('illusion-test','IllusionRelay',{name:'illusion-main-v1'});check((await storage.exec('SELECT COUNT(*) AS n FROM media_chunks'))[0].n>1,'Imported large envelopes use bounded ciphertext chunks');
  const different=structuredClone(original);different.tables.reads.push({userId:account.user.id,peerId:account.user.id,lastId:1});different.sourceHash=createHash('sha256').update(JSON.stringify(different.tables)).digest('hex');check((await api('/admin/import',different,testAdmin)).status===400,'A different snapshot cannot overwrite a nonempty cloud database');
  check((await api('/admin/status',null,testAdmin)).body.registrationOpen===false,'Migration does not accidentally open public registration');
  await openTestRegistration(mf);check((await api('/admin/status',null,testAdmin)).body.registrationOpen===true,'Only the authenticated setup action opens registration');
  check(app.db.prepare('SELECT COUNT(*) AS n FROM users').get().n===1&&app.db.prepare('SELECT envelope FROM messages').get().envelope===original.tables.messages[0].envelope,'The source account and ciphertext remain unchanged');
  writeFileSync('artifacts/cloud-migration-qa.txt',checks+' isolated local-to-Cloudflare migration checks passed. No production data was changed.\n');
}finally{await mf.dispose();await app.close();assert.ok(dir.startsWith(join(tmpdir(),'illusion-migration-')));rmSync(dir,{recursive:true,force:true});}
