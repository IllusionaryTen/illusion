# Contributing to Illusion

Thanks for helping. Illusion is a small end-to-end encrypted messenger with three clients (web/Android WebView, Windows WPF) and one relay.

## Ground rules

- Message text and attachments must never reach the relay unencrypted. If you touch `web/crypto.mjs`, `desktop/Crypto.cs` or `server/features.mjs`, read `SECURITY.md` first.
- Every user-visible string goes through the translation catalog (`web/i18n.mjs` for web, `T()` for Windows). English fallbacks are treated as bugs.
- Do not publish operational tooling or credentials. Hosting/moderation scripts are intentionally local-only (`scripts/` is partly ignored).
- Never commit `.env`, `.illusionkey`, databases, profiles or session state.

## Before opening a pull request

```bash
npm test                 # unit + server tests
node scripts/test-miniapp.mjs   # real browser: two contexts, encryption, media, themes, languages
npm run build            # Windows compile gate
```

`node scripts/audit-i18n.mjs` must report **0 leaks**, and `node scripts/audit-desktop-i18n.mjs` must report **0 real gaps**.

## Code layout

| Path | What lives there |
|---|---|
| `web/` | The app used by the browser, the Android WebView and the Telegram Mini App |
| `desktop/` | Windows client (WPF, compiled with the .NET Framework `csc`) |
| `server/` | Relay: auth, envelopes, media validation, moderation hooks |
| `cloudflare/` | Worker + Durable Object hosting the public relay |
| `bot/` | Telegram bot (delivery of EXE/APK, Mini App links) |
| `shared/` | Version, secrets comparison, fonts |
| `tests/` | Node test suites + Windows UI tests |

See `docs/translations.md` for adding a language.
