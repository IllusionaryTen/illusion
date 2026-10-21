import { randomUUID } from 'node:crypto';

// Keep the same synchronous SQL contract as the desktop relay. Ciphertext over
// 128 KiB is split into rows below Cloudflare's 2 MB SQLite row limit.
export class CloudDatabase {
  constructor(storage) { this.storage = storage; this.sql = storage.sql; }
  exec(text) {
    const clean = text.replace(/PRAGMA\s+(?:journal_mode|foreign_keys|secure_delete)\s*=\s*\w+\s*;/gi, '');
    if (clean.trim()) this.sql.exec(clean);
  }
  transaction(work) { return this.storage.transactionSync(work); }
  enableChunks() {
    this.sql.exec(`CREATE TABLE IF NOT EXISTS media_chunks(messageId INTEGER NOT NULL REFERENCES messages(id) ON DELETE CASCADE,part INTEGER NOT NULL,data TEXT NOT NULL,PRIMARY KEY(messageId,part)) WITHOUT ROWID;
      CREATE TABLE IF NOT EXISTS attachment_chunks(messageId INTEGER NOT NULL REFERENCES messages(id) ON DELETE CASCADE,part INTEGER NOT NULL,data TEXT NOT NULL,PRIMARY KEY(messageId,part)) WITHOUT ROWID;
      CREATE TRIGGER IF NOT EXISTS erase_media_chunks AFTER UPDATE OF envelope ON messages WHEN NEW.envelope<>OLD.envelope BEGIN DELETE FROM media_chunks WHERE messageId=OLD.id; END;
      CREATE TRIGGER IF NOT EXISTS erase_attachment_chunks AFTER UPDATE OF media ON messages WHEN NEW.media<>OLD.media BEGIN DELETE FROM attachment_chunks WHERE messageId=OLD.id; END;`);
  }
  hydrate(row) {
    if (row && typeof row.envelope === 'string' && row.envelope.startsWith('@illusion-chunks:')) {
      row.envelope = this.sql.exec('SELECT data FROM media_chunks WHERE messageId=? ORDER BY part', row.id).toArray().map(p => p.data).join('');
      if (!row.envelope) throw Error('Encrypted attachment storage is incomplete.');
    }
    if (row && typeof row.media === 'string' && row.media.startsWith('@illusion-attachment:')) {
      row.media = this.sql.exec('SELECT data FROM attachment_chunks WHERE messageId=? ORDER BY part', row.id).toArray().map(p => p.data).join('');
      if (!row.media) throw Error('Unencrypted attachment storage is incomplete.');
    }
    return row;
  }
  prepare(text) {
    const read = args => this.sql.exec(text, ...args).toArray().map(row => this.hydrate(row));
    return {
      all: (...args) => read(args),
      get: (...args) => read(args)[0],
      run: (...args) => {
        const columns=text.match(/^INSERT INTO messages\(([^)]+)\)/i)?.[1].split(',');
        const envelopeIndex=columns?.indexOf('envelope')??-1;
        const mediaIndex=columns?.indexOf('media')??-1;
        if (envelopeIndex>=0 && Buffer.byteLength(args[envelopeIndex]) > 131072) {
          return this.transaction(() => {
            const envelope = args[envelopeIndex], values = [...args]; values[envelopeIndex] = '@illusion-chunks:' + randomUUID();
            this.sql.exec(text, ...values);
            const id = this.sql.exec('SELECT last_insert_rowid() AS id').one().id;
            for (let offset = 0, part = 0; offset < envelope.length; offset += 131072, part++) this.sql.exec('INSERT INTO media_chunks VALUES(?,?,?)', id, part, envelope.slice(offset, offset + 131072));
            return { lastInsertRowid: id, changes: 1 };
          });
        }
        if (mediaIndex>=0 && Buffer.byteLength(args[mediaIndex])>131072) {
          return this.transaction(() => {
            const media=args[mediaIndex],values=[...args];values[mediaIndex]='@illusion-attachment:'+randomUUID();
            this.sql.exec(text,...values);
            const id=this.sql.exec('SELECT last_insert_rowid() AS id').one().id;
            for(let offset=0,part=0;offset<media.length;offset+=131072,part++)this.sql.exec('INSERT INTO attachment_chunks VALUES(?,?,?)',id,part,media.slice(offset,offset+131072));
            return {lastInsertRowid:id,changes:1};
          });
        }
        const cursor = this.sql.exec(text, ...args);
        return { changes: cursor.rowsWritten, lastInsertRowid: this.sql.exec('SELECT last_insert_rowid() AS id').one().id };
      }
    };
  }
}
