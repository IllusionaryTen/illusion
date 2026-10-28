import test from 'node:test';
import assert from 'node:assert/strict';
import { languageOptions, translate, translationCatalog } from '../web/i18n.mjs';

test('all published locales have a complete UI phrase row', () => {
  const { languages, english, rows } = translationCatalog;
  assert.equal(languages.length, 34);
  assert.ok(languages.some(([code]) => code === 'el'));
  assert.ok(languages.some(([code]) => code === 'vi'));
  assert.ok(languages.some(([code]) => code === 'fi'));
  assert.ok(languages.some(([code]) => code === 'no'));
  assert.ok(languages.some(([code]) => code === 'da'));
  for (const [code] of languages) {
    if (code === 'en') continue;
    assert.equal(rows[code]?.length, english.length, `${code} translation length`);
    assert.ok(rows[code].every(value => typeof value === 'string' && value.trim()), `${code} has an empty translation`);
  }
});

test('the full-coverage block leaves no English fallback in any locale', () => {
  const { english, rows } = translationCatalog;
  // Words that are legitimately identical in many languages (numbers, language names, loanwords).
  const allowed = new Set(['13–15', '16–17', 'English', 'Greek', 'Vietnamese', 'Chats', 'Cloud', '1 minute', 'Pause', '1 week', 'Photo / Video', 'Report', 'Play', 'Draft: ', 'You: ', 'Spam', 'Silver', 'Orbit']);
  for (const [code, row] of Object.entries(rows)) {
    for (let i = 84; i < english.length; i++) {
      if (row[i] === english[i] && !allowed.has(english[i])) {
        assert.fail(`${code} untranslated full-block phrase: ${english[i]}`);
      }
    }
  }
});

test('the product name stays Illusion in every language', () => {
  for (const { code } of languageOptions()) assert.equal(translate('Illusion', code), 'Illusion');
});

test('accessibility labels use localized equivalents where the app uses a short variant', () => {
  assert.notEqual(translate('Edit my profile', 'el'), 'Edit my profile');
  assert.notEqual(translate('Device storage notice', 'vi'), 'Device storage notice');
  assert.equal(translate('Edit my profile', 'en'), 'Edit my profile');
});

test('Greek and Vietnamese translate the messenger entry points', () => {
  assert.equal(translate('Username', 'el'), 'Όνομα χρήστη');
  assert.equal(translate('Username', 'vi'), 'Tên người dùng');
  assert.equal(translate('Private. Customizable. Free.', 'el'), 'Ιδιωτικό. Προσαρμόσιμο. Δωρεάν.');
  assert.equal(translate('Private. Customizable. Free.', 'vi'), 'Riêng tư. Tùy chỉnh được. Miễn phí.');
});

test('the expanded landing and settings copy does not silently fall back to English', () => {
  const required = ['Private. Customizable. Free.', 'No tracking cookies or analytics. Essential device storage keeps your encryption keys and appearance preferences.', 'This is an encrypted messenger inside Telegram. It uses your Illusion account, without importing Telegram contacts or identity.', 'Profile accent', 'Connect Gmail for account recovery'];
  for (const [code] of translationCatalog.languages) {
    if (code === 'en') continue;
    for (const phrase of required) assert.notEqual(translate(phrase, code), phrase, `${code} untranslated: ${phrase}`);
  }
});
