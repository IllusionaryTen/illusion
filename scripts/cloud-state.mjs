import {readFileSync,writeFileSync,existsSync,mkdirSync} from 'node:fs';
import {spawnSync} from 'node:child_process';
import {randomBytes} from 'node:crypto';
export const stateFile='build/cloudflare-state.json',secretsFile='build/cloudflare-secrets.dpapi';
export function readState(){return existsSync(stateFile)?JSON.parse(readFileSync(stateFile,'utf8')):{};}
export function saveState(state){mkdirSync('build',{recursive:true});writeFileSync(stateFile,JSON.stringify(state,null,2)+'\n');}
function dpapi(input,protect){
  const code='Add-Type -AssemblyName System.Security; $taskInput=[Console]::In.ReadToEnd(); '+(protect?'$taskBytes=[Text.Encoding]::UTF8.GetBytes($taskInput); [Console]::Write([Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect($taskBytes,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)))':'$taskBytes=[Security.Cryptography.ProtectedData]::Unprotect([Convert]::FromBase64String($taskInput),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser); [Console]::Write([Text.Encoding]::UTF8.GetString($taskBytes))');
  const r=spawnSync('powershell.exe',['-NoProfile','-NonInteractive','-Command',code],{input,encoding:'utf8',windowsHide:true});if(r.status!==0)throw Error('Windows could not access the protected cloud administration credentials.');return r.stdout;
}
export function readSecrets(){if(!existsSync(secretsFile))throw Error('Cloud administration credentials are not configured on this Windows account.');return JSON.parse(dpapi(readFileSync(secretsFile,'utf8'),false));}
export function ensureSecrets(){if(existsSync(secretsFile))return readSecrets();const secrets={admin:randomBytes(32).toString('hex'),webhook:randomBytes(32).toString('hex')};mkdirSync('build',{recursive:true});writeFileSync(secretsFile,dpapi(JSON.stringify(secrets),true));return secrets;}
export async function cloudAdmin(path,body){const state=readState(),secret=readSecrets();if(!/^https:\/\/[a-z0-9.-]+\.workers\.dev$/.test(state.origin||''))throw Error('Deploy Illusion to Cloudflare first.');const response=await fetch(state.origin+'/admin/'+path,{method:body?'POST':'GET',headers:{Authorization:'Bearer '+secret.admin,'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined,redirect:'error',signal:AbortSignal.timeout(60000)});const result=await response.json();if(!response.ok)throw Error(result.error||'Cloud administration failed ('+response.status+').');return result;}
