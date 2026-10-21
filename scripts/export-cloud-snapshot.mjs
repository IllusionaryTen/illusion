import {DatabaseSync} from 'node:sqlite';
import {readFileSync,writeFileSync,mkdirSync} from 'node:fs';
import {resolve} from 'node:path';
import {createHash} from 'node:crypto';
import {migrationTables} from '../cloudflare/migration.mjs';
const db=new DatabaseSync(resolve(process.env.DATA_DIR||'./data','illusion.sqlite'),{readOnly:true}),tables={};
try{db.exec('BEGIN');for(const table of migrationTables){tables[table]=db.prepare('SELECT * FROM '+table).all();if(table==='messages')for(const row of tables[table])row.envelopeBytes=Buffer.byteLength(row.envelope);}db.exec('COMMIT');}finally{db.close();}
const snapshot={format:'illusion.cloud-migration.v1',sourceHash:createHash('sha256').update(JSON.stringify(tables)).digest('hex'),tables},encoded=JSON.stringify(snapshot);
if(Buffer.byteLength(encoded)>7500000)throw Error('This migration exceeds the single-snapshot import limit. The original local database is unchanged.');
mkdirSync('build',{recursive:true});writeFileSync('build/cloud-migration.json',encoded);
console.log('Created a consistent read-only migration snapshot: '+JSON.stringify(Object.fromEntries(Object.entries(tables).map(([t,rows])=>[t,rows.length])))+'. No private device keys or reusable session tokens are included.');
