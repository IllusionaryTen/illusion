import test from 'node:test';
import assert from 'node:assert/strict';
import { LOOK_VERSION, validateLookFile } from '../web/theme-file.mjs';

const options = { themes: ['blue', 'astraios', 'crimson-moon'], fonts: ['Segoe UI', 'Arial', 'Georgia'] };
const valid = { version: LOOK_VERSION, theme: 'astraios', font: 'Georgia', textColor: '#F1F5FF', accent: '#467AFF' };

test('a well-formed look file is accepted and normalized', () => {
  assert.deepEqual(validateLookFile(valid, options), { ...valid, textColor: '#f1f5ff', accent: '#467aff' });
});

test('unknown themes and fonts are rejected', () => {
  assert.throws(() => validateLookFile({ ...valid, theme: 'injected-theme' }, options), /unknown theme/);
  assert.throws(() => validateLookFile({ ...valid, font: 'Bad}/{}' }, options), /unknown font/);
});

test('colours must be six-digit hex values', () => {
  for (const textColor of ['red', '#fff', '#12345g', 'url(javascript:1)', '#1234567', '']) {
    assert.throws(() => validateLookFile({ ...valid, textColor }, options), /invalid textColor/);
  }
  assert.throws(() => validateLookFile({ ...valid, accent: 'rgb(0,0,0)' }, options), /invalid accent/);
});

test('version and shape are enforced', () => {
  assert.throws(() => validateLookFile({ ...valid, version: 2 }, options), /unsupported/);
  assert.throws(() => validateLookFile({ ...valid, version: '1' }, options), /unsupported/);
  assert.throws(() => validateLookFile(null, options), /invalid/);
  assert.throws(() => validateLookFile([valid], options), /invalid/);
});

test('extra fields are ignored', () => {
  const result = validateLookFile({ ...valid, css: 'body{display:none}' }, options);
  assert.equal(result.css, undefined);
});
