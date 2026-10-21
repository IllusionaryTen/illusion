import { spawn } from 'node:child_process';
import { readFileSync, writeFileSync, renameSync, existsSync, mkdirSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createMessenger } from '../server/index.mjs';
import { cloudflareEdges } from './cloudflare-dns.mjs';
import { probePublicRelay } from './public-health.mjs';

const root=resolve(dirname(fileURLToPath(import.meta.url)),'..');
process.chdir(root);mkdirSync('build',{recursive:true});
const port=Number(process.env.PORT||3789), local='http://127.0.0.1:'+port;
const statePath=resolve('build/public-state.json');
let app, tunnel, stopping=false, state={pid:process.pid,status:'starting',url:''};
function save(){writeFileSync(statePath,JSON.stringify(state,null,2));}
async function stop(){if(stopping)return;stopping=true;state.status='stopped';save();if(tunnel)tunnel.kill();if(app)await app.close();}
process.once('SIGINT',()=>stop().then(()=>process.exit(0)));process.once('SIGTERM',()=>stop().then(()=>process.exit(0)));
try {
  if(existsSync(statePath)){let prior;try{prior=JSON.parse(readFileSync(statePath,'utf8'));}catch{}if(prior && prior.pid!==process.pid && ['starting','online'].includes(prior.status)){let alive=false;try{process.kill(prior.pid,0);alive=true;}catch{}if(alive){console.log('The public host is already running. Address: '+(prior.url||'still connecting'));process.exit(0);}}}
  const binary=resolve('tools/cloudflared.exe');
  const expected='4d2490e74082c5d48dbfedaf92576fa5bd1702379ab88eb7faa0cfbe8dc891b6';
  if(!existsSync(binary) || createHash('sha256').update(readFileSync(binary)).digest('hex')!==expected)throw new Error('Run Setup-Public.cmd to install the verified tunnel client.');
  let existing;
  try{existing=await fetch(local+'/health',{signal:AbortSignal.timeout(2000)}).then(r=>r.json());}catch{}
  if(existing){if(existing.app!=='Illusion' || existing.version!=='0.6.1')throw new Error('Close the old Start-Server window, then run Start-Public.cmd again.');console.log('Using the existing Illusion 0.6 relay.');}
  else {app=createMessenger();await new Promise((yes,no)=>{app.server.once('error',no);app.server.listen(port,'127.0.0.1',yes);});console.log('Started Illusion 0.6 relay on loopback.');}
  const localHealth=await fetch(local+'/health',{signal:AbortSignal.timeout(3000)}).then(r=>r.json());
  if(!localHealth.instance)throw Error('Restart the local relay to load the updated public connection checks.');
  save();
  // Some VPN DNS clients suppress SRV records. Discover the same Cloudflare
  // servers via HTTPS DNS and keep all original tunnel TLS validation enabled.
  const edges=await cloudflareEdges();
  const protocol=process.env.PUBLIC_TUNNEL_PROTOCOL||'auto';if(!['auto','http2','quic'].includes(protocol))throw Error('PUBLIC_TUNNEL_PROTOCOL must be auto, http2 or quic.');
  const tunnelArgs=['tunnel','--no-autoupdate','--url',local,'--protocol',protocol,'--edge-ip-version','4'];
  for(const edge of edges)tunnelArgs.push('--edge',edge);
  tunnel=spawn(binary,tunnelArgs,{windowsHide:true,stdio:['ignore','pipe','pipe'],env:{...process.env,TELEGRAM_BOT_TOKEN:'',REGISTRATION_CODE:''}});
  const url=await new Promise((yes,no)=>{
    const timeout=setTimeout(()=>no(new Error('Cloudflare did not provide a public address within 60 seconds.')),60000);
    let log='';const output=chunk=>{const s=chunk.toString();process.stdout.write(s);log=(log+s).slice(-12000);const match=log.match(/https:\/\/[a-z0-9-]+\.trycloudflare\.com/);if(match){clearTimeout(timeout);yes(match[0]);}};
    tunnel.stdout.on('data',output);tunnel.stderr.on('data',output);tunnel.once('error',e=>{clearTimeout(timeout);no(e);});tunnel.once('exit',()=>{clearTimeout(timeout);no(new Error('Public tunnel exited before setup completed.'));});
  });
  state.candidate=url;save();
  console.log('\nChecking the public relay at '+url+'/health ...');
  let reachable=false, lastFailure='No health response';
  for(let n=0;n<30 && !reachable;n++){
    const check=await probePublicRelay(url,localHealth);reachable=check.ready;lastFailure=check.reason;
    if(!reachable && (n===0 || (n+1)%5===0))console.log('Public check '+(n+1)+'/30: '+lastFailure);
    if(!reachable)await new Promise(r=>setTimeout(r,1500));
  }
  if(!reachable)throw new Error('Public relay check failed: '+lastFailure+'. No connection settings have been changed.');
  await new Promise((yes,no)=>{const build=spawn('powershell.exe',['-NoProfile','-ExecutionPolicy','Bypass','-File','scripts/build.ps1','-OutputName','Illusion-0.6.1.exe','-RelayUrl',url],{windowsHide:true,stdio:'inherit'});build.once('exit',code=>code===0?yes():no(new Error('Public connection works, but rebuilding the download failed. Close Illusion-0.6.1.exe and run Start-Public.cmd again.')));build.once('error',no);});
  const envPath=resolve('.env');let contents=existsSync(envPath)?readFileSync(envPath,'utf8'):'';
  contents=contents.replace(/^DEFAULT_RELAY_URL=.*\r?\n?/gm,'').trimEnd()+'\nDEFAULT_RELAY_URL='+url+'\n';
  writeFileSync(envPath+'.tmp',contents);renameSync(envPath+'.tmp',envPath);
  state={...state,status:'online',url,local};save();
  console.log('\nILLUSION IS PUBLIC: '+url+'\nUpdated download: dist/Illusion-0.6.1.exe\nThe Telegram bot now distributes this build. Keep this window open and the laptop awake.\nRestarting this temporary tunnel changes the public address. Friends then need the updated /connect invitation.');
  tunnel.once('exit',()=>{if(!stopping){state.status='tunnel-disconnected';save();console.error('Public connection stopped. Restart Start-Public.cmd.');stop().then(()=>process.exit(1));}});
}catch(error){console.error(error.message);state.status='failed';state.error=error.message;save();await stop();process.exitCode=1;}
