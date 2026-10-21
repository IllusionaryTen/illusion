// Resolve only Cloudflare's own edge destinations through its HTTPS DNS service.
// This is process-local: it never changes Windows DNS/firewall settings or TLS checks.
export async function cloudflareEdges(fetcher=fetch) {
  async function query(name,type){const r=await fetcher('https://cloudflare-dns.com/dns-query?name='+encodeURIComponent(name)+'&type='+type,{headers:{Accept:'application/dns-json'},signal:AbortSignal.timeout(5000),redirect:'error'});if(!r.ok)throw Error('Cloudflare HTTPS DNS failed.');const b=await r.json();if(b.Status!==0 || !Array.isArray(b.Answer))throw Error('Cloudflare DNS had no edge answers.');return b.Answer;}
  const srv=await query('_v2-origintunneld._tcp.argotunnel.com','SRV');
  const names=[...new Set(srv.filter(x=>x.type===33).map(x=>x.data.trim().split(/\s+/)).filter(x=>x.length===4 && x[2]==='7844' && /^region[12]\.v2\.argotunnel\.com\.?$/.test(x[3])).map(x=>x[3].replace(/\.$/,'')))];
  if(!names.length)throw Error('Cloudflare did not return recognized tunnel destinations.');
  const edges=[];
  for(const name of names){const answers=await query(name,'A');for(const answer of answers)if(answer.type===1 && /^198\.41\.(192|200)\.\d{1,3}$/.test(answer.data) && Number(answer.data.split('.').at(-1))<256)edges.push(answer.data+':7844');}
  if(!edges.length)throw Error('Cloudflare did not return a recognized edge address.');
  return [...new Set(edges)];
}
