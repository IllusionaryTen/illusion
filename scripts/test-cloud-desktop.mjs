import {localWorker,openTestRegistration} from './cloudflare-test-runtime.mjs';
import {mkdtempSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';
import {spawn} from 'node:child_process';
const dir=mkdtempSync(join(tmpdir(),'illusion-cloud-native-relay-')),mf=localWorker(dir);
try{await openTestRegistration(mf);const base=(await mf.ready).origin,child=spawn(resolve('dist/CloudDesktopTests.exe'),[base],{stdio:'inherit',windowsHide:true});const timeout=setTimeout(()=>child.kill(),90000);process.exitCode=await new Promise((yes,no)=>{child.once('exit',code=>yes(code??1));child.once('error',no);});clearTimeout(timeout);}finally{await mf.dispose();rmSync(dir,{recursive:true,force:true});}
