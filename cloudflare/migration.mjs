import {createHash} from 'node:crypto';
import {validateEnvelope} from '../server/relay.mjs';
export const migrationTables=['users','profiles','messages','reads','receipts','chat_settings','blocks','bans','reports','consents','deletion_requests'];
export function importSnapshot(db,snapshot){
  if(snapshot?.format!=='illusion.cloud-migration.v1' || !snapshot.tables || Object.keys(snapshot.tables).some(t=>!migrationTables.includes(t)))throw Error('Invalid migration snapshot.');
  const hash=createHash('sha256').update(JSON.stringify(snapshot.tables)).digest('hex');
  if(hash!==snapshot.sourceHash)throw Error('Snapshot checksum does not match.');
  return db.transaction(()=>{
    if(db.prepare("SELECT value FROM cloud_settings WHERE key='import_hash'").get()?.value===hash)return {ok:true,alreadyImported:true};
    if(db.prepare('SELECT COUNT(*) AS n FROM users').get().n!==0)throw Error('Import is only allowed into an empty hosted account database.');
    const counts={};
    for(const table of migrationTables){
      const rows=snapshot.tables[table]||[];if(!Array.isArray(rows))throw Error('Invalid table rows.');
      const allowed=new Set(db.prepare('PRAGMA table_info('+table+')').all().map(c=>c.name));
      for(const row of rows){
        const fields=Object.keys(row);if(!fields.length || fields.some(f=>!allowed.has(f)))throw Error('Unexpected snapshot columns.');
        if(table==='messages' && !row.removed){const key=db.prepare('SELECT publicKey FROM users WHERE id=?').get(row.senderId)?.publicKey;validateEnvelope(JSON.parse(row.envelope),row.senderId,row.recipientId,row.clientId,key);}
        db.prepare('INSERT INTO '+table+'('+fields.join(',')+') VALUES('+fields.map(()=>'?').join(',')+')').run(...fields.map(f=>row[f]));
      }
      counts[table]=rows.length;
    }
    db.prepare("INSERT OR REPLACE INTO cloud_settings VALUES('import_hash',?)").run(hash);
    return {ok:true,counts};
  });
}
