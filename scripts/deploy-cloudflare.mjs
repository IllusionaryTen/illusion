import {spawn} from 'node:child_process';
import {readFileSync,writeFileSync,existsSync} from 'node:fs';
import {resolve} from 'node:path';
import {parseEnv} from 'node:util';
import {createHash} from 'node:crypto';
import {ensureSecrets,readState,saveState,cloudAdmin} from './cloud-state.mjs';
import {loadRelease} from '../bot/index.mjs';
const root=resolve('.'),wrangler=resolve('node_modules/wrangler/bin/wrangler.js');
const env={...process.env,CLOUDFLARE_LOAD_DEV_VARS_FROM_DOT_ENV:'false',WRANGLER_SEND_METRICS:'false',WRANGLER_LOG_PATH:resolve('build/wrangler-deploy.log')};
for(const name of ['TELEGRAM_BOT_TOKEN','ADMIN_TOKEN','TELEGRAM_WEBHOOK_SECRET','REGISTRATION_CODE'])delete env[name];
function run(command,args,{input='',quiet=false,extraEnv={}}={}){return new Promise((yes,no)=>{let output='';const childEnv={...env,...extraEnv};if(command.toLowerCase()==='powershell.exe')for(const key of Object.keys(childEnv))if(key.toLowerCase()==='psmodulepath')delete childEnv[key];const child=spawn(command,args,{cwd:root,env:childEnv,windowsHide:true,stdio:['pipe','pipe','pipe']});for(const stream of [child.stdout,child.stderr])stream.on('data',chunk=>{output+=chunk;if(!quiet)process.stdout.write(chunk);});child.on('error',()=>no(Error('Could not start '+command)));child.on('exit',code=>code===0?yes(output):no(Error('Command failed ('+code+'): '+command+(quiet?'':'. See the output above.'))));child.stdin.end(input);});}
const wr=(args,options)=>run(process.execPath,[wrangler,...args,'--config','cloudflare/wrangler.jsonc'],options);
const state=readState(),secrets=ensureSecrets();
const identity=JSON.parse((await wr(['whoami','--json'],{quiet:true})).trim());
if(!identity.loggedIn)throw Error('Sign in using Wrangler login before deploying.');
const accountId=state.accountId||process.env.CLOUDFLARE_ACCOUNT_ID||(identity.accounts?.length===1?identity.accounts[0].id:null);if(!accountId)throw Error('Choose the intended Cloudflare account before deploying.');env.CLOUDFLARE_ACCOUNT_ID=accountId;state.accountId=accountId;
async function deploy(botUsername){
  await run(process.execPath,['scripts/prepare-cloudflare.mjs']);
  const output=await wr(['deploy',...(botUsername?['--var','BOT_USERNAME:'+botUsername]:[])]),origins=[...output.matchAll(/https:\/\/illusion-messenger\.[a-z0-9-]+\.workers\.dev\b/g)];
  const origin=origins.at(-1)?.[0];if(!origin)throw Error('Deployment did not return the Illusion workers.dev address. Finish Workers onboarding in Cloudflare and rerun.');
  if(state.origin&&state.origin!==origin)throw Error('Cloudflare returned a different origin; stopped before migrating accounts.');state.origin=origin;saveState(state);
}
if(!state.origin){await deploy();state.stage='bootstrap';saveState(state);}
await wr(['secret','put','ADMIN_TOKEN'],{input:secrets.admin+'\n'});
if(process.argv.includes('--bootstrap')){console.log('Bootstrap ready at '+state.origin+'. Registration remains closed.');process.exit(0);}
if(!state.migrated){
  await cloudAdmin('close-registration',{});
  if(existsSync('data/illusion.sqlite')){
    await run(process.execPath,['scripts/export-cloud-snapshot.mjs']);
    const snapshot=JSON.parse(readFileSync('build/cloud-migration.json','utf8')),result=await cloudAdmin('import',snapshot);
    const status=await cloudAdmin('status');for(const table of ['users','messages','profiles','reports'])if(status.counts[table]!==snapshot.tables[table].length)throw Error('Migration count mismatch; registration remains closed.');
    state.migrationHash=snapshot.sourceHash;console.log('Migration verified: '+JSON.stringify(status.counts));
  }else console.log('No local database found; hosting starts with an empty database.');
  state.migrated=true;saveState(state);
}
const settings=existsSync('.env')?parseEnv(readFileSync('.env','utf8')):{},botToken=settings.TELEGRAM_BOT_TOKEN;
async function telegram(method,body){if(!botToken)throw Error('Telegram bot token is not configured locally.');let response;try{response=await fetch('https://api.telegram.org/bot'+botToken+'/'+method,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(30000)});}catch{throw Error('Telegram could not be reached; the bot token was not logged.');}const result=await response.json();if(!response.ok||!result.ok)throw Error('Telegram '+method+' failed ('+response.status+').');return result.result;}
const bot=botToken?await telegram('getMe',{}):null;if(bot){state.botUsername=bot.username;saveState(state);await wr(['secret','put','TELEGRAM_BOT_TOKEN'],{input:botToken+'\n'});await wr(['secret','put','TELEGRAM_WEBHOOK_SECRET'],{input:secrets.webhook+'\n'});}
await run('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File','scripts/build.ps1','-OutputName','Illusion-0.6.1.exe','-RelayUrl',state.origin,'-NetworkName','Illusion']);
await run(process.execPath,['scripts/write-legal.mjs','build/legal.json','--documents']);
await deploy(bot?.username);
const release=loadRelease('dist/release.json');
for(const [path,expected]of [['/health',null],['/app/',null],['/download/'+release.file,release.sha256]]){const response=await fetch(state.origin+path,{redirect:'error',signal:AbortSignal.timeout(30000)});if(!response.ok)throw Error('Hosted verification failed for '+path);if(expected&&createHash('sha256').update(Buffer.from(await response.arrayBuffer())).digest('hex')!==expected)throw Error('Hosted download checksum mismatch.');}
let dotenv=existsSync('.env')?readFileSync('.env','utf8'):'';for(const [name,value]of Object.entries({DEFAULT_RELAY_URL:state.origin,DEFAULT_NETWORK_NAME:'Illusion'})){const line=name+'='+value,expression=new RegExp('^'+name+'=.*$','m');dotenv=expression.test(dotenv)?dotenv.replace(expression,line):dotenv.replace(/\s*$/,'')+'\n'+line+'\n';}writeFileSync('.env',dotenv);
await run('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File','scripts/migrate-cloud-device.ps1']);
await cloudAdmin('open-registration',{});
if(bot){
  // Stop only this project's old polling process, after the hosted app and
  // downloads have passed their public health/checksum checks.
  await run('powershell.exe',['-NoProfile','-NonInteractive','-File','scripts/stop-legacy-bot.ps1']);
  await telegram('setWebhook',{url:state.origin+'/telegram/webhook',secret_token:secrets.webhook,allowed_updates:['message'],drop_pending_updates:false});
  await telegram('setChatMenuButton',{menu_button:{type:'web_app',text:'Open Illusion',web_app:{url:state.origin+'/app/'}}});
  await telegram('setMyCommands',{commands:[{command:'app',description:'Open Illusion inside Telegram'},{command:'apk',description:'Download Android APK'},{command:'privacy',description:'Privacy and service policies'},{command:'download',description:'Windows desktop version'},{command:'connect',description:'Desktop connection invitation'}]});
  const hook=await telegram('getWebhookInfo',{});if(hook.url!==state.origin+'/telegram/webhook')throw Error('Telegram webhook did not match the hosted address.');state.botWebhook=true;
}
state.stage='ready';state.version=release.version;state.sha256=release.sha256;state.verifiedAt=new Date().toISOString();saveState(state);
console.log('Illusion is ready: '+state.origin+'/app/'+(bot?' · Telegram @'+bot.username:''));
