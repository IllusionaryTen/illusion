import {Miniflare,convertV4MiniflareOptions} from 'miniflare';
import {resolve} from 'node:path';
export const testAdmin='synthetic-local-qa-admin-token-not-a-production-secret';
export async function openTestRegistration(worker){const r=await worker.dispatchFetch('http://localhost/admin/open-registration',{method:'POST',headers:{Authorization:'Bearer '+testAdmin,'Content-Type':'application/json'},body:'{}'});if(r.status!==200)throw Error('Could not open isolated test registration.');}
export function localWorker(dir){
  const options=convertV4MiniflareOptions({name:'illusion-test',modules:true,scriptPath:resolve('build/cloudflare-bundle/worker.js'),compatibilityDate:'2026-10-12',compatibilityFlags:['nodejs_compat'],durableObjects:{RELAY:{className:'IllusionRelay',useSQLite:true}},ratelimits:{AUTH_LIMIT:{namespace_id:'7606001',simple:{limit:15,period:60}},LIVE_LIMIT:{namespace_id:'7606002',simple:{limit:30,period:60}}},durableObjectsPersist:dir,bindings:{ADMIN_TOKEN:testAdmin},assets:{directory:resolve('build/cloud-assets'),binding:'ASSETS',run_worker_first:['/','/v1/*','/health','/policies/*','/admin/*','/telegram/*']}});
  options.telemetry={enabled:false};options.resourcePersistencePath=dir;options.unsafeInspectDurableObjects=true;options.workers[0].config.exports.default={type:'worker'};options.workers[0].config.assets.hasUserWorker=true;
  return new Miniflare(options);
}
