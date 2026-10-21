import { DatabaseSync } from 'node:sqlite';
import { resolve } from 'node:path';
import { fulfillDeletion } from '../server/legal.mjs';
const db=new DatabaseSync(resolve(process.env.DATA_DIR||'./data','illusion.sqlite'));
const [command='list',arg,confirmation]=process.argv.slice(2);
try {
  if(command==='deletions'){
    for(const row of db.prepare("SELECT d.id,d.createdAt,d.status,u.username FROM deletion_requests d JOIN users u ON u.id=d.userId WHERE d.status='pending' ORDER BY d.id").all())console.log(JSON.stringify(row));
  }else if(command==='fulfill-deletion' && /^\d+$/.test(arg) && confirmation){console.log(JSON.stringify(fulfillDeletion(db,Number(arg),confirmation)));
  }else if(command==='list') {
    for(const row of db.prepare("SELECT r.id,r.createdAt,r.reason,r.status,u.username AS target,p.username AS reporter FROM reports r JOIN users u ON u.id=r.targetId JOIN users p ON p.id=r.reporterId ORDER BY r.id DESC LIMIT 50").all()) console.log(JSON.stringify(row));
  } else if(command==='show' && /^\d+$/.test(arg)) {
    const row=db.prepare('SELECT * FROM reports WHERE id=?').get(Number(arg));if(!row)throw Error('Report not found.');console.log(JSON.stringify({...row,evidence:row.evidence?JSON.parse(row.evidence):null},null,2));
  } else if(['resolve','dismiss'].includes(command) && /^\d+$/.test(arg)) {
    db.prepare('UPDATE reports SET status=? WHERE id=?').run(command==='resolve'?'resolved':'dismissed',Number(arg));console.log('Report updated.');
  } else if(['ban','unban'].includes(command) && /^[A-Za-z0-9_-]{2,24}$/.test(arg)) {
    const u=db.prepare('SELECT id FROM users WHERE username=?').get(arg.toLowerCase());if(!u)throw Error('Account not found.');
    if(command==='ban'){db.prepare('INSERT OR IGNORE INTO bans VALUES(?,?)').run(u.id,Date.now());db.prepare('DELETE FROM sessions WHERE userId=?').run(u.id);}else db.prepare('DELETE FROM bans WHERE userId=?').run(u.id);console.log('Account '+command+' complete.');
  }else throw Error('Usage: node --env-file-if-exists=.env scripts/moderate.mjs list | show ID | resolve ID | dismiss ID | ban USERNAME | unban USERNAME');
}finally{db.close();}
