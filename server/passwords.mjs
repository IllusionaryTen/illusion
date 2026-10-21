import {scrypt,timingSafeEqual} from 'node:crypto';
import {promisify} from 'node:util';
const derive=promisify(scrypt),prefix='scrypt$32768$8$3$';
// OWASP's 32 MiB scrypt profile. Only these fixed parameters and the original
// legacy profile are accepted; stored strings cannot request arbitrary work.
const options={N:32768,r:8,p:3,maxmem:64*1024*1024};
export async function passwordHash(password,salt){return prefix+(await derive(password,salt,64,options)).toString('hex');}
export async function passwordMatches(password,salt,stored){
  const legacy=/^[a-f0-9]{128}$/.test(stored||''),encoded=legacy?stored:typeof stored==='string'&&stored.startsWith(prefix)?stored.slice(prefix.length):'';
  const candidate=await derive(password,salt,64,legacy?{}:options);
  return /^[a-f0-9]{128}$/.test(encoded)&&timingSafeEqual(candidate,Buffer.from(encoded,'hex'));
}
export const needsPasswordUpgrade=stored=>!stored.startsWith(prefix);
