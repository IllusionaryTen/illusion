import assert from 'node:assert/strict';
import {randomBytes,randomUUID} from 'node:crypto';
import {writeFileSync} from 'node:fs';
import {readState,cloudAdmin} from './cloud-state.mjs';
import {newIdentity,encrypt,decrypt} from '../web/crypto.mjs';
const origin=readState().origin,nonce=randomBytes(5).toString('hex'),password=randomBytes(24).toString('base64url'),accounts=[],sockets=[];let checks=0;
function check(c,m){assert.ok(c,m);checks++;console.log('PASS: '+m);}
async function api(path,user,body){const r=await fetch(origin+path,{method:body?'POST':'GET',headers:{'Content-Type':'application/json',...(user?{Authorization:'Bearer '+user.token}:{})},body:body?JSON.stringify(body):undefined,redirect:'error',signal:AbortSignal.timeout(30000)});const result=await r.json();if(!r.ok)throw Error(path+' failed ('+r.status+'): '+(result.error||'Request rejected'));return result;}
async function register(suffix){const key=await newIdentity(),username='qa_'+nonce+'_'+suffix,result=await api('/v1/register',null,{username,displayName:'Temporary integration test',password,publicKey:key.publicKey,ageBand:'18+',acceptTerms:true,acknowledgePrivacy:true,policyVersion:'2026-10-19.1'}),account={...result,key,username};accounts.push(account);return account;}
async function live(user){const socket=new WebSocket(origin.replace('https:','wss:')+'/v1/live'),queue=[];sockets.push(socket);socket.addEventListener('message',e=>{try{queue.push(JSON.parse(e.data));}catch{}});await new Promise((yes,no)=>{const timer=setTimeout(()=>no(Error('Public live socket timed out')),15000);socket.addEventListener('open',()=>{clearTimeout(timer);yes();},{once:true});socket.addEventListener('error',()=>{clearTimeout(timer);no(Error('Public live socket could not connect'));},{once:true});});socket.send(JSON.stringify({type:'authenticate',token:user.token}));async function next(type){const until=Date.now()+15000;while(Date.now()<until){const i=queue.findIndex(x=>x.type===type);if(i>=0)return queue.splice(i,1)[0];await new Promise(r=>setTimeout(r,25));}throw Error('No public live '+type+' event');}await next('ready');return {socket,next};}
try{
  const alice=await register('a'),bob=await register('b');check(true,'Two temporary accounts register through the public HTTPS service');
  const login=await api('/v1/login',null,{username:alice.username,password});check(login.user.id===alice.user.id,'Public production scrypt login accepts the registered password');
  const a=await live(alice),b=await live(bob);check(true,'Two public WSS connections authenticate with independent sessions');
  const clientId=randomUUID(),text='Public encrypted QA '+nonce,envelope=await encrypt(text,alice.user,bob.user,clientId,alice.key,{viewOnce:false,ttlSeconds:0});
  const sent=await api('/v1/messages',alice,{recipientId:bob.user.id,clientId,envelope});check(sent.message.envelope.ciphertext===envelope.ciphertext,'Public relay accepts and returns the signed encrypted envelope');
  await b.next('changed');check(true,'The recipient receives a live notification through the public Cloudflare edge');
  const history=await api('/v1/messages?peer='+alice.user.id,bob);check((await decrypt(history.messages.find(m=>m.id===sent.message.id),bob.user,bob.key,alice.user)).text===text,'The recipient decrypts and verifies the message fetched over public HTTPS');
  await a.next('changed');check(true,'The sender receives its independent live conversation update');
}finally{
  for(const socket of sockets)socket.close(1000,'QA complete');
  const failures=[];for(const account of accounts){try{const request=await api('/v1/deletion-request',account,{confirmUsername:account.username,password});await cloudAdmin('fulfill-deletion',{id:request.requestId,username:account.username});}catch(e){failures.push(account.username+': '+e.message);}}
  if(failures.length)throw Error('Temporary account cleanup needs attention: '+failures.join('; '));
  if(accounts.length)console.log('Removed all '+accounts.length+' temporary production accounts and their encrypted messages.');
}
writeFileSync('artifacts/cloud-public-message-qa.json',JSON.stringify({checkedAt:new Date().toISOString(),origin,checks,temporaryAccounts:accounts.length,temporaryAccountsDeleted:true,notes:'Actual public HTTPS registration/password login, two authenticated WSS clients, signed encrypted delivery and original-key decryption. Only synthetic QA content was sent; no real Telegram messages were sent.'},null,2)+'\n');
