import { createHash, createHmac, createDecipheriv, timingSafeEqual } from 'node:crypto';
import { fonts } from './legal.mjs';
import {imageDimensions} from '../web/security.mjs';

const fail = (status, message) => { throw Object.assign(new Error(message), { status }); };
const hash = s => createHash('sha256').update(s).digest('hex');
export const timers = [0, 60, 3600, 86400, 604800, 2592000];
export function policy(value) {
  if (!value || typeof value.viewOnce !== 'boolean' || !timers.includes(value.ttlSeconds) || Object.keys(value).some(k => !['viewOnce', 'ttlSeconds'].includes(k))) fail(400, 'Invalid disappearing message policy.');
  return { viewOnce: value.viewOnce, ttlSeconds: value.ttlSeconds };
}
export function cleanProfile(p) {
  const allowed = ['bio', 'avatar', 'banner', 'accent', 'textColor', 'font', 'effect'];
  if (!p || typeof p !== 'object' || Array.isArray(p) || Object.keys(p).some(k => !allowed.includes(k))) fail(400, 'Invalid profile fields.');
  const result = {};
  for (const k of ['bio', 'avatar', 'banner']) result[k] = p[k] ?? '';
  if (typeof result.bio !== 'string' || result.bio.length > 300 || /[\x00-\x08\x0b\x0c\x0e-\x1f]/.test(result.bio)) fail(400, 'Keep your description under 300 characters.');
  for (const k of ['avatar', 'banner']) {
    const s = result[k];
    if (typeof s !== 'string' || s.length > 350000 || (s && (!/^data:image\/(png|jpeg);base64,[A-Za-z0-9+/]+={0,2}$/.test(s)))) fail(400, 'Use a PNG or JPEG profile image up to 250 KB.');
    if (s) {
      const bytes = Buffer.from(s.split(',')[1], 'base64');
      if (bytes.length > 256000 || !(bytes.subarray(0, 8).equals(Buffer.from('89504e470d0a1a0a', 'hex')) || bytes.subarray(0, 3).equals(Buffer.from('ffd8ff', 'hex')))) fail(400, 'Invalid profile image.');
      try{imageDimensions(bytes);}catch{fail(400,'Profile image dimensions are invalid or too large.');}
    }
  }
  for (const k of ['accent', 'textColor']) { result[k] = p[k] ?? (k === 'accent' ? '#467AFF' : '#F1F5FF'); if (!/^#[0-9a-fA-F]{6}$/.test(result[k])) fail(400, 'Choose a six-digit hex color.'); }
  result.font = p.font ?? 'Segoe UI'; result.effect = p.effect ?? 'Orbit';
  if (!fonts.includes(result.font) || !['None', 'Orbit', 'Glow', 'Prism', 'Checkerboard'].includes(result.effect)) fail(400, 'Choose an available font and effect.');
  return result;
}
export function features({ db, notify, getUser, safeUser, body, now = Date.now, limit=()=>{}, reserveStorage=()=>{} }) {
  db.exec(`PRAGMA secure_delete=ON;
    CREATE TABLE IF NOT EXISTS profiles(userId TEXT PRIMARY KEY REFERENCES users(id), value TEXT NOT NULL);
    CREATE TABLE IF NOT EXISTS chat_settings(a TEXT NOT NULL,b TEXT NOT NULL,ttlSeconds INTEGER NOT NULL DEFAULT 0,changedBy TEXT NOT NULL,PRIMARY KEY(a,b));
    CREATE TABLE IF NOT EXISTS blocks(userId TEXT NOT NULL,peerId TEXT NOT NULL,PRIMARY KEY(userId,peerId));
    CREATE TABLE IF NOT EXISTS bans(userId TEXT PRIMARY KEY,createdAt INTEGER NOT NULL);
    CREATE TABLE IF NOT EXISTS reports(id INTEGER PRIMARY KEY AUTOINCREMENT,reporterId TEXT NOT NULL,targetId TEXT NOT NULL,messageId INTEGER,reason TEXT NOT NULL,note TEXT NOT NULL,evidence TEXT,createdAt INTEGER NOT NULL,status TEXT NOT NULL DEFAULT 'open');`);
  const columns = new Set(db.prepare('PRAGMA table_info(messages)').all().map(x => x.name));
  for (const [name, type] of [['expiresAt', 'INTEGER NOT NULL DEFAULT 0'], ['viewOnce', 'INTEGER NOT NULL DEFAULT 0'], ['removed', "TEXT NOT NULL DEFAULT ''"], ['envelopeHash', "TEXT NOT NULL DEFAULT ''"], ['envelopeBytes', 'INTEGER NOT NULL DEFAULT 0'], ['media', "TEXT NOT NULL DEFAULT ''"], ['mediaBytes', 'INTEGER NOT NULL DEFAULT 0']]) if (!columns.has(name)) db.exec(`ALTER TABLE messages ADD COLUMN ${name} ${type}`);
  if (!columns.has('envelopeBytes')) db.prepare('UPDATE messages SET envelopeBytes=length(envelope)').run();
  db.exec("CREATE INDEX IF NOT EXISTS message_expiry ON messages(expiresAt) WHERE removed='' AND expiresAt>0");
  const pair = (a, b) => [a, b].sort();
  const ttl = (a, b) => db.prepare('SELECT ttlSeconds FROM chat_settings WHERE a=? AND b=?').get(...pair(a, b))?.ttlSeconds || 0;
  const profile = id => JSON.parse(db.prepare('SELECT value FROM profiles WHERE userId=?').get(id)?.value || '{}');
  const banned = id => !!db.prepare('SELECT 1 FROM bans WHERE userId=?').get(id);
  const blocked = (a, b) => !!db.prepare('SELECT 1 FROM blocks WHERE (userId=? AND peerId=?) OR (userId=? AND peerId=?)').get(a, b, b, a);
  function erase(row, reason, announce=true) {
    const digest=row.envelopeHash||hash(row.envelope??db.prepare('SELECT * FROM messages WHERE id=?').get(row.id).envelope);
    db.prepare("UPDATE messages SET envelopeHash=?,envelope='{}',envelopeBytes=2,media='',mediaBytes=0,removed=? WHERE id=?").run(digest, reason, row.id);
    if(announce){notify(row.senderId); if (row.recipientId !== row.senderId) notify(row.recipientId);}
  }
  function sweep() {const changed=new Set();for (const row of db.prepare("SELECT id,senderId,recipientId,envelopeHash FROM messages WHERE removed='' AND expiresAt>0 AND expiresAt<=? ORDER BY expiresAt LIMIT 100").all(now())){erase(row,'expired',false);changed.add(row.senderId);changed.add(row.recipientId);}for(const id of changed)notify(id);}
  function serialize(row,opened=false) {
    const { envelopeHash, envelopeBytes, mediaBytes, ...value } = row;
    const expired=row.expiresAt>0&&row.expiresAt<=now();
    const hidden=row.removed||expired||row.viewOnce&&!opened;
    let media=null;if(!hidden&&row.media){try{media=JSON.parse(row.media);}catch{fail(500,'Stored attachment is invalid.');}}
    return { ...value, ...(expired&&!row.removed?{removed:'expired'}:{}),viewOnce: !!row.viewOnce, envelope: hidden ? null : JSON.parse(row.envelope), media };
  }
  function validateMedia(value) {
    if(value==null)return null;
    if(!value||typeof value!=='object'||Array.isArray(value)||Object.keys(value).sort().join(',')!=='data,kind,mime'||!['photo','video'].includes(value.kind)||typeof value.data!=='string'||value.data.length>5592408||!/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(value.data))fail(400,'Invalid unencrypted attachment.');
    const bytes=Buffer.from(value.data,'base64');if(!bytes.length||bytes.length>4*1024*1024||bytes.toString('base64')!==value.data)fail(413,'Choose an image or MP4 video up to 4 MB.');
    if(value.kind==='photo'){
      if(!['image/jpeg','image/png'].includes(value.mime)||!(value.mime==='image/jpeg'?bytes.subarray(0,3).equals(Buffer.from('ffd8ff','hex')):bytes.subarray(0,8).equals(Buffer.from('89504e470d0a1a0a','hex'))))fail(400,'Choose a valid PNG or JPEG photo.');
      try{imageDimensions(bytes);}catch{fail(400,'Photo dimensions are invalid or too large.');}
    } else if(value.mime!=='video/mp4'||bytes.length<12||bytes.toString('ascii',4,8)!=='ftyp')fail(400,'Choose a valid MP4 video.');
    return {kind:value.kind,mime:value.mime,data:value.data};
  }
  function validateSend(sender, recipient, envelope) {
    if (banned(recipient) || blocked(sender, recipient)) fail(403, 'This conversation cannot receive new messages.');
    const p = envelope.v === 2 ? policy(envelope.policy) : { viewOnce: false, ttlSeconds: 0 };
    if (p.ttlSeconds !== ttl(sender, recipient)) fail(409, 'The chat timer changed. Refresh this chat and send a new message.');
    if (p.viewOnce && sender === recipient) fail(400, 'View once is for messages to someone else.');
    return p;
  }
  async function route(req, path, url, me) {
    if (req.method === 'PUT' && path === '/v1/profile') {
      limit('profile:'+me.id,10,60000,true);
      const b = await body(req, 750000), p = cleanProfile(b.profile);
      if (typeof b.displayName !== 'string' || !b.displayName.trim() || b.displayName.trim().length > 40) fail(400, 'Display name must be 1–40 characters.');
      const oldProfile=db.prepare('SELECT value FROM profiles WHERE userId=?').get(me.id);reserveStorage(Buffer.byteLength(JSON.stringify(p))-(oldProfile?Buffer.byteLength(oldProfile.value):0)+(oldProfile?0:1024));
      db.prepare('INSERT INTO profiles VALUES(?,?) ON CONFLICT(userId) DO UPDATE SET value=excluded.value').run(me.id, JSON.stringify(p));
      db.prepare('UPDATE users SET displayName=? WHERE id=?').run(b.displayName.trim(), me.id);
      for (const x of db.prepare('SELECT DISTINCT CASE WHEN senderId=? THEN recipientId ELSE senderId END AS id FROM messages WHERE senderId=? OR recipientId=?').all(me.id, me.id, me.id)) notify(x.id);
      notify(me.id); return { user: safeUser(getUser(me.id)) };
    }
    if (req.method === 'GET' && path === '/v1/chat-settings') {
      const peer = url.searchParams.get('peer'); if (!getUser(peer)) fail(404, 'Account not found.');
      return { ttlSeconds: ttl(me.id, peer), blocked: !!db.prepare('SELECT 1 FROM blocks WHERE userId=? AND peerId=?').get(me.id, peer) };
    }
    if (req.method === 'PUT' && path === '/v1/chat-settings') {
      limit('settings:'+me.id,30,60000,true);
      const b = await body(req); if (!getUser(b.peer) || !timers.includes(b.ttlSeconds)) fail(400, 'Choose an available chat timer.');
      if(!db.prepare('SELECT 1 FROM chat_settings WHERE a=? AND b=?').get(...pair(me.id,b.peer)))reserveStorage(256);
      db.prepare('INSERT INTO chat_settings VALUES(?,?,?,?) ON CONFLICT(a,b) DO UPDATE SET ttlSeconds=excluded.ttlSeconds,changedBy=excluded.changedBy').run(...pair(me.id, b.peer), b.ttlSeconds, me.id);
      notify(me.id); if (b.peer !== me.id) notify(b.peer); return { ok: true, ttlSeconds: b.ttlSeconds };
    }
    if (req.method === 'PUT' && path === '/v1/block') {
      limit('block:'+me.id,30,60000,true);
      const b = await body(req); if (!getUser(b.peer) || b.peer === me.id || typeof b.blocked !== 'boolean') fail(400, 'Choose another account.');
      if(b.blocked&&!db.prepare('SELECT 1 FROM blocks WHERE userId=? AND peerId=?').get(me.id,b.peer))reserveStorage(256);
      if (b.blocked) db.prepare('INSERT OR IGNORE INTO blocks VALUES(?,?)').run(me.id, b.peer); else db.prepare('DELETE FROM blocks WHERE userId=? AND peerId=?').run(me.id, b.peer);
      notify(me.id); return { ok: true };
    }
    if (req.method === 'POST' && path === '/v1/open') {
      limit('open:'+me.id,60,60000,true);
      const b = await body(req), row = db.prepare('SELECT * FROM messages WHERE id=? AND recipientId=? AND viewOnce=1').get(b.id, me.id);
      if (!row || row.removed) fail(410, 'This message is no longer available.');
      if(row.expiresAt>0&&row.expiresAt<=now()){erase(row,'expired');fail(410,'This message is no longer available.');}
      // Synchronous read + erase: competing opens cannot obtain a second copy.
      const message = { ...serialize(row,true), envelope: JSON.parse(row.envelope) };
      erase(row, 'opened'); return { message };
    }
    if (req.method === 'POST' && path === '/v1/reports') {
      const recent = db.prepare('SELECT COUNT(*) AS n FROM reports WHERE reporterId=? AND createdAt>?').get(me.id, now() - 3600000).n;
      if (recent >= 10) fail(429, 'You can submit up to 10 reports per hour.');
      reserveStorage(1024);
      const b = await body(req, 8000000);
      if (!['spam', 'harassment', 'threats', 'sexual-content', 'other'].includes(b.reason) || typeof b.note !== 'string' || b.note.length > 1000) fail(400, 'Choose a reason and keep the note under 1,000 characters.');
      if (!getUser(b.targetId) || b.targetId === me.id) fail(400, 'Choose another account to report.');
      let evidence = null;
      if (b.messageId !== undefined) {
        const row = db.prepare('SELECT * FROM messages WHERE id=? AND senderId=? AND recipientId=?').get(b.messageId, b.targetId, me.id);
        if (!row) fail(404, 'Message not found.');
        if (b.evidence) {
          // The reporter deliberately discloses this message's one-time AES/HMAC material.
          const e = b.evidence.envelope, material = Buffer.from(b.evidence.material || '', 'base64');
          if (!e || material.length !== 64 || hash(JSON.stringify(e)) !== (row.envelopeHash || hash(row.envelope))) fail(400, 'Report evidence does not match this message.');
          try {
            const iv = Buffer.from(e.iv, 'base64'), bytes = Buffer.from(e.ciphertext, 'base64'), mac = createHmac('sha256', material.subarray(32)).update(iv).update(bytes).digest();
            const claimed = Buffer.from(e.mac, 'base64'); if (mac.length !== claimed.length || !timingSafeEqual(mac, claimed)) throw Error();
            const cipher = createDecipheriv('aes-256-cbc', material.subarray(0, 32), iv);
            const plaintext = Buffer.concat([cipher.update(bytes), cipher.final()]).toString('utf8');
            // Media bytes are never retained in reports; only selected text/caption is shared.
            let text = plaintext;
            try { const content = JSON.parse(plaintext); if (content.format === 'illusion.message.v2' || content.format === 'illusion.message.v1') text = content.text || ''; } catch { }
            evidence = JSON.stringify({ text: text.slice(0, 2000), verifiedMessage: true });
          } catch { fail(400, 'Could not verify the selected evidence.'); }
          material.fill(0);
        }
      } else if (b.evidence) fail(400, 'Message evidence needs a message.');
      if (b.messageId === undefined) evidence = JSON.stringify({ profile: profile(b.targetId), displayName: getUser(b.targetId).displayName });
      reserveStorage(1024+Buffer.byteLength(evidence||'')+Buffer.byteLength(b.note));
      const id = db.prepare('INSERT INTO reports(reporterId,targetId,messageId,reason,note,evidence,createdAt) VALUES(?,?,?,?,?,?,?)').run(me.id, b.targetId, b.messageId ?? null, b.reason, b.note, evidence, now()).lastInsertRowid;
      return { ok: true, reportId: Number(id) };
    }
  }
  return { route, sweep, serialize, validateSend, validateMedia, profile, banned, ttl, now };
}
