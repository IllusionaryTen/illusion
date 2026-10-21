import {readState} from './cloud-state.mjs';
const state=readState();
try{
  if(state.stage!=='ready')throw Error('Cloud setup is not finished. Run Deploy-Cloudflare.cmd to resume it.');
  if(!/^https:\/\/[a-z0-9.-]+\.workers\.dev$/.test(state.origin))throw Error('The saved cloud address is invalid.');
  const response=await fetch(state.origin+'/health',{redirect:'error',signal:AbortSignal.timeout(20000)}),health=await response.json();
  if(!response.ok||health.app!=='Illusion')throw Error('The hosted relay could not be verified.');
  console.log('Illusion '+health.version+' is hosted on Cloudflare. No server window needs to stay open.');
  console.log('Mini App: '+state.origin+'/app/');
  console.log('Windows: '+state.origin+'/download/Illusion-0.6.1.exe');
  if(state.botWebhook)console.log('Telegram @'+state.botUsername+' runs through its cloud webhook. Use Open Illusion or /download.');
}catch(e){console.error(e.message);process.exitCode=1;}
