import {mkdirSync,copyFileSync,writeFileSync,existsSync,readFileSync,readdirSync,unlinkSync} from 'node:fs';
import {resolve,join} from 'node:path';
import {createHash} from 'node:crypto';
import {securityHeaders} from '../cloudflare/public.mjs';
import {loadRelease} from '../bot/index.mjs';
const root=resolve('build/cloud-assets');mkdirSync(join(root,'app/icons'),{recursive:true});mkdirSync(join(root,'android'),{recursive:true});
const appFiles=['index.html','app.mjs','crypto.mjs','security.mjs','request-signal.mjs','i18n.mjs','theme-file.mjs','reactions.mjs','style.css','mobile.css','manifest.webmanifest','sw.js'];
const allowed=new Set(['_headers','android/index.html',...appFiles.map(n=>'app/'+n),'app/icons/icon-192.png','app/icons/icon-512.png','app/sounds/illusionsfx.mp3']);
for(const name of appFiles)copyFileSync(join('web',name),join(root,'app',name));
mkdirSync(join(root,'app/sounds'),{recursive:true});copyFileSync(join('web/sounds/illusionsfx.mp3'),join(root,'app/sounds/illusionsfx.mp3'));
for(const name of ['icon-192.png','icon-512.png'])copyFileSync(join('web/icons',name),join(root,'app/icons',name));
copyFileSync(join('web/android.html'),join(root,'android/index.html'));
writeFileSync(join(root,'_headers'),'/app/*\n'+Object.entries(securityHeaders).map(([k,v])=>'  '+k+': '+v).join('\n')+'\n\n/android/*\n'+Object.entries(securityHeaders).map(([k,v])=>'  '+k+': '+v).join('\n')+'\n');
if(existsSync('dist/release.json')){
  const release=loadRelease('dist/release.json');mkdirSync(join(root,'download'),{recursive:true});
  for(const file of readdirSync(join(root,'download')))if(/^Illusion(?:-\d+\.\d+\.\d+)?\.exe$/.test(file)&&file!==release.file)unlinkSync(join(root,'download',file));
  for(const name of [release.file,'Illusion-Connection.illusion','Illusion-Windows.zip','release.json'])copyFileSync(join('dist',name),join(root,'download',name));
  for(const name of [release.file,'Illusion-Connection.illusion','Illusion-Windows.zip','release.json'])allowed.add('download/'+name);
  writeFileSync(join(root,'_headers'),readFileSync(join(root,'_headers'),'utf8')+'\n/download/*\n  X-Content-Type-Options: nosniff\n  Strict-Transport-Security: max-age=31536000; includeSubDomains\n  Content-Disposition: attachment\n  Cache-Control: no-cache\n');
}
if(existsSync('dist/android-release.json')){
  const release=JSON.parse(readFileSync('dist/android-release.json','utf8').replace(/^\uFEFF/,''));
  if(!/^\d+\.\d+\.\d+$/.test(release.version)||release.file!=='Illusion-'+release.version+'.apk'||!Number.isSafeInteger(release.bytes)||release.bytes<=0||release.bytes>50_000_000||!/^[a-f0-9]{64}$/.test(release.sha256))throw Error('Android release manifest is invalid.');
  const apk=readFileSync(join('dist',release.file));if(apk.length!==release.bytes||createHash('sha256').update(apk).digest('hex')!==release.sha256)throw Error('Android APK does not match its release manifest.');
  const dir=join(root,'download/android');mkdirSync(dir,{recursive:true});
  for(const name of readdirSync(dir))if(/^Illusion-\d+\.\d+\.\d+\.apk$/.test(name)&&name!==release.file)unlinkSync(join(dir,name));
  copyFileSync(join('dist',release.file),join(dir,release.file));writeFileSync(join(dir,'release.json'),JSON.stringify(release)+'\n');
  allowed.add('download/android/'+release.file);allowed.add('download/android/release.json');
  writeFileSync(join(root,'_headers'),readFileSync(join(root,'_headers'),'utf8')+'\n/download/android/*\n  X-Content-Type-Options: nosniff\n  Strict-Transport-Security: max-age=31536000; includeSubDomains\n  Content-Disposition: attachment\n  Cache-Control: no-cache\n  Content-Type: application/vnd.android.package-archive\n');
}
// Public landing page: HTML is served at / by cloudflare/public.mjs; styles, script and SEO files are static assets.
mkdirSync(join(root,'site'),{recursive:true});
for(const name of ['index.html','site.css','site.js'])copyFileSync(join('web/site',name),join(root,'site',name));
for(const name of ['robots.txt','sitemap.xml','favicon.ico'])copyFileSync(join('web/site',name),join(root,name));
allowed.add('site/index.html');allowed.add('site/site.css');allowed.add('site/site.js');allowed.add('robots.txt');allowed.add('sitemap.xml');allowed.add('favicon.ico');
if(existsSync('web/site/og.png')){copyFileSync('web/site/og.png',join(root,'og.png'));allowed.add('og.png');}
writeFileSync(join(root,'_headers'),readFileSync(join(root,'_headers'),'utf8')+'\n/site/*\n  Cache-Control: public, max-age=300\n  X-Content-Type-Options: nosniff\n  Referrer-Policy: no-referrer\n  Strict-Transport-Security: max-age=31536000; includeSubDomains\n  X-Frame-Options: DENY\n  Permissions-Policy: camera=(), microphone=(), geolocation=()\n  Content-Security-Policy: default-src \'none\'; script-src \'self\'; style-src \'self\'; img-src \'self\' data:; connect-src \'self\'; font-src \'self\'; base-uri \'none\'; form-action \'self\'; frame-ancestors \'none\'\n\n/robots.txt\n  Cache-Control: public, max-age=3600\n\n/sitemap.xml\n  Cache-Control: public, max-age=3600\n'+(existsSync('web/site/og.png')?'\n/og.png\n  Cache-Control: public, max-age=3600\n':''));
function audit(dir,prefix=''){for(const item of readdirSync(dir,{withFileTypes:true})){const relative=prefix+item.name;if(item.isDirectory())audit(join(dir,item.name),relative+'/');else if(!item.isFile()||!allowed.has(relative))throw Error('Unexpected staged asset: '+relative+'. Review and remove it before publishing.');}}
audit(root);
console.log('Prepared Mini App assets; no secrets, relay database or .env included.');
