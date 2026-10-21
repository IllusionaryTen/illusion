import {createRequire} from 'node:module';
import {mkdtempSync,rmSync,writeFileSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import assert from 'node:assert/strict';
import {localWorker,openTestRegistration} from './cloudflare-test-runtime.mjs';
const require=createRequire(import.meta.url),{chromium}=require(process.env.ILLUSION_PLAYWRIGHT||'C:/Users/Giga/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const dir=mkdtempSync(join(tmpdir(),'illusion-security-web-')),worker=localWorker(dir),results=[];let browser;
function check(ok,name){results.push({name,ok:!!ok});console.log((ok?'PASS: ':'FAIL: ')+name);}
async function register(page,name){if(await page.locator('#registration').isHidden())await page.getByRole('button',{name:'Create an account',exact:true}).click();await page.getByLabel('Username',{exact:true}).fill(name);await page.getByLabel('Password',{exact:true}).fill('a synthetic security test password');await page.getByLabel('Display name',{exact:true}).fill(name);await page.getByLabel('Your age range').selectOption('18+');await page.getByLabel('I agree to the Terms').check();await page.getByLabel('I have read the Privacy').check();await page.getByRole('button',{name:'Create account',exact:true}).click();await page.locator('#workspace').waitFor({state:'visible',timeout:25000});}
try{
  await openTestRegistration(worker);browser=await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true});const page=await browser.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));await page.goto((await worker.ready).origin+'/app/');await register(page,'security_alice');
  await page.getByRole('button',{name:'Saved messages',exact:true}).click();const secret='Synthetic Alice private message <img src=x onerror="globalThis.__illusionXss=1">';await page.getByLabel('Message',{exact:true}).fill(secret);await page.locator('#send-form').getByRole('button',{name:'Send',exact:true}).click();await page.locator('#messages').getByText(secret,{exact:true}).waitFor();
  check(await page.locator('#messages img').count()===0&&await page.evaluate(()=>!globalThis.__illusionXss),'Message markup remains inert text, with no inserted image or event execution');
  await page.getByLabel('Message',{exact:true}).fill('Synthetic private draft');
  await page.evaluate(()=>{const proto=Object.getPrototypeOf(crypto.subtle),original=proto.decrypt;proto.decrypt=function(a,...args){const p=original.call(this,a,...args);return a.name==='AES-CBC'&&globalThis.__holdDecrypt?p.then(async result=>{globalThis.__decryptHeld=true;await new Promise(r=>globalThis.__releaseDecrypt=r);return result;}):p;};globalThis.__holdDecrypt=true;});
  await page.getByRole('button',{name:'Saved messages',exact:true}).click();await page.waitForFunction(()=>globalThis.__decryptHeld);
  await page.getByRole('button',{name:'Appearance & privacy'}).click();await page.getByRole('button',{name:'Sign out',exact:true}).click();await page.locator('#auth').waitFor({state:'visible'});
  check(await page.locator('#messages').textContent()===''&&await page.locator('#chats').textContent()==='','Sign-out removes previous account messages and contacts from the DOM');
  check(await page.locator('#composer').inputValue()===''&&await page.locator('#chat-title').textContent()==='Make a connection.','Sign-out removes drafts and conversation identity');
  const storage=await worker.unsafeGetDurableObjectStorage('illusion-test','IllusionRelay',{name:'illusion-main-v1'});let sessions=1;for(let i=0;i<30&&sessions;i++){sessions=(await storage.exec("SELECT COUNT(*) AS n FROM sessions s JOIN users u ON u.id=s.userId WHERE u.username='security_alice'"))[0].n;if(sessions)await new Promise(r=>setTimeout(r,50));}
  check(sessions===0,'Web sign-out revokes the actual server session');
  await register(page,'security_bob');check(!(await page.locator('#workspace').textContent()).includes('Synthetic Alice private message'),'A second account cannot see the previous account’s rendered chat');
  await page.evaluate(()=>{globalThis.__holdDecrypt=false;globalThis.__releaseDecrypt();});await page.waitForTimeout(150);
  check(!(await page.locator('#workspace').textContent()).includes('Synthetic Alice private message'),'An old in-flight decryption cannot restore private content after account switching');
  check(errors.length===0,'No browser runtime errors during the sign-out/decryption race');
  const baseline=process.argv.includes('--baseline'),file=baseline?'artifacts/security-web-before.json':'artifacts/security-web-after.json';writeFileSync(file,JSON.stringify({checkedAt:new Date().toISOString(),baseline,results},null,2)+'\n');assert.ok(results.every(r=>r.ok),'Security browser regression failed');
}finally{if(browser)await browser.close();await worker.dispose();assert.ok(dir.startsWith(join(tmpdir(),'illusion-security-web-')));rmSync(dir,{recursive:true,force:true});}
