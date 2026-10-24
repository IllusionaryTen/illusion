import { randomBytes, randomUUID, createHash, createHmac, createPublicKey, randomInt, timingSafeEqual, verify } from 'node:crypto';
import { features, policy } from './features.mjs';
import { legalRoutes } from './legal.mjs';
import { transaction } from './transaction.mjs';
import { version } from '../shared/version.mjs';
import {passwordHash,passwordMatches,needsPasswordUpgrade} from './passwords.mjs';
import {durableLimiter} from './limits.mjs';
import {storageBudget} from './storage-budget.mjs';
const hash = value => createHash('sha256').update(value).digest('hex');
const fail = (status, message) => { throw Object.assign(new Error(message), { status }); };
const cleanName = value => typeof value === 'string' ? value.trim().toLowerCase() : '';
const validUsername = value => typeof value === 'string' && /^[A-Za-z0-9_-]{2,24}$/.test(value.trim());
const usernameHint = 'Usernames must be 2–24 characters: Latin letters (A–Z), numbers, _ or -.';
function publicKey(xml) {
  if (typeof xml !== 'string' || xml.length > 1800) fail(400, 'A valid encryption key is required.');
  const n = xml.match(/^<RSAKeyValue><Modulus>([A-Za-z0-9+/]{512})<\/Modulus><Exponent>AQAB<\/Exponent><\/RSAKeyValue>$/)?.[1];
  if (!n || Buffer.from(n,'base64')[0]<128) fail(400, 'A canonical 3072-bit RSA public key is required.');
  try { return createPublicKey({ key: { kty: 'RSA', n: Buffer.from(n, 'base64').toString('base64url'), e: 'AQAB' }, format: 'jwk' }); }
  catch { fail(400, 'Invalid public key.'); }
}
export function signatureText(sender, recipient, clientId, e) {
  return ['illusion:v' + e.v, sender, recipient, clientId, e.iv, e.ciphertext, e.mac, e.keys[sender], e.keys[recipient], ...(e.v === 2 ? [e.policy.viewOnce ? '1' : '0', String(e.policy.ttlSeconds)] : [])].join('\n');
}
export function validateEnvelope(e, sender, recipient, clientId, xml) {
  if (!e || ![1, 2].includes(e.v) || !e.keys || Array.isArray(e.keys)) fail(400, 'An encrypted message is required.');
  if (e.v === 2) policy(e.policy);
  const isBase64 = (v, min, max) => typeof v === 'string' && v.length % 4 === 0 && /^[A-Za-z0-9+/]+={0,2}$/.test(v) && Buffer.from(v, 'base64').length >= min && Buffer.from(v, 'base64').length <= max;
  if (!isBase64(e.iv, 16, 16) || !isBase64(e.mac, 32, 32) || !isBase64(e.ciphertext, 16, e.v === 2 ? 5600000 : 12016) || Buffer.from(e.ciphertext, 'base64').length % 16 !== 0 || !isBase64(e.signature, 384, 384)) fail(400, 'Invalid encrypted message.');
  if (Object.keys(e.keys).some(k => k !== sender && k !== recipient) || !isBase64(e.keys[sender], 384, 384) || !isBase64(e.keys[recipient], 384, 384)) fail(400, 'Invalid message keys.');
  if (!verify('RSA-SHA256', Buffer.from(signatureText(sender, recipient, clientId, e)), publicKey(xml), Buffer.from(e.signature, 'base64'))) fail(400, 'Message signature is invalid.');
  return { v: e.v, iv: e.iv, ciphertext: e.ciphertext, mac: e.mac, keys: e.keys, signature: e.signature, ...(e.v === 2 ? { policy: policy(e.policy) } : {}) };
}

export function createRelay({ db, registrationCode = '', now = Date.now, timers = true, onNotify = () => {}, webHandler = () => false, liveTransport = 'long-poll', persistentLimits = false, sendEmail = null, emailCodeKey = '', emailRecoveryAvailable = Boolean(sendEmail&&emailCodeKey.length>=32) } = {}) {
  db.exec(`PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;
    CREATE TABLE IF NOT EXISTS users(id TEXT PRIMARY KEY, username TEXT UNIQUE NOT NULL, displayName TEXT NOT NULL, salt TEXT NOT NULL, passwordHash TEXT NOT NULL, publicKey TEXT NOT NULL, createdAt INTEGER NOT NULL);
    CREATE TABLE IF NOT EXISTS sessions(tokenHash TEXT PRIMARY KEY, userId TEXT NOT NULL REFERENCES users(id), expiresAt INTEGER NOT NULL);
    CREATE TABLE IF NOT EXISTS messages(id INTEGER PRIMARY KEY AUTOINCREMENT, senderId TEXT NOT NULL REFERENCES users(id), recipientId TEXT NOT NULL REFERENCES users(id), clientId TEXT NOT NULL, envelope TEXT NOT NULL, createdAt INTEGER NOT NULL, UNIQUE(senderId, clientId));
    CREATE INDEX IF NOT EXISTS message_pair ON messages(senderId, recipientId, id);
    CREATE INDEX IF NOT EXISTS message_recipient ON messages(recipientId, id);
    CREATE TABLE IF NOT EXISTS reads(userId TEXT NOT NULL, peerId TEXT NOT NULL, lastId INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(userId, peerId));
    CREATE TABLE IF NOT EXISTS receipts(userId TEXT NOT NULL, peerId TEXT NOT NULL, lastId INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(userId, peerId));
    CREATE TABLE IF NOT EXISTS notices(id INTEGER PRIMARY KEY AUTOINCREMENT, version TEXT NOT NULL, text TEXT NOT NULL, createdAt INTEGER NOT NULL);`);
  try { db.exec('ALTER TABLE users ADD COLUMN email TEXT'); } catch(e) { if (!String(e.message).includes('duplicate column')) throw e; }
  try { db.exec('ALTER TABLE users ADD COLUMN emailVerifiedAt INTEGER'); } catch(e) { if (!String(e.message).includes('duplicate column')) throw e; }
  db.exec('CREATE UNIQUE INDEX IF NOT EXISTS users_email_unique ON users(email) WHERE email IS NOT NULL; CREATE TABLE IF NOT EXISTS email_codes(id TEXT PRIMARY KEY,userId TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,purpose TEXT NOT NULL,email TEXT NOT NULL,codeHash TEXT NOT NULL,expiresAt INTEGER NOT NULL,attempts INTEGER NOT NULL DEFAULT 0,createdAt INTEGER NOT NULL); CREATE INDEX IF NOT EXISTS email_codes_lookup ON email_codes(userId,purpose,expiresAt); CREATE TABLE IF NOT EXISTS account_recovery_challenges(id TEXT PRIMARY KEY,username TEXT NOT NULL,userId TEXT,nonce TEXT NOT NULL,expiresAt INTEGER NOT NULL,attempts INTEGER NOT NULL DEFAULT 0); CREATE INDEX IF NOT EXISTS recovery_challenges_expiry ON account_recovery_challenges(expiresAt);');
  const limits = new Map();
  db.exec('CREATE INDEX IF NOT EXISTS sessions_expiry ON sessions(expiresAt); CREATE INDEX IF NOT EXISTS sessions_user ON sessions(userId,expiresAt)');
  const durable=persistentLimits?durableLimiter(db):null;
  let passwordWork=0,bodyBudget=0,bodyCount=0;const activeBodies=new Set();
  async function passwordJob(work){if(passwordWork>=1)fail(503,'Sign-in is busy. Please retry shortly.');passwordWork++;try{return await work();}finally{passwordWork--;}}
  const boot = randomUUID();
  const revisions = new Map();
  const watchers = new Map();
  const cursorFor = user => boot + ':' + (revisions.get(user) || 0);
  function notify(user) {
    revisions.set(user, (revisions.get(user) || 0) + 1);
    try { onNotify(user, cursorFor(user)); } catch { console.error('Live notification failed.'); }
    for (const finish of [...(watchers.get(user) || [])]) finish(true);
  }
  function publishNotice(version, text) {
    if (!/^\d+\.\d+\.\d+$/.test(version)) fail(400, 'Use a release version like 0.8.4.');
    if (typeof text !== 'string' || !text.trim() || text.length > 500) fail(400, 'Notice text must be 1–500 characters.');
    const at = now();
    db.prepare('INSERT INTO notices(version,text,createdAt) VALUES(?,?,?)').run(version, text.trim(), at);
    // Keep the service history bounded; notices are host broadcasts, not chats.
    db.prepare('DELETE FROM notices WHERE id<=(SELECT MAX(id) FROM notices)-50');
    for (const { userId } of db.prepare('SELECT DISTINCT userId FROM sessions WHERE expiresAt>?').all(at)) notify(userId);
    return db.prepare('SELECT id,version,text,createdAt FROM notices WHERE createdAt=? AND version=?').get(at, version);
  }
  function waitForChange(user, cursor, res) {
    if (cursor !== cursorFor(user)) return Promise.resolve({ cursor: cursorFor(user), changed: true });
    const pending = watchers.get(user) || new Set();
    if (pending.size >= 5) fail(429, 'Too many live connections for this account.');
    watchers.set(user, pending);
    return new Promise(resolve => {
      const finish = changed => {
        clearTimeout(timeout);res.off('close', disconnected);pending.delete(finish);
        if (!pending.size) watchers.delete(user);
        resolve({ cursor: cursorFor(user), changed });
      };
      const disconnected = () => finish(false);
      const timeout = setTimeout(() => finish(false), 8000);
      pending.add(finish);res.once('close', disconnected);
    });
  }
  function limit(key, max, window = 60_000, persist=false) {
    const now = Date.now();
    if(persist&&durable){if(!durable.hit(key,max,window,now))fail(429,'Too many attempts. Please wait before trying again.');return;}
    let entry = limits.get(key);
    if (!entry || entry.reset < now) { entry = { count: 0, reset: now + window }; limits.set(key, entry); }
    if (++entry.count > max) fail(429, 'Too many attempts. Please wait a minute.');
  }
  function maintain() {
    legal.sweep();
    durable?.sweep(Date.now());
    db.prepare('DELETE FROM email_codes WHERE expiresAt<?').run(Date.now());
    db.prepare('DELETE FROM account_recovery_challenges WHERE expiresAt<?').run(Date.now());
    for (const [k, v] of limits) if (v.reset < Date.now()) limits.delete(k);
    db.prepare('DELETE FROM sessions WHERE expiresAt < ?').run(Date.now());
  }
  const cleanup = timers ? setInterval(maintain, 60_000) : null;
  cleanup?.unref();
  const safeUser = row => ({ id: row.id, username: row.username, displayName: row.displayName, publicKey: row.publicKey, profile: extra.profile(row.id) });
  const getUser = id => db.prepare('SELECT * FROM users WHERE id=?').get(id);
  const extra = features({ db, notify, getUser, safeUser, body, now, limit, reserveStorage:bytes=>storage.require(bytes) });
  const legal = legalRoutes(db,notify);legal.sweep();
  const storage=storageBudget(db);
  const expiry = timers ? setInterval(() => extra.sweep(), 1000) : null; expiry?.unref();
  function session(user) {
    const token = randomBytes(32).toString('hex');
    db.prepare('INSERT INTO sessions VALUES(?,?,?)').run(hash(token), user.id, Date.now() + 30 * 86400_000);
    db.prepare('DELETE FROM sessions WHERE userId=? AND tokenHash NOT IN (SELECT tokenHash FROM sessions WHERE userId=? ORDER BY expiresAt DESC,rowid DESC LIMIT 10)').run(user.id,user.id);
    notify(user.id);
    return { token, user: safeUser(user) };
  }
  async function body(req, maximum = 30000) {
    if(!/^application\/json(?:\s*;|$)/i.test(req.headers['content-type']||''))fail(415,'Use application/json.');
    const actor=req.actor||'ip:'+req.socket.remoteAddress;
    if(bodyCount>=4||bodyBudget+maximum>12000000||activeBodies.has(actor))fail(503,'Request processing is busy. Please retry.');
    bodyCount++;bodyBudget+=maximum;activeBodies.add(actor);
    try{
    let size = 0;
    const chunks = [];
    for await (const chunk of req) {
      size += chunk.length;
      if (size > maximum) fail(413, 'Request is too large.');
      chunks.push(chunk);
    }
    try { const b = JSON.parse(Buffer.concat(chunks).toString('utf8')); if (!b || typeof b !== 'object' || Array.isArray(b)) throw new Error(); return b; }
    catch { fail(400, 'Invalid JSON.'); }
    }finally{bodyCount--;bodyBudget-=maximum;activeBodies.delete(actor);}
  }
  function authenticate(token) {
  const s = db.prepare('SELECT * FROM sessions WHERE tokenHash=? AND expiresAt>?').get(hash(token), Date.now());
  if (!s) fail(401, 'Please sign in again.');
  const me = getUser(s.userId);
  if (extra.banned(me.id)) fail(403, 'This account is suspended.');
    return {user:me,session:s};
  }
  const validEmail = value => typeof value === 'string' && value.length <= 254 && /^[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@(?:gmail\.com|googlemail\.com)$/i.test(value.trim());
  const emailDigest = (id,code) => createHmac('sha256',emailCodeKey).update(id+'\n'+code.toUpperCase()).digest('hex');
  const newEmailCode = () => Array.from({length:6},()=>String.fromCharCode(65+randomInt(26))).join('');
  const maskEmail = email => { const [local,domain]=email.split('@');return (local.slice(0,1)+'***'+(local.length>2?local.slice(-1):''))+'@'+domain; };
  const recoveryText = row => 'illusion:account-recovery:v1\n'+row.id+'\n'+row.username+'\n'+row.nonce+'\n'+row.expiresAt;
  async function issueEmailCode(user,email,purpose) {
    if (!emailCodeKey || emailCodeKey.length<32 || typeof sendEmail!=='function') fail(503,'Email verification is not configured yet.');
    email=email.trim().toLowerCase();
    if (!validEmail(email)) fail(400,'Enter a valid Gmail address.');
    limit('email-send:'+user.id,3,3600000,true);
    const code=newEmailCode(),id=randomUUID(),created=now();
    db.prepare('DELETE FROM email_codes WHERE userId=? AND purpose=?').run(user.id,purpose);
    db.prepare('INSERT INTO email_codes(id,userId,purpose,email,codeHash,expiresAt,attempts,createdAt) VALUES(?,?,?,?,?,?,0,?)').run(id,user.id,purpose,email,emailDigest(id,code),created+10*60_000,created);
    try { await sendEmail({to:email,subject:purpose==='recovery'?'Illusion password recovery code':'Verify your Illusion email',code,expiresMinutes:10}); }
    catch { db.prepare('DELETE FROM email_codes WHERE id=?').run(id); fail(503,'Illusion could not send the email code. Try again later.'); }
    return {ok:true};
  }
  function verifyEmailCode(user,email,purpose,code) {
    if (!emailCodeKey || emailCodeKey.length<32) fail(503,'Email verification is not configured yet.');
    const row=db.prepare('SELECT * FROM email_codes WHERE userId=? AND purpose=? AND email=? ORDER BY createdAt DESC LIMIT 1').get(user.id,purpose,email.toLowerCase());
    if (!row || row.expiresAt<=now() || row.attempts>=5) { if(row)db.prepare('DELETE FROM email_codes WHERE id=?').run(row.id); fail(400,'That code is invalid or expired. Request a new one.'); }
    const candidate=Buffer.from(emailDigest(row.id,typeof code==='string'?code:''),'hex'),expected=Buffer.from(row.codeHash,'hex');
    if (typeof code!=='string'||!/^[A-Z]{6}$/i.test(code)||candidate.length!==expected.length||!timingSafeEqual(candidate,expected)) { db.prepare('UPDATE email_codes SET attempts=attempts+1 WHERE id=?').run(row.id); fail(400,'That code is invalid or expired. Request a new one.'); }
    db.prepare('DELETE FROM email_codes WHERE id=?').run(row.id);
    return row;
  }
  const messageRow = row => extra.serialize(row);
  async function route(req, res) {
    const url = new URL(req.url, 'http://localhost');
    const path = url.pathname;
    extra.sweep();
    const ip = req.socket.remoteAddress;
    limit('all:' + ip, 1800);
    if (req.method === 'GET' && path === '/health') return { app: 'Illusion', version, liveTransport, instance: boot, encryption: 'RSA3072-AES256-CBC-HMAC-SHA256', features: ['profiles', 'reports', 'view-once', 'expiry', 'media'] };
    if (req.method === 'POST' && ['/v1/register', '/v1/login'].includes(path)) {
      limit('auth:' + ip, 15);
      const b = await body(req);
      const username = cleanName(b.username);
      if (!validUsername(b.username)) fail(400, usernameHint);
      if (typeof b.password !== 'string' || b.password.length < 10 || b.password.length > 128) fail(400, 'Use a password of 10–128 characters.');
      if (path.endsWith('register')) {
        legal.validate(b);
        if (registrationCode && b.registrationCode !== registrationCode) fail(403, 'An invitation code is required.');
        publicKey(b.publicKey);
        if (typeof b.displayName !== 'string' || !b.displayName.trim() || b.displayName.trim().length > 40) fail(400, 'Display name must be 1–40 characters.');
        limit('register-ip:'+ip,10,86400000,true);
        if(db.prepare('SELECT COUNT(*) AS n FROM users').get().n>=1000)fail(503,'This preview has reached its account capacity.');
        storage.require(5000);
        const salt = randomBytes(16).toString('hex');
        const encodedPassword = await passwordJob(()=>passwordHash(b.password,salt));
        const id = randomUUID();
        try { transaction(db, () => { db.prepare('INSERT INTO users(id,username,displayName,salt,passwordHash,publicKey,createdAt) VALUES(?,?,?,?,?,?,?)').run(id, username, b.displayName.trim(), salt, encodedPassword, b.publicKey, Date.now()); legal.record(id,b); }); }
        catch(e) { if(String(e.message).includes('UNIQUE')) fail(409,'That username is already taken.'); throw e; }
        return session(getUser(id));
      }
      const user = db.prepare('SELECT * FROM users WHERE username=?').get(username);
      if(user)limit('login-user:'+user.id,30,900000,true);
      const matches=await passwordJob(()=>passwordMatches(b.password,user?.salt||'not-a-real-user-salt',user?.passwordHash));
      if (!user || !matches) fail(401, 'Username or password is incorrect.');
      if (extra.banned(user.id)) fail(403, 'This account is suspended.');
      if(needsPasswordUpgrade(user.passwordHash)){const upgraded=await passwordJob(()=>passwordHash(b.password,user.salt));db.prepare('UPDATE users SET passwordHash=? WHERE id=? AND passwordHash=?').run(upgraded,user.id,user.passwordHash);}
      return session(user);
    }
    if(req.method==='POST'&&path==='/v1/recovery/start'){
      limit('recovery-ip:'+ip,5,3600000,true);if(!emailRecoveryAvailable)fail(503,'Email recovery is not configured yet. Use a saved device key or .illusionkey backup.');const b=await body(req),username=cleanName(b.username),email=typeof b.email==='string'?b.email.trim().toLowerCase():'';
      if(validUsername(b.username)&&validEmail(email)){const user=db.prepare('SELECT id,email,emailVerifiedAt FROM users WHERE username=?').get(username);if(user?.emailVerifiedAt&&user.email===email){try{await issueEmailCode(user,email,'recovery');}catch{}}}
      return {ok:true,message:'If the account has this verified Gmail address, a code will arrive shortly.'};
    }
    if(req.method==='POST'&&path==='/v1/recovery/key/challenge'){
      limit('key-recovery-ip:'+ip,10,3600000,true);const b=await body(req);if(!validUsername(b.username))fail(400,usernameHint);
      db.prepare('DELETE FROM account_recovery_challenges WHERE expiresAt<?').run(now());
      const username=cleanName(b.username),user=db.prepare('SELECT id FROM users WHERE username=?').get(username),row={id:randomUUID(),username,userId:user?.id||null,nonce:randomBytes(32).toString('base64url'),expiresAt:now()+5*60_000};
      db.prepare('INSERT INTO account_recovery_challenges(id,username,userId,nonce,expiresAt,attempts) VALUES(?,?,?,?,?,0)').run(row.id,row.username,row.userId,row.nonce,row.expiresAt);
      return {challengeId:row.id,challenge:row.nonce,expiresAt:row.expiresAt};
    }
    if(req.method==='POST'&&path==='/v1/recovery/key/finish'){
      limit('key-recovery-finish-ip:'+ip,10,3600000,true);const b=await body(req);
      if(!validUsername(b.username)||typeof b.challengeId!=='string'||typeof b.signature!=='string'||typeof b.password!=='string'||b.password.length<10||b.password.length>128)fail(400,'Enter the username, device proof, and a password of 10–128 characters.');
      const row=db.prepare('SELECT * FROM account_recovery_challenges WHERE id=?').get(b.challengeId),username=cleanName(b.username);
      if(!row||row.username!==username||row.expiresAt<=now()||row.attempts>=5||!row.userId){if(row)db.prepare('DELETE FROM account_recovery_challenges WHERE id=?').run(row.id);fail(400,'This device cannot prove the account key, or the recovery challenge expired.');}
      const user=getUser(row.userId);let signature;try{signature=Buffer.from(b.signature,'base64');}catch{signature=Buffer.alloc(0);}
      const valid=signature.length===384&&verify('RSA-SHA256',Buffer.from(recoveryText(row)),publicKey(user.publicKey),signature);
      if(!valid){db.prepare('UPDATE account_recovery_challenges SET attempts=attempts+1 WHERE id=?').run(row.id);fail(400,'This device cannot prove the account key, or the recovery challenge expired.');}
      const salt=randomBytes(16).toString('hex'),encoded=await passwordJob(()=>passwordHash(b.password,salt));
      db.prepare('UPDATE users SET salt=?,passwordHash=? WHERE id=?').run(salt,encoded,user.id);db.prepare('DELETE FROM sessions WHERE userId=?').run(user.id);db.prepare('DELETE FROM email_codes WHERE userId=?').run(user.id);db.prepare('DELETE FROM account_recovery_challenges WHERE userId=?').run(user.id);
      return {ok:true};
    }
    if(req.method==='POST'&&path==='/v1/recovery/finish'){
      limit('recovery-finish-ip:'+ip,10,3600000,true);const b=await body(req);
      if(!validUsername(b.username)||!validEmail(b.email)||typeof b.password!=='string'||b.password.length<10||b.password.length>128)fail(400,'Enter the account username, Gmail address, and a new password of 10–128 characters.');
      const user=db.prepare('SELECT * FROM users WHERE username=?').get(cleanName(b.username));
      if(!user||!user.emailVerifiedAt||user.email!==b.email.trim().toLowerCase())fail(400,'That code is invalid or expired. Request a new one.');
      verifyEmailCode(user,user.email,'recovery',b.code);
      const salt=randomBytes(16).toString('hex'),encoded=await passwordJob(()=>passwordHash(b.password,salt));
      db.prepare('UPDATE users SET salt=?,passwordHash=? WHERE id=?').run(salt,encoded,user.id);
      db.prepare('DELETE FROM sessions WHERE userId=?').run(user.id);db.prepare('DELETE FROM email_codes WHERE userId=?').run(user.id);
      return {ok:true};
    }
    const token = req.headers.authorization?.replace(/^Bearer /, '') || '';
    const { user: me } = authenticate(token);
    req.actor=me.id;
    if(req.method==='GET'&&path==='/v1/email/status')return {available:emailRecoveryAvailable,verified:Boolean(me.emailVerifiedAt),email:me.emailVerifiedAt?maskEmail(me.email):null};
    if(req.method==='POST'&&path==='/v1/email/start'){const b=await body(req);if(me.emailVerifiedAt)fail(409,'This account already has a verified Gmail address.');return issueEmailCode(me,b.email,'link');}
    if(req.method==='POST'&&path==='/v1/email/verify'){
      const b=await body(req),email=typeof b.email==='string'?b.email.trim().toLowerCase():'';if(me.emailVerifiedAt)fail(409,'This account already has a verified Gmail address.');
      const row=verifyEmailCode(me,email,'link',b.code);
      try{db.prepare('UPDATE users SET email=?,emailVerifiedAt=? WHERE id=?').run(row.email,now(),me.id);}catch(e){if(String(e.message).includes('UNIQUE'))fail(409,'That Gmail address is already linked to another account.');throw e;}
      return {ok:true,email:maskEmail(row.email)};
    }
    if (req.method==='POST' && path==='/v1/deletion-request') {
      limit('deletion:'+me.id,5,60000,true);const b=await body(req);
      if(typeof b.password!=='string' || b.password.length>128 || b.confirmUsername!==me.username)fail(400,'Confirm your username and password.');
      if(!await passwordJob(()=>passwordMatches(b.password,me.salt,me.passwordHash)))fail(403,'Password is incorrect.');
      return legal.request(me.id);
    }
    const handled = await extra.route(req, path, url, me); if (handled !== undefined) return handled;
    if (req.method === 'GET' && path === '/v1/me') return { user: safeUser(me) };
    if (req.method === 'GET' && path === '/v1/events') return waitForChange(me.id, url.searchParams.get('cursor'), res);
    if (req.method === 'POST' && path === '/v1/logout') {
      db.prepare('DELETE FROM sessions WHERE tokenHash=?').run(hash(token));
      notify(me.id);
      return { ok: true };
    }
    if (req.method === 'GET' && path === '/v1/users') {
      limit('lookup:' + me.id, 30);
      if (!validUsername(url.searchParams.get('username'))) fail(400, usernameHint);
      const user = db.prepare('SELECT * FROM users WHERE username=?').get(cleanName(url.searchParams.get('username')));
      if (!user) fail(404, 'No account with that username.');
      return { user: safeUser(user) };
    }
    if (req.method === 'GET' && path === '/v1/chats') {
      const rows = db.prepare(`SELECT u.id,u.username,u.displayName,u.publicKey,MAX(m.id) AS lastId,MAX(m.createdAt) AS updatedAt,
        SUM(CASE WHEN m.recipientId=? AND m.senderId=u.id AND m.id>COALESCE(r.lastId,0) AND m.senderId<>m.recipientId AND m.removed='' THEN 1 ELSE 0 END) AS unread
        FROM messages m JOIN users u ON u.id=CASE WHEN m.senderId=? THEN m.recipientId ELSE m.senderId END
        LEFT JOIN reads r ON r.userId=? AND r.peerId=u.id
        WHERE m.senderId=? OR m.recipientId=? GROUP BY u.id ORDER BY lastId DESC LIMIT 100`).all(me.id, me.id, me.id, me.id, me.id);
      const last = db.prepare('SELECT id,senderId,recipientId,clientId,createdAt,expiresAt,viewOnce,removed,envelopeBytes,mediaBytes FROM messages WHERE id=?');
      const chats=[];let bytes=0;
      for(const row of rows){const stub=last.get(row.lastId),sealed=stub.viewOnce||stub.removed,large=stub.envelopeBytes>24000||stub.mediaBytes>0;
        const m=messageRow(sealed||large?{...stub,envelope:'{}'}:db.prepare('SELECT * FROM messages WHERE id=?').get(row.lastId));
        if(large&&!sealed){m.envelope=null;m.media=null;m.attachment=true;}
        const chat={...row,profile:extra.profile(row.id),lastMessage:m},size=Buffer.byteLength(JSON.stringify(chat));
        if(bytes+size>4000000&&chats.length)break;chats.push(chat);bytes+=size;
      }
      return {chats,notices:db.prepare('SELECT id,version,text,createdAt FROM notices ORDER BY id DESC LIMIT 20').all()};
    }
    if (req.method === 'GET' && path === '/v1/messages') {
      const peer = url.searchParams.get('peer');
      if (!getUser(peer)) fail(404, 'Account not found.');
      const before = Number(url.searchParams.get('before') || Number.MAX_SAFE_INTEGER);
      const after = Number(url.searchParams.get('after') || 0);
      if (!Number.isSafeInteger(before) || !Number.isSafeInteger(after) || before <= 0 || after < 0) fail(400, 'Invalid message cursor.');
      // Choose a page from small metadata first. Fetching/hydrating every
      // ciphertext before applying the byte budget can exceed Worker memory.
      const source = db.prepare(`SELECT id,senderId,recipientId,clientId,createdAt,expiresAt,viewOnce,removed,envelopeBytes,mediaBytes FROM messages WHERE ((senderId=? AND recipientId=?) OR (senderId=? AND recipientId=?)) AND id<? AND id>? ORDER BY id ${after ? 'ASC' : 'DESC'} LIMIT 100`).all(me.id, peer, peer, me.id, before, after);
      const rows = []; let pageBytes = 0;
      for (const row of source) { const sealed=row.viewOnce||row.removed||(row.expiresAt>0&&row.expiresAt<=now());const size = sealed ? 500 : row.envelopeBytes+(row.mediaBytes||0)+500; if (pageBytes + size > 8000000 && rows.length) break; rows.push(sealed?{...row,envelope:'{}',media:''}:db.prepare('SELECT * FROM messages WHERE id=?').get(row.id)); pageBytes += size; }
      if (!after) rows.reverse();
      const read = db.prepare('SELECT lastId FROM receipts WHERE userId=? AND peerId=?').get(peer, me.id);
      const removedIds = db.prepare("SELECT id FROM messages WHERE ((senderId=? AND recipientId=?) OR (senderId=? AND recipientId=?)) AND removed<>'' ORDER BY id DESC LIMIT 1000").all(me.id, peer, peer, me.id).map(x => x.id);
      return { messages: rows.map(messageRow), removedIds, ttlSeconds: extra.ttl(me.id, peer), hasMore: source.length === 100 || rows.length < source.length, readThrough: read?.lastId || 0 };
    }
    if (req.method === 'POST' && path === '/v1/read') {
      const b = await body(req);
      if (typeof b.peer !== 'string' || !Number.isSafeInteger(b.lastId) || b.lastId < 0) fail(400, 'Invalid read cursor.');
      // Bound the marker to actual messages in this conversation.
      const row = db.prepare('SELECT MAX(id) AS id FROM messages WHERE senderId=? AND recipientId=? AND id<=?').get(b.peer, me.id, b.lastId);
      const old = db.prepare('SELECT lastId FROM reads WHERE userId=? AND peerId=?').get(me.id, b.peer)?.lastId || 0;
      if ((row.id || 0) > old) {
        db.prepare('INSERT INTO reads VALUES(?,?,?) ON CONFLICT(userId,peerId) DO UPDATE SET lastId=MAX(lastId,excluded.lastId)').run(me.id, b.peer, row.id);
        notify(me.id);
      }
      const shared = db.prepare('SELECT lastId FROM receipts WHERE userId=? AND peerId=?').get(me.id, b.peer)?.lastId || 0;
      if (b.shareReceipts !== false && (row.id || 0) > shared) {
        db.prepare('INSERT INTO receipts VALUES(?,?,?) ON CONFLICT(userId,peerId) DO UPDATE SET lastId=MAX(lastId,excluded.lastId)').run(me.id, b.peer, row.id);
        if (b.peer !== me.id) notify(b.peer);
      }
      return { ok: true };
    }
    if (req.method === 'POST' && path === '/v1/messages') {
      limit('send:' + me.id, 90,60000,true);
      const b = await body(req, 8000000);
      if (JSON.stringify(b).length > 30000 && b.envelope?.v !== 2 && !b.media) fail(413, 'Request is too large.');
      if (typeof b.recipientId !== 'string' || !getUser(b.recipientId)) fail(404, 'Recipient not found.');
      if (typeof b.clientId !== 'string' || !/^[a-zA-Z0-9-]{16,64}$/.test(b.clientId)) fail(400, 'Invalid message identifier.');
      const envelope = validateEnvelope(b.envelope, me.id, b.recipientId, b.clientId, me.publicKey);
      const media = extra.validateMedia(b.media);
      const old = db.prepare('SELECT * FROM messages WHERE senderId=? AND clientId=?').get(me.id, b.clientId);
      if (old) {
        if (old.recipientId !== b.recipientId || (old.envelopeHash || hash(old.envelope)) !== hash(JSON.stringify(envelope)) || (old.media||'') !== (media?JSON.stringify(media):'')) fail(409, 'Message identifier already used.');
        return { message: messageRow(old) };
      }
      const p = extra.validateSend(me.id, b.recipientId, envelope);
      const encoded = JSON.stringify(envelope);
      const mediaEncoded = media ? JSON.stringify(media) : '', mediaBytes = media ? Buffer.byteLength(Buffer.from(media.data,'base64')) : 0;
      const used = db.prepare('SELECT COALESCE(SUM(envelopeBytes+mediaBytes),0) AS n FROM messages WHERE senderId=?').get(me.id).n;
      if (used + Buffer.byteLength(encoded) + mediaBytes > 64 * 1024 * 1024) fail(507, 'Relay storage limit reached. Wait for disappearing messages to expire or contact the host.');
      storage.require(Buffer.byteLength(encoded)+Buffer.byteLength(mediaEncoded)+1024);
      const createdAt = now();
      const result = db.prepare('INSERT INTO messages(senderId,recipientId,clientId,envelope,createdAt,expiresAt,viewOnce,envelopeHash,envelopeBytes,media,mediaBytes) VALUES(?,?,?,?,?,?,?,?,?,?,?)').run(me.id, b.recipientId, b.clientId, encoded, createdAt, p.ttlSeconds ? createdAt + p.ttlSeconds * 1000 : 0, p.viewOnce ? 1 : 0, hash(encoded), Buffer.byteLength(encoded),mediaEncoded,mediaBytes);
      notify(me.id);if (b.recipientId !== me.id) notify(b.recipientId);
      return { message: messageRow(db.prepare('SELECT * FROM messages WHERE id=?').get(Number(result.lastInsertRowid))) };
    }
    fail(404, 'Endpoint not found.');
  }
  const handler = async (req, res) => {
    req.setTimeout(15_000, () => req.destroy());
    try {
      if(webHandler(req,res))return;
      const result = await route(req, res);
      if (res.destroyed) return;
      res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' });
      res.end(JSON.stringify(result));
    } catch (e) {
      const status = e.status || 500;
      if (status === 500) console.error('Request failed:', e.message);
      if (!res.headersSent && !res.destroyed) {
        res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' });
        res.end(JSON.stringify({ error: status === 500 ? 'Server error. Try again.' : e.message }));
      }
    }
  };
  return { db, handler, authenticate, cursorFor, publishNotice, maintain() { extra.sweep(); maintain(); },
    close() { clearInterval(cleanup);clearInterval(expiry);for(const pending of [...watchers.values()])for(const finish of [...pending])finish(false); }
  };
}
