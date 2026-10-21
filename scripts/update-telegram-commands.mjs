import {readFileSync} from 'node:fs';
import {parseEnv} from 'node:util';
import {pathToFileURL} from 'node:url';
import {resolve} from 'node:path';
import {readState} from './cloud-state.mjs';

export async function updateTelegramCommands({token,origin,username,fetcher=fetch}){
  if(typeof token!=='string'||!/^[0-9]+:[\w-]+$/.test(token))throw Error('Telegram bot token is not configured.');
  const root=new URL(origin);
  if(root.protocol!=='https:'||root.pathname!=='/'||root.search||root.hash)throw Error('Configured public HTTPS origin is invalid.');
  async function api(method,body={}){
    const response=await fetcher('https://api.telegram.org/bot'+token+'/'+method,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body),signal:AbortSignal.timeout(15000)});
    let result;try{result=await response.json();}catch{throw Error('Telegram command setup failed.');}
    if(!response.ok||!result.ok)throw Error('Telegram command setup failed.');
    return result.result;
  }
  const bot=await api('getMe');
  if(username&&bot.username?.toLowerCase()!==username.toLowerCase())throw Error('The configured token belongs to a different Telegram bot.');
  const appUrl=root.origin+'/app/';
  await api('setChatMenuButton',{menu_button:{type:'web_app',text:'Open Illusion',web_app:{url:appUrl}}});
  const commands=[{command:'app',description:'Open Illusion inside Telegram'},{command:'apk',description:'Download Android APK'},{command:'privacy',description:'Privacy and service policies'},{command:'download',description:'Windows desktop version'},{command:'connect',description:'Desktop connection invitation'}];
  await api('setMyCommands',{commands});
  const confirmed=await api('getMyCommands');
  if(!confirmed.some(command=>command.command==='apk')||confirmed.some(command=>['android','version'].includes(command.command)))throw Error('Telegram did not confirm the requested command list.');
  return {username:bot.username,origin:root.origin};
}

if(process.argv[1]&&import.meta.url===pathToFileURL(resolve(process.argv[1])).href){
  try{
    const state=readState(),settings=parseEnv(readFileSync('.env','utf8'));
    const result=await updateTelegramCommands({token:settings.TELEGRAM_BOT_TOKEN,origin:state.origin,username:state.botUsername});
    console.log('Updated Telegram @'+result.username+' commands for '+result.origin+'. APK download: /apk.');
  }catch(error){console.error(error.message);process.exitCode=1;}
}
