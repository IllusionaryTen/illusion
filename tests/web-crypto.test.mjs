import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createPublicKey,verify,privateDecrypt,createDecipheriv,createHmac } from 'node:crypto';
import { writeFileSync,mkdirSync,readFileSync } from 'node:fs';
import * as web from '../web/crypto.mjs';
import { signatureText } from '../server/index.mjs';
test('browser Web Crypto matches relay signatures and message format, rejects policy tampering',async()=>{
  const alice=await web.newIdentity(),bob=await web.newIdentity(),a={id:'alice',publicKey:alice.publicKey},b={id:'bob',publicKey:bob.publicKey};const clientId=crypto.randomUUID(),text='Web Crypto interop Привет ✨';
  const envelope=await web.encrypt(text,a,b,clientId,alice,{viewOnce:false,ttlSeconds:0});const key=createPublicKey({key:alice.exportJwk,format:'jwk'});assert.ok(verify('RSA-SHA256',Buffer.from(signatureText(a.id,b.id,clientId,envelope)),key,Buffer.from(envelope.signature,'base64')));
  const message={senderId:a.id,recipientId:b.id,clientId,envelope};assert.equal((await web.decrypt(message,b,bob,a)).text,text);
  envelope.policy.viewOnce=true;await assert.rejects(web.decrypt(message,b,bob,a),/signature/);envelope.policy.viewOnce=false;
  const backup=await web.backup(alice,'a strong interop backup password','web-fixture','http://interop.test');const restored=await web.restore(backup,'a strong interop backup password');assert.equal(restored.publicKey,alice.publicKey);await assert.rejects(web.restore(backup,'wrong backup password'),/password/);
  assert.equal(backup.version,2);const legacy=JSON.parse(readFileSync('tests/fixtures/legacy-key.json','utf8'));assert.equal((await web.restore(legacy.backup,legacy.password)).publicKey,legacy.publicKey);
  mkdirSync('build',{recursive:true});writeFileSync('build/webcrypto-interop.json',JSON.stringify({backup,password:'a strong interop backup password',publicKey:alice.publicKey}));
});
