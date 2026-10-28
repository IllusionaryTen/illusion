import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync,statSync} from 'node:fs';
import {join,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
import {publicWeb} from '../server/web.mjs';

const root=join(dirname(fileURLToPath(import.meta.url)),'..','web');
test('Android install guide and PWA resources are served with install scope and safe cache rules',()=>{
  for(const [path,contentType,pattern] of [
    ['/android/','text/html',/home screen/i],
    ['/app/manifest.webmanifest','application/manifest+json',/"display": "standalone"/],
    ['/app/sw.js','text/javascript',/illusion-app-shell-v4/],
    ['/app/mobile.css','text/css',/chat-row/],
    ['/app/sounds/illusionsfx.mp3','audio/mpeg',null],
    ['/app/request-signal.mjs','text/javascript',/AbortSignal\.any\/timeout/],
    ['/app/icons/icon-192.png','image/png',null],
    ['/app/icons/icon-512.png','image/png',null]
  ]){
    let status,headers,body;
    const handled=publicWeb({method:'GET',url:path},{writeHead(code,value){status=code;headers=value;},end(value){body=value;}});
    assert.equal(handled,true,path);assert.equal(status,200,path);assert.ok(headers['Content-Type'].startsWith(contentType),path);if(pattern)assert.match(String(body),pattern,path);
    if(path.includes('.png')){const file=join(root,...path.replace(/^\/(app\/)?/,'').split('/'));assert.ok(statSync(file).size>1000,path);assert.deepEqual(Buffer.from(body),readFileSync(file));}
  }
  const worker=readFileSync(join(root,'sw.js'),'utf8');
  assert.match(worker,/if \(request\.method !== 'GET'/);assert.match(worker,/if \(!SHELL\.includes\(path\)\) return/);assert.doesNotMatch(worker,/\/v1\//);
  assert.match(worker,/illusion-app-shell-v4/);assert.match(worker,/notificationclick/);assert.match(worker,/illusionsfx\.mp3/);
  const app=readFileSync(join(root,'app.mjs'),'utf8');assert.match(app,/serviceWorker\.register\('\/app\/sw\.js'/);assert.match(app,/requestSignal\(sessionController\.signal/);assert.doesNotMatch(app,/AbortSignal\.(any|timeout)\(/);assert.match(app,/status\(register\?'Preparing your account…':'Signing in…'\)/);assert.match(app,/password\.length<10\|\|password\.length>128/);assert.match(app,/chat-pins:/);assert.match(app,/notifyMessage/);assert.match(app,/media:pendingSend\.media/);
  const stylesheet=readFileSync(join(root,'style.css'),'utf8')+readFileSync(join(root,'mobile.css'),'utf8');assert.match(stylesheet,/\.chat-row/);assert.match(stylesheet,/#workspace\[data-view=list\]/);
  const index=readFileSync(join(root,'index.html'),'utf8');assert.match(index,/<form id="auth-form" novalidate>/);assert.match(index,/id="favorites-row"/);assert.match(index,/attachments are encrypted on your device/i);
  const crypto=readFileSync(join(root,'crypto.mjs'),'utf8');assert.match(crypto,/r\.onblocked=/);assert.match(crypto,/Device key storage timed out/);
});
