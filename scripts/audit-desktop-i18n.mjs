// Audit the Windows app: every user-visible string in Main.xaml and desktop/*.cs must
// exist in the translation catalog after T() normalization (prefix/suffix decoration).
// Usage: node scripts/audit-desktop-i18n.mjs [lang]
import { readFileSync, readdirSync } from 'node:fs';
import { translationCatalog } from '../web/i18n.mjs';

const root = new URL('../desktop/', import.meta.url);
const xaml = readFileSync(new URL('Main.xaml', root), 'utf8');
const csFiles = readdirSync(root).filter((f) => f.endsWith('.cs'));
const api = translationCatalog.english;
const norm = (value) => value.replace(/&#10;/g, " ").replace(/\s+/g, " ").trim().replace(/’/g, "'");
const english = new Set(api.map(norm));

// Mirrors T(): strip leading decorations and trailing ↗, trim.
const decorations = ['◇', '↓', '+', '＋', '●', '○', '⚪', '✓✓', '✓', '◷', '▶', '▧', '×', '◈', '•', '↑'];
function normalize(source) {
  let core = source.trim();
  let changed = true;
  while (changed) {
    changed = false;
    for (const d of decorations) if (core.startsWith(d)) { core = core.slice(d.length).trimStart(); changed = true; }
  }
  if (core.endsWith('↗')) core = core.slice(0, -1).trimEnd();
  return core;
}

const found = new Map(); // raw -> where
const add = (s, where) => {
  s = s.replace(/\\n/g, ' ').trim();
  if (!s || !/[A-Za-z]{2,}/.test(s)) return;
  if (/^(https?:|\/|#|\.|\*|data:)/.test(s)) return;
  if (s.includes('{0}') || s.includes('+') && s.trim().startsWith('+')) { /* keep, normalize handles */ }
  if (!found.has(s)) found.set(s, []);
  found.get(s).push(where);
};

// XAML attributes
for (const m of xaml.matchAll(/(Text|Content|ToolTip|Header)="([^"]*)"/g)) add(m[2], 'Main.xaml:' + m[1]);

// C# literals
for (const file of csFiles) {
  const src = readFileSync(new URL(file, root), 'utf8');
  const line = (index) => src.slice(0, index).split('\n').length;
  for (const m of src.matchAll(/Label\(\s*"((?:[^"\\]|\\.)*)"/g)) add(m[1], file + ':' + line(m.index));
  for (const m of src.matchAll(/(?:Content|Text|ToolTip|Header)\s*=\s*T\(\s*"((?:[^"\\]|\\.)*)"\s*\)/g)) add(m[1], file + ':' + line(m.index));
  for (const m of src.matchAll(/(?:Content|Text|ToolTip|Header)\s*=\s*"((?:[^"\\]|\\.)*)"/g)) add(m[1], file + ':' + line(m.index));
  for (const m of src.matchAll(/(?:Content|Text|ToolTip|Header)\s*=\s*"((?:[^"\\]|\\.)*)"\s*\+/g)) add(m[1], file + ':' + line(m.index) + ' (composed)');
}

const lang = process.argv[2] || 'ru';
const missing = [];
for (const [raw, where] of found) {
  const candidates = [raw, normalize(raw)];
  // also try without trailing dynamic tail markers
  if (!candidates.map(norm).some((c) => english.has(c))) missing.push([raw, where.join(', ')]);
}
console.log(`desktop strings checked: ${found.size}  missing from catalog: ${missing.length}  (lang ${lang})`);
const art = new Set(["Illusion","[ dream state / connected minds ]","ドリームステート / DREAM STATE","[ illusion / 001 ]"].map(norm));
const real = missing.filter(([raw]) => !art.has(norm(raw)));
console.log(`real gaps: ${real.length}`);
for (const [raw, where] of real) console.log(`  MISS ${JSON.stringify(raw)}  [${where}]`);
