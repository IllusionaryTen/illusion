import {DurableObject} from 'cloudflare:workers';
import {createHash} from 'node:crypto';
import {secretMatches} from '../shared/secrets.mjs';
import {createRelay} from '../server/relay.mjs';
import {fulfillDeletion} from '../server/legal.mjs';
import {CloudDatabase} from './database.mjs';
import {relayResponse} from './bridge.mjs';
import {publicResponse,securityHeaders} from './public.mjs';
import {importSnapshot} from './migration.mjs';
import {telegramWebhook} from '../bot/webhook.mjs';
import {createGmailSender} from './gmail.mjs';

export default {
  async fetch(request,env) {
    const url=new URL(request.url);
    if(url.pathname==='/telegram/webhook'&&!secretMatches(request.headers.get('X-Telegram-Bot-Api-Secret-Token'),env.TELEGRAM_WEBHOOK_SECRET))return new Response('Not authorized',{status:401,headers:securityHeaders});
    if(!['GET','HEAD'].includes(request.method)&&request.headers.has('Origin')&&request.headers.get('Origin')!==url.origin)return new Response('Origin rejected',{status:403,headers:securityHeaders});
    const visible = await publicResponse(request,env); if(visible)return visible;
    const ip=request.headers.get('CF-Connecting-IP')||'unknown';
    if(url.pathname.startsWith('/admin/')){
      if(!env.ADMIN_TOKEN||env.ADMIN_TOKEN.length<24)return Response.json({error:'Admin access is disabled until a strong token is configured.'},{status:503,headers:securityHeaders});
      if(env.ADMIN_LIMIT&&!(await env.ADMIN_LIMIT.limit({key:'admin:'+ip})).success)return Response.json({error:'Too many admin attempts. Try again later.'},{status:429,headers:securityHeaders});
      if(!secretMatches(request.headers.get('Authorization')?.replace(/^Bearer /,''),env.ADMIN_TOKEN))return Response.json({error:'Not authorized'},{status:401,headers:securityHeaders});
    }
    if(env.API_LIMIT&&!(await env.API_LIMIT.limit({key:ip})).success)return Response.json({error:'Too many requests. Please retry shortly.'},{status:429,headers:securityHeaders});
    const limiter=['/v1/register','/v1/login'].includes(url.pathname)?env.AUTH_LIMIT:url.pathname==='/v1/live'?env.LIVE_LIMIT:null;
    if(limiter && !(await limiter.limit({key:ip})).success)return Response.json({error:'Too many attempts. Please wait a minute.'},{status:429,headers:securityHeaders});
    if(url.pathname!=='/health' && !url.pathname.startsWith('/v1/') && !url.pathname.startsWith('/admin/') && url.pathname!=='/telegram/webhook')return new Response('Not found',{status:404});
    // Replace any client-supplied internal routing header with Cloudflare's IP.
    const headers=new Headers(request.headers);headers.set('x-illusion-client-ip',request.headers.get('CF-Connecting-IP')||'unknown');
    return env.RELAY.get(env.RELAY.idFromName('illusion-main-v1')).fetch(new Request(request,{headers}));
  }
};

export class IllusionRelay extends DurableObject {
  constructor(ctx,env) {
    super(ctx,env);this.env=env;this.ctx=ctx;
    this.db=new CloudDatabase(ctx.storage);
    const gmailReady=Boolean(env.EMAIL_CODE_KEY&&env.EMAIL_CODE_KEY.length>=32&&env.GMAIL_CLIENT_ID&&env.GMAIL_CLIENT_SECRET&&env.GMAIL_REFRESH_TOKEN&&env.GMAIL_SENDER);
    this.relay=createRelay({db:this.db,registrationCode:env.REGISTRATION_CODE||'',timers:false,persistentLimits:true,liveTransport:'websocket',onNotify:(user,cursor)=>this.notify(user,cursor),sendEmail:createGmailSender(env),emailCodeKey:env.EMAIL_CODE_KEY||'',emailRecoveryAvailable:gmailReady});
    this.db.enableChunks();
    this.db.exec('CREATE TABLE IF NOT EXISTS cloud_settings(key TEXT PRIMARY KEY,value TEXT NOT NULL); CREATE TABLE IF NOT EXISTS bot_updates(id INTEGER PRIMARY KEY,status TEXT NOT NULL,createdAt INTEGER NOT NULL)');
    ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair('ping','pong'));
  }
  notify(user,cursor) {
    for(const socket of this.ctx.getWebSockets()) {
      const a=socket.deserializeAttachment();if(a?.userId!==user)continue;
      try {if(!this.validSocket(a)){socket.close(4001,'Session expired');continue;}socket.send(JSON.stringify({type:'changed',cursor}));}catch{}
    }
  }
  validSocket(a) {
    return a?.userId && a.expiresAt>Date.now() && this.db.prepare('SELECT 1 FROM sessions WHERE tokenHash=? AND userId=? AND expiresAt>?').get(a.tokenHash,a.userId,Date.now()) && !this.db.prepare('SELECT 1 FROM bans WHERE userId=?').get(a.userId);
  }
  async fetch(request) {
    const url=new URL(request.url);
    if(url.pathname==='/telegram/webhook' && request.method==='POST'){const response=await telegramWebhook(request,{env:this.env,db:this.db,readJson:(r,n)=>this.readJson(r,n)});await this.scheduleAlarm();return response;}
    if(url.pathname==='/v1/live' && request.headers.get('Upgrade')?.toLowerCase()==='websocket') {
      if(request.headers.has('Origin') && request.headers.get('Origin')!==url.origin)return new Response('Origin rejected',{status:403});
      if(this.ctx.getWebSockets().length>=200)return new Response('Live connection limit reached',{status:429});
      const ipHash=createHash('sha256').update(request.headers.get('x-illusion-client-ip')||'unknown').digest('hex'),pending=this.ctx.getWebSockets().map(s=>s.deserializeAttachment()).filter(a=>!a?.userId);
      if(pending.length>=20||pending.filter(a=>a?.ipHash===ipHash).length>=2)return new Response('Pending live connection limit reached',{status:429});
      const pair=new WebSocketPair(),[client,server]=Object.values(pair);
      this.ctx.acceptWebSocket(server);server.serializeAttachment({deadline:Date.now()+15000,ipHash});
      const alarm=await this.ctx.storage.getAlarm();if(alarm===null || alarm>Date.now()+15000)await this.ctx.storage.setAlarm(Date.now()+15000);
      return new Response(null,{status:101,webSocket:client});
    }
    if(url.pathname.startsWith('/admin/'))return this.admin(request,url.pathname);
    if(url.pathname==='/v1/register' && this.db.prepare("SELECT value FROM cloud_settings WHERE key='registration_open'").get()?.value!=='true')return Response.json({error:'Hosted setup is not finished yet. Please try again later.'},{status:503,headers:securityHeaders});
    const response=await relayResponse(request,this.relay.handler);
    if(request.method!=='GET')await this.scheduleAlarm();
    const headers=new Headers(response.headers);for(const [k,v]of Object.entries(securityHeaders))headers.set(k,v);
    return new Response(response.body,{status:response.status,headers});
  }
  async webSocketMessage(socket,message) {
    try {
      if(typeof message!=='string' || message.length>1024)throw Error('Invalid live message');
      const a=socket.deserializeAttachment();
      if(a?.userId){if(!this.validSocket(a))throw Error('Session expired');return;}
      if(!a || a.deadline<Date.now())throw Error('Authentication timed out');
      const b=JSON.parse(message);if(b.type!=='authenticate' || !/^[a-f0-9]{64}$/.test(b.token||''))throw Error('Authentication required');
      const {user,session}=this.relay.authenticate(b.token);
      if(this.ctx.getWebSockets().filter(s=>s.deserializeAttachment()?.userId===user.id).length>=5)throw Error('Too many live connections');
      socket.serializeAttachment({userId:user.id,tokenHash:session.tokenHash,expiresAt:session.expiresAt});
      socket.send(JSON.stringify({type:'ready',cursor:this.relay.cursorFor(user.id)}));
    }catch{socket.close(4001,'Live authentication failed');}
  }
  webSocketClose(socket,code,reason){try{socket.close(code===1005?1000:code,reason);}catch{}}
  webSocketError(socket){try{socket.close(1011,'Live connection failed');}catch{}}
  async scheduleAlarm() {
    const next=this.db.prepare("SELECT MIN(expiresAt) AS time FROM messages WHERE removed='' AND expiresAt>0").get()?.time;
    const when=Math.max(Date.now()+1000,Math.min(next||Infinity,Date.now()+3600000));
    const current=await this.ctx.storage.getAlarm();if(current===null || current>when)await this.ctx.storage.setAlarm(when);
  }
  async alarm(){this.relay.maintain();this.db.prepare('DELETE FROM bot_updates WHERE createdAt<?').run(Date.now()-86400000);for(const s of this.ctx.getWebSockets()){const a=s.deserializeAttachment();if(a?.deadline<Date.now() || a?.userId&&!this.validSocket(a))try{s.close(4001,'Session expired');}catch{}}await this.scheduleAlarm();}
  async admin(request,path) {
    const token=request.headers.get('Authorization')?.replace(/^Bearer /,'')||'';
    // Admin credentials are separate from Telegram and never appear in downloads.
    if(token.length<32||!secretMatches(token,this.env.ADMIN_TOKEN))return Response.json({error:'Not authorized'},{status:401,headers:securityHeaders});
    if(request.method==='POST' && path==='/admin/open-registration'){this.db.prepare("INSERT OR REPLACE INTO cloud_settings VALUES('registration_open','true')").run();return Response.json({ok:true},{headers:securityHeaders});}
    if(request.method==='POST' && path==='/admin/close-registration'){this.db.prepare("INSERT OR REPLACE INTO cloud_settings VALUES('registration_open','false')").run();return Response.json({ok:true},{headers:securityHeaders});}
    if(request.method==='GET' && path==='/admin/status')return Response.json({counts:Object.fromEntries(['users','messages','profiles','reports'].map(t=>[t,this.db.prepare('SELECT COUNT(*) AS n FROM '+t).get().n])),registrationOpen:this.db.prepare("SELECT value FROM cloud_settings WHERE key='registration_open'").get()?.value==='true',importHash:this.db.prepare("SELECT value FROM cloud_settings WHERE key='import_hash'").get()?.value||null},{headers:securityHeaders});
    if(request.method==='POST' && path==='/admin/import'){
      try{const b=await this.readJson(request,8000000),result=importSnapshot(this.db,b);await this.scheduleAlarm();return Response.json(result,{headers:securityHeaders});}catch(e){return Response.json({error:e.message},{status:400,headers:securityHeaders});}
    }
    if(request.method==='GET' && path==='/admin/reports')return Response.json({reports:this.db.prepare("SELECT * FROM reports WHERE status='open' ORDER BY id DESC LIMIT 100").all()},{headers:securityHeaders});
    if(request.method==='GET' && path==='/admin/deletions')return Response.json({requests:this.db.prepare("SELECT d.id,d.createdAt,u.username FROM deletion_requests d JOIN users u ON u.id=d.userId WHERE d.status='pending' ORDER BY d.id LIMIT 100").all()},{headers:securityHeaders});
    if(request.method==='POST' && path==='/admin/report'){
      try{const b=await this.readJson(request,2048);if(!Number.isSafeInteger(b.id)||!['resolved','dismissed'].includes(b.status))throw Error('Choose a report and status.');this.db.prepare('UPDATE reports SET status=? WHERE id=?').run(b.status,b.id);return Response.json({ok:true},{headers:securityHeaders});}catch(e){return Response.json({error:e.message},{status:400,headers:securityHeaders});}}
    if(request.method==='POST' && path==='/admin/ban'){
      try{const b=await this.readJson(request,2048);if(typeof b.username!=='string'||typeof b.banned!=='boolean')throw Error('Choose an account and action.');const u=this.db.prepare('SELECT id FROM users WHERE username=?').get(b.username.toLowerCase());if(!u)throw Error('Account not found.');if(b.banned){this.db.prepare('INSERT OR IGNORE INTO bans VALUES(?,?)').run(u.id,Date.now());this.db.prepare('DELETE FROM sessions WHERE userId=?').run(u.id);}else this.db.prepare('DELETE FROM bans WHERE userId=?').run(u.id);this.notify(u.id,'moderation');return Response.json({ok:true},{headers:securityHeaders});}catch(e){return Response.json({error:e.message},{status:400,headers:securityHeaders});}}
    if(request.method==='POST' && path==='/admin/notice'){
      try{const b=await this.readJson(request,2048);const notice=this.relay.publishNotice(String(b.version||''),String(b.text||''));return Response.json({ok:true,notice},{headers:securityHeaders});}catch(e){return Response.json({error:e.message},{status:400,headers:securityHeaders});}}
    if(request.method==='POST' && path==='/admin/fulfill-deletion'){
      try{const b=await this.readJson(request,2048);const row=this.db.prepare('SELECT userId FROM deletion_requests WHERE id=?').get(b.id);const result=fulfillDeletion(this.db,b.id,b.username);if(row)this.notify(row.userId,'deleted');return Response.json(result,{headers:securityHeaders});}catch(e){return Response.json({error:e.message},{status:400,headers:securityHeaders});}
    }
    return Response.json({error:'Endpoint not found'},{status:404,headers:securityHeaders});
  }
  async readJson(request,maximum){let size=0;const chunks=[];if(request.body)for await(const bytes of request.body){size+=bytes.length;if(size>maximum)throw Error('Request too large.');chunks.push(Buffer.from(bytes));}return JSON.parse(Buffer.concat(chunks).toString('utf8'));}
}
