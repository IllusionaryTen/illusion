import {createHash} from 'node:crypto';
export function durableLimiter(db){
  db.exec('CREATE TABLE IF NOT EXISTS request_limits(key TEXT PRIMARY KEY,count INTEGER NOT NULL,resetsAt INTEGER NOT NULL); CREATE INDEX IF NOT EXISTS request_limits_expiry ON request_limits(resetsAt)');
  const update=db.prepare('INSERT INTO request_limits VALUES(?,1,?) ON CONFLICT(key) DO UPDATE SET count=CASE WHEN resetsAt<=? THEN 1 ELSE count+1 END,resetsAt=CASE WHEN resetsAt<=? THEN excluded.resetsAt ELSE resetsAt END WHERE resetsAt<=? OR count<? RETURNING count');
  return {hit(key,max,window,now){const id=createHash('sha256').update(key).digest('hex'),row=update.get(id,now+window,now,now,now,max);return !!row&&row.count<=max;},sweep(now){db.prepare('DELETE FROM request_limits WHERE resetsAt<=?').run(now);}};
}
