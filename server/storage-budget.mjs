export const storageLimit=256*1024*1024;
// Account for encrypted bodies, profile/report payloads and bounded estimates
// for row/index overhead. Incremental triggers avoid scanning every stored
// ciphertext on each request, and include tombstones/metadata in the quota.
export function storageBudget(db){
  const sizes={users:'4096',sessions:'256',messages:'1024+envelopeBytes+mediaBytes',groups:"2048+length(CAST(name AS BLOB))",group_members:'256',group_messages:'1024+envelopeBytes',group_reads:'256',group_events:"256+length(CAST(COALESCE(targetId,'') AS BLOB))",profiles:'1024+length(CAST(value AS BLOB))',reports:"1024+length(CAST(COALESCE(evidence,'') AS BLOB))+length(CAST(note AS BLOB))",reads:'256',receipts:'256',chat_settings:'256',blocks:'256',bans:'256',consents:'256',deletion_requests:'256',request_limits:'256'};
  db.exec('CREATE TABLE IF NOT EXISTS storage_usage(name TEXT PRIMARY KEY,bytes INTEGER NOT NULL)');
  for(const [table,size]of Object.entries(sizes)){
    if(!db.prepare("SELECT 1 FROM sqlite_master WHERE type='table' AND name=?").get(table))continue;
    if(!db.prepare('SELECT 1 FROM storage_usage WHERE name=?').get(table))db.prepare('INSERT INTO storage_usage SELECT ?,COALESCE(SUM('+size+'),0) FROM '+table).run(table);
    const qualify=prefix=>size.replace(/\b(envelopeBytes|mediaBytes|value|evidence|note|name|targetId)\b/g,prefix+'.$1'),next=qualify('NEW'),old=qualify('OLD');
    db.exec(`CREATE TRIGGER IF NOT EXISTS budget_${table}_insert AFTER INSERT ON ${table} BEGIN UPDATE storage_usage SET bytes=bytes+(${next}) WHERE name='${table}'; END;
      CREATE TRIGGER IF NOT EXISTS budget_${table}_delete AFTER DELETE ON ${table} BEGIN UPDATE storage_usage SET bytes=MAX(0,bytes-(${old})) WHERE name='${table}'; END;
      CREATE TRIGGER IF NOT EXISTS budget_${table}_update AFTER UPDATE ON ${table} WHEN (${next})<>(${old}) BEGIN UPDATE storage_usage SET bytes=MAX(0,bytes+(${next})-(${old})) WHERE name='${table}'; END;`);
  }
  return {used(){return db.prepare('SELECT COALESCE(SUM(bytes),0) AS n FROM storage_usage').get().n;},require(bytes){if(bytes>0&&this.used()+bytes>storageLimit)throw Object.assign(Error('Relay storage capacity reached. Expire content or ask the host to fulfill deletion requests.'),{status:507});}};
}
