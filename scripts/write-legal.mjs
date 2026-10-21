import { writeFileSync,mkdirSync } from 'node:fs';
import { documents,operator,legalVersion } from '../server/legal.mjs';
const out=process.argv[2];if(!out)throw Error('Output path required.');writeFileSync(out,JSON.stringify({version:legalVersion,operator:operator(),documents:documents()}));
if(process.argv.includes('--documents')){mkdirSync('legal',{recursive:true});for(const [id,text] of Object.entries(documents()))writeFileSync('legal/'+id+'.md',text+'\n');}
