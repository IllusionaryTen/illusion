import {localWorker,testAdmin,openTestRegistration} from './cloudflare-test-runtime.mjs';
import {mkdtempSync,rmSync,writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import assert from 'node:assert/strict';
import {newIdentity} from '../web/crypto.mjs';
const dir=mkdtempSync(join(tmpdir(),'illusion-security-cloud-')),worker=localWorker(dir),sockets=[];let checks=0;
function check(c,m){assert.ok(c,m);checks++;console.log('PASS: '+m);}
async function api(path,body,token,method=body?'POST':'GET'){const r=await worker.dispatchFetch('http://localhost'+path,{method,headers:{'Content-Type':'application/json',Connection:'close',...(token?{Authorization:'Bearer '+token}:{})},body:body?JSON.stringify(body):undefined});return {status:r.status,body:await r.json()};}
try{
  check((await api('/admin/status')).status===401,'Untrusted administration requests fail at the outer Worker');
  const hook=await worker.dispatchFetch('http://localhost/telegram/webhook',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'});check(hook.status===401,'Forged Telegram updates fail at the outer Worker');
  const blockedOpen=await api('/admin/open-registration',{});const initial=await api('/admin/status',null,testAdmin);check(blockedOpen.status===401&&!initial.body.registrationOpen&&initial.body.counts.users===0,'Rejected privileged requests cannot open registration or create account records');
  const storage=await worker.unsafeGetDurableObjectStorage('illusion-test','IllusionRelay',{name:'illusion-main-v1'});
  const cross=await worker.dispatchFetch('http://localhost/v1/register',{method:'POST',headers:{Origin:'https://attacker.example','Content-Type':'application/json'},body:'{}'});check(cross.status===403,'Cross-origin mutation requests are rejected');
  await openTestRegistration(worker);const key=await newIdentity(),account=await api('/v1/register',{username:'cloud_security',displayName:'Synthetic security fixture',password:'a synthetic security password',publicKey:key.publicKey,ageBand:'18+',acceptTerms:true,acknowledgePrivacy:true,policyVersion:'2026-10-19.1'});check(account.status===200,'Hardened hosted registration works with the stronger password hash');
  for(let i=0;i<2;i++){const r=await worker.dispatchFetch('http://localhost/v1/live',{headers:{Upgrade:'websocket'}});assert.equal(r.status,101);r.webSocket.accept();sockets.push(r.webSocket);}
  const third=await worker.dispatchFetch('http://localhost/v1/live',{headers:{Upgrade:'websocket'}});check(third.status===429,'One IP cannot occupy more than two unauthenticated live slots');for(const socket of sockets)socket.close(1000,'QA complete');sockets.length=0;
  for(let i=0;i<9;i++)assert.equal((await api('/v1/profile',{displayName:'Synthetic',profile:{}},account.body.token,'PUT')).status,200);
  await worker.unsafeEvictDurableObject('illusion-test','IllusionRelay',{name:'illusion-main-v1',webSockets:'hibernate'});assert.equal((await api('/v1/profile',{displayName:'Synthetic',profile:{}},account.body.token,'PUT')).status,200);check((await api('/v1/profile',{displayName:'Synthetic',profile:{}},account.body.token,'PUT')).status===429,'Account write limits remain enforced across real object hibernation');
  const stats=await api('/admin/status',null,testAdmin);check(stats.status===200&&stats.body.counts.users===1,'Only the separate administration secret accesses operational status');check((await api('/admin/status',null,account.body.token)).status===401,'An ordinary account token cannot become a host administration token');
  const sizes=await storage.exec('SELECT name,bytes FROM storage_usage');check(sizes.some(r=>r.name==='users'&&r.bytes>0)&&sizes.every(r=>r.bytes>=0),'Hosted storage accounting covers account and metadata rows');
  writeFileSync('artifacts/security-cloud-qa.txt',checks+' isolated Cloudflare security checks passed. No production users, messages or infrastructure were attacked.\n');
}finally{for(const socket of sockets)socket.close(1000,'QA complete');await worker.dispose();assert.ok(dir.startsWith(join(tmpdir(),'illusion-security-cloud-')));rmSync(dir,{recursive:true,force:true});}
