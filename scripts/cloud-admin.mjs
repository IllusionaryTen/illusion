import {cloudAdmin} from './cloud-state.mjs';
const [command='list',value,username]=process.argv.slice(2);
try{
  if(command==='status')console.log(JSON.stringify(await cloudAdmin('status'),null,2));
  else if(command==='list'||command==='show'){
    const {reports}=await cloudAdmin('reports');
    if(command==='show'){const r=reports.find(r=>r.id===Number(value));if(!r)throw Error('Open report not found.');console.log(JSON.stringify({...r,evidence:r.evidence?JSON.parse(r.evidence):null},null,2));}
    else for(const {id,createdAt,targetId,reason,status}of reports)console.log(JSON.stringify({id,createdAt,targetId,reason,status}));
  }else if(['resolve','dismiss'].includes(command)&&/^\d+$/.test(value))console.log(JSON.stringify(await cloudAdmin('report',{id:Number(value),status:command==='resolve'?'resolved':'dismissed'})));
  else if(['ban','unban'].includes(command)&&/^[A-Za-z0-9_-]{2,24}$/.test(value))console.log(JSON.stringify(await cloudAdmin('ban',{username:value,banned:command==='ban'})));
  else if(command==='deletions')console.log(JSON.stringify(await cloudAdmin('deletions'),null,2));
  else if(command==='fulfill-deletion'&&/^\d+$/.test(value)&&username)console.log(JSON.stringify(await cloudAdmin('fulfill-deletion',{id:Number(value),username})));
  else throw Error('Usage: node scripts/cloud-admin.mjs status | list | show ID | resolve ID | dismiss ID | ban USERNAME | unban USERNAME | deletions | fulfill-deletion ID EXACT_USERNAME');
}catch(e){console.error(e.message);process.exitCode=1;}
