import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { translate, translationCatalog } from '../web/i18n.mjs';

const style = readFileSync(new URL('../web/style.css', import.meta.url), 'utf8');
const app = readFileSync(new URL('../web/app.mjs', import.meta.url), 'utf8');
const appearance = readFileSync(new URL('../desktop/Appearance.cs', import.meta.url), 'utf8');

// Theme ids and labels declared in the web app.
const choices = JSON.parse(app.match(/const themeChoices=(\[.*?\]);/s)[1]);
const ids = choices.map(([id]) => id);

// Colors declared in web/style.css: :root is blue, every other theme needs a block.
const blockVars = (block) => Object.fromEntries([...block.matchAll(/--([\w-]+):\s*([^;]+)/g)].map((m) => [m[1], m[2].trim()]));
const cssThemes = { blue: blockVars(style.match(/:root\{([^}]*)\}/)[1]) };
for (const m of style.matchAll(/body\[data-theme=([\w-]+)\]\{([^}]*)\}/g)) cssThemes[m[1]] = { ...(cssThemes[m[1]] || {}), ...blockVars(m[2]) };

// Colors declared for Windows.
const winThemes = {};
for (const m of appearance.matchAll(/\{"(\w+)",new\[\]\{"(#[0-9A-Fa-f]{6})","(#[0-9A-Fa-f]{6})","(#[0-9A-Fa-f]{6})","(#[0-9A-Fa-f]{6})","(#[0-9A-Fa-f]{6})","(#[0-9A-Fa-f]{6})","(#[0-9A-Fa-f]{6})","(#[0-9A-Fa-f]{6})"\}\}/g)) {
  winThemes[m[1]] = [m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9]];
}

const luminance = (hex) => {
  const rgb = hex.replace('#', '').match(/../g).map((x) => parseInt(x, 16) / 255)
    .map((x) => (x <= 0.04045 ? x / 12.92 : ((x + 0.055) / 1.055) ** 2.4));
  return 0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2];
};
const contrast = (a, b) => {
  const x = luminance(a), y = luminance(b);
  return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05);
};

test('the web ships at least 15 distinct themes', () => {
  assert.ok(ids.length >= 15, `expected >=15 themes, found ${ids.length}`);
  assert.equal(new Set(ids).size, ids.length, 'theme ids must be unique');
});

test('every advertised theme has a stylesheet palette', () => {
  for (const id of ids) assert.ok(cssThemes[id], `missing CSS palette for theme ${id}`);
});

test('web and Windows palettes define the same themes', () => {
  assert.deepEqual(Object.keys(winThemes).sort(), [...ids].sort());
});

test('web and Windows agree on the core colors of every theme', () => {
  for (const id of ids) {
    const web = cssThemes[id], win = winThemes[id];
    // Windows palette: bg, panel, control, line, accent, onAccent, muted, highlight
    assert.equal(web.bg.toLowerCase(), win[0].toLowerCase(), `${id} background`);
    assert.equal(web.panel.toLowerCase(), win[1].toLowerCase(), `${id} panel`);
    assert.equal(web.control.toLowerCase(), win[2].toLowerCase(), `${id} control`);
    assert.equal(web.accent.toLowerCase(), win[4].toLowerCase(), `${id} accent`);
    assert.equal(web.muted.toLowerCase(), win[6].toLowerCase(), `${id} muted`);
  }
});

test('every theme keeps text readable (4.5:1 body, 4.5:1 accent button)', () => {
  const text = '#f1f5ff';
  for (const id of ids) {
    const vars = cssThemes[id];
    assert.ok(contrast(text, vars.bg) >= 4.5, `${id}: body text contrast ${contrast(text, vars.bg).toFixed(2)}`);
    assert.ok(contrast(vars.muted, vars.bg) >= 4.5, `${id}: muted text contrast ${contrast(vars.muted, vars.bg).toFixed(2)}`);
    assert.ok(contrast(vars['on-accent'], vars.accent) >= 4.5, `${id}: accent button contrast ${contrast(vars['on-accent'], vars.accent).toFixed(2)}`);
  }
});

test('every theme name is translated in all 28 non-English languages', () => {
  for (const [, label] of choices) {
    for (const [code] of translationCatalog.languages) {
      if (code === 'en') continue;
      assert.notEqual(translate(label, code), label, `${code} untranslated theme name: ${label}`);
    }
  }
});
