import http from 'node:http';
import https from 'node:https';
import {DatabaseSync} from 'node:sqlite';
import {mkdirSync,readFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {pathToFileURL} from 'node:url';
import {createRelay} from './relay.mjs';
import {publicWeb} from './web.mjs';
export {signatureText} from './relay.mjs';
export function createMessenger({dataDir=process.env.DATA_DIR||'./data',registrationCode=process.env.REGISTRATION_CODE||'',cert,key,now=Date.now,sendEmail=null,emailCodeKey=''}={}) {
  mkdirSync(dataDir,{recursive:true});const db=new DatabaseSync(resolve(dataDir,'illusion.sqlite'));
  const relay=createRelay({db,registrationCode,now,webHandler:publicWeb,sendEmail,emailCodeKey});
  const server=cert&&key?https.createServer({cert,key},relay.handler):http.createServer(relay.handler);
  server.requestTimeout=15000;server.headersTimeout=10000;server.maxConnections=100;
  return {server,db,async close(){relay.close();await new Promise(r=>server.close(r));db.close();}};
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  if (Boolean(process.env.TLS_CERT) !== Boolean(process.env.TLS_KEY)) throw new Error('Set both TLS_CERT and TLS_KEY.');
  const app = createMessenger({ cert: process.env.TLS_CERT && readFileSync(process.env.TLS_CERT), key: process.env.TLS_KEY && readFileSync(process.env.TLS_KEY) });
  const host = process.env.HOST || '127.0.0.1';
  const port = Number(process.env.PORT || 3789);
  app.server.listen(port, host, () => console.log(`Illusion 0.6.1 listening on ${process.env.TLS_CERT ? 'https' : 'http'}://${host}:${port}\nMessage bodies are encrypted on the desktop. Keep this process running to relay messages.`));
  for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => app.close().then(() => process.exit(0)));
}
