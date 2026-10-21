import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cloudflareEdges } from '../scripts/cloudflare-dns.mjs';
test('public tunnel resolves only verified Cloudflare edge destinations without system DNS changes',async()=>{
  const requests=[];const fetcher=async(url,options)=>{requests.push(url);assert.equal(options.redirect,'error');return {ok:true,json:async()=>url.includes('type=SRV')?{Status:0,Answer:[{type:33,data:'1 1 7844 region1.v2.argotunnel.com'},{type:33,data:'1 1 1234 evil.example'},{type:33,data:'1 1 7844 evil.example'}]}:{Status:0,Answer:[{type:1,data:'198.41.192.227'},{type:1,data:'127.0.0.1'},{type:1,data:'198.41.192.999'}]}};};
  assert.deepEqual(await cloudflareEdges(fetcher),['198.41.192.227:7844']);assert.equal(requests.length,2);assert.ok(requests.every(u=>u.startsWith('https://cloudflare-dns.com/')));
});
test('unrecognized or failing Cloudflare DNS answers fail closed',async()=>{
  for(const body of [{Status:2},{Status:0,Answer:[{type:33,data:'1 1 7844 evil.example'}]}])await assert.rejects(cloudflareEdges(async()=>({ok:true,json:async()=>body})));
});

test('both documented Cloudflare regions are usable while unrelated address ranges are rejected',async()=>{
  const fetcher=async url=>({ok:true,json:async()=>url.includes('type=SRV')?
    {Status:0,Answer:[{type:33,data:'1 1 7844 region1.v2.argotunnel.com'},{type:33,data:'1 1 7844 region2.v2.argotunnel.com'}]}:
    {Status:0,Answer:url.includes('region1')?[{type:1,data:'198.41.192.27'}]:[{type:1,data:'198.41.200.13'},{type:1,data:'198.41.193.13'},{type:1,data:'203.0.113.1'}]}});
  assert.deepEqual(await cloudflareEdges(fetcher),['198.41.192.27:7844','198.41.200.13:7844']);
});
