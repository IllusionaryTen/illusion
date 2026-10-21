import { EventEmitter } from 'node:events';

export async function relayResponse(request, handler) {
  const responseEvents = new EventEmitter();
  const url = new URL(request.url);
  const req = {
    method: request.method, url: url.pathname + url.search,
    headers: Object.fromEntries(request.headers),
    socket: { remoteAddress: request.headers.get('x-illusion-client-ip') || 'unknown' },
    setTimeout() {},
    async *[Symbol.asyncIterator]() {
      if (!request.body) return;
      const reader = request.body.getReader();
      let timer;const timeout=new Promise((_,reject)=>{timer=setTimeout(()=>{reader.cancel().catch(()=>{});reject(Object.assign(Error('Request body timed out.'),{status:408}));},15000);});
      try { for (;;) { const { done, value } = await Promise.race([reader.read(),timeout]); if (done) break; yield Buffer.from(value); } }
      finally { clearTimeout(timer);reader.cancel().catch(()=>{});reader.releaseLock(); }
    }
  };
  return new Promise((resolve, reject) => {
    const res = Object.assign(responseEvents, {
      destroyed: false, headersSent: false, status: 200, headers: {},
      writeHead(status, headers) { this.status = status; this.headers = headers; this.headersSent = true; },
      end(body = '') { resolve(new Response(body, { status: this.status, headers: this.headers })); this.emit('close'); }
    });
    Promise.resolve(handler(req, res)).catch(reject);
  });
}
