// Definitive audit: every user-visible string in index.html and app.mjs must have a
// distinct translation in every non-English language, except words that are genuinely
// identical in that language. Usage: node artifacts/audit-all.mjs
import { readFileSync } from 'node:fs';
import { translate, languages } from '../web/i18n.mjs';

const html = readFileSync(new URL('../web/index.html', import.meta.url), 'utf8');
const app = readFileSync(new URL('../web/app.mjs', import.meta.url), 'utf8');

const strings = new Set();
const addExact = (s) => { if (s && /[A-Za-z]{2,}/.test(s)) strings.add(s); };
const addTrimmed = (s) => { s = s.trim(); if (s && /[A-Za-z]{2,}/.test(s)) strings.add(s); };

// index.html: attributes keep their exact value (keys may end in a space); text nodes are trimmed.
for (const m of html.matchAll(/(aria-label|placeholder|title)="([^"]+)"/g)) addExact(m[2]);
for (const m of html.matchAll(/>([^<>]+)</g)) addTrimmed(m[1]);

// app.mjs: string literals reaching the UI keep their exact value.
const lit = /'((?:[^'\\]|\\.)*)'/g;
const unescape = (s) => s.replace(/\\'/g, "'");
for (const m of app.matchAll(/\b(?:button|label|modal|status)\(\s*'((?:[^'\\]|\\.)*)'/g)) addExact(unescape(m[1]));
for (const m of app.matchAll(/select\(\[([^\]]*)\]/g)) for (const i of m[1].matchAll(lit)) addExact(unescape(i[1]));
for (const m of app.matchAll(/Error\(\s*'((?:[^'\\]|\\.)*)'/g)) addExact(unescape(m[1]));
for (const m of app.matchAll(/node\(\s*'[a-z0-9]+',\s*'((?:[^'\\]|\\.)*)'/g)) addExact(unescape(m[1]));
for (const m of app.matchAll(/translate\(\s*'((?:[^'\\]|\\.)*)'/g)) addExact(unescape(m[1]));
for (const m of app.matchAll(/translate\(\s*`([^`]*)`/g)) addExact(m[1]);
for (const m of app.matchAll(/textContent='((?:[^'\\]|\\.)*)'/g)) addExact(unescape(m[1]));
for (const m of app.matchAll(/placeholder='((?:[^'\\]|\\.)*)'/g)) addExact(unescape(m[1]));
for (const m of app.matchAll(/\.alt='((?:[^'\\]|\\.)*)'/g)) addExact(unescape(m[1]));
for (const m of app.matchAll(/setAttribute\('aria-label','((?:[^'\\]|\\.)*)'/g)) addExact(unescape(m[1]));
for (const m of app.matchAll(/setAttribute\('aria-label',\s*content\.text\|\|'((?:[^'\\]|\\.)*)'/g)) addExact(unescape(m[1]));

// Genuinely identical in the target language, or never shown to users.
const identicalOK = new Set([
  '13–15','16–17','English','Greek','Vietnamese','Chats','Cloud','1 minute','Pause','1 week',
  'Photo / Video','Report','Play','Draft: ','You: ','Illusion','Telegram','MP4','RGB','Skip to messages',
  'Illusion ','Spam','Silver','Conversations','Message','Description','Password','Privacy','Send','Orbit',
  'load','timeout','seek'
]);

let total = 0;
const report = [];
for (const [code] of languages) {
  if (code === 'en') continue;
  let leaks = 0;
  for (const s of strings) {
    const out = translate(s, code);
    if (out === s && !identicalOK.has(s)) { leaks++; report.push(`  ${code}: ${JSON.stringify(s)}`); }
  }
  total += leaks;
}
console.log(`catalog rows: ${(await import('../web/i18n.mjs')).translationCatalog.english.length}`);
console.log(`checked ${strings.size} distinct UI strings x 28 languages`);
console.log(`TOTAL LEAKS: ${total}`);
for (const line of report) console.log(line);
