import {documents, operator, fonts, legalVersion} from '../server/legal.mjs';
const escape = s => String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
export const securityHeaders = {
  'Cache-Control':'no-store', 'X-Content-Type-Options':'nosniff', 'Referrer-Policy':'no-referrer',
  'Permissions-Policy':'camera=(), microphone=(self), geolocation=()',
  'Content-Security-Policy':"default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; img-src 'self' data: blob:; media-src 'self' blob:; font-src 'self'; manifest-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'self' https://web.telegram.org https://*.telegram.org"
};
const siteHeaders = {
  'Cache-Control':'public, max-age=0, must-revalidate', 'X-Content-Type-Options':'nosniff', 'Referrer-Policy':'no-referrer',
  'X-Frame-Options':'DENY', 'Permissions-Policy':'camera=(), microphone=(), geolocation=()',
  'Content-Security-Policy':"default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; font-src 'self'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'"
};
export async function publicResponse(request, env) {
  const url = new URL(request.url), path = url.pathname;
  if (path === '/') {
    if (request.method !== 'GET' && request.method !== 'HEAD') return null;
    const assetUrl = new URL(request.url); assetUrl.pathname = '/site/';
    const response = await env.ASSETS.fetch(new Request(assetUrl, request));
    if (response.status === 304) {
      const headers = new Headers(response.headers);
      for (const [k,v] of Object.entries(siteHeaders)) headers.set(k,v);
      return new Response(null, {status: 304, headers});
    }
    if (!response.ok) return new Response('Landing page is not built in this deployment.', {status: 503, headers: {...siteHeaders, 'Cache-Control': 'no-store'}});
    const headers = new Headers(response.headers); for (const [k,v] of Object.entries(siteHeaders)) headers.set(k,v);
    return new Response(request.method === 'HEAD' ? null : response.body, {status: 200, headers});
  }
  if (request.method !== 'GET') return null;
  if (path === '/v1/public') return Response.json({policyVersion:legalVersion, minimumAge:18, operator:operator(), fonts, policies:Object.keys(documents()), optionalTracking:false, price:0, liveTransport:'websocket'}, {headers:securityHeaders});
  const match = path.match(/^\/(v1\/legal|policies)\/([a-z]+)$/);
  if (match) {
    const docs = documents(), id = match[2]; if (!Object.hasOwn(docs,id)) return new Response('Not found',{status:404});
    if (match[1] === 'v1/legal') return Response.json({version:legalVersion,title:id,text:docs[id]},{headers:securityHeaders});
    return new Response(`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Illusion · ${escape(id)}</title><link rel="stylesheet" href="/app/style.css"><main class="policy"><a href="/app/">Back to Illusion</a><h1>${escape(id)}</h1><div class="document">${escape(docs[id])}</div></main></html>`,{headers:{...securityHeaders,'Content-Type':'text/html; charset=utf-8'}});
  }
  const asset = new Map([['/app','/index.html'],['/app/','/index.html'],['/app/app.mjs','/app.mjs'],['/app/crypto.mjs','/crypto.mjs'],['/app/security.mjs','/security.mjs'],['/app/request-signal.mjs','/request-signal.mjs'],['/app/style.css','/style.css'],['/app/i18n.mjs','/i18n.mjs'],['/app/theme-file.mjs','/theme-file.mjs'],['/app/manifest.webmanifest','/manifest.webmanifest'],['/app/sw.js','/sw.js'],['/app/icons/icon-192.png','/icons/icon-192.png'],['/app/icons/icon-512.png','/icons/icon-512.png'],['/android','/android/index.html'],['/android/','/android/index.html']]).get(path);
  if (asset) { url.pathname = asset; const response = await env.ASSETS.fetch(new Request(url,request)); const headers = new Headers(response.headers); for(const [k,v] of Object.entries(securityHeaders))headers.set(k,v); return new Response(response.body,{status:response.status,headers}); }
  return null;
}
