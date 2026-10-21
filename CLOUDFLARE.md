# Illusion 0.6.1 cloud hosting

Illusion uses a Cloudflare Worker for HTTPS/static files and a SQLite-backed Durable Object for accounts, encrypted message storage and hibernating live sockets. The Telegram bot uses a secret-authenticated webhook in the same Worker. The owner's laptop can be off. No tunnel, custom domain, router forwarding or VPN configuration is required.

The public origin is saved locally in `build/cloudflare-state.json`. Run `Start-Public.cmd`, `Start-Server.cmd` or `Start-Telegram-Bot.cmd` to check hosted health. They do not start duplicate laptop servers once cloud hosting is configured. Open the app with `Start-Illusion.cmd`; friends can use the bot's Open Illusion button, `/download` for Windows, or `/android` for Android. Android uses the Telegram Mini App or a home-screen web install; it is not an APK. Existing users on other devices must import `/connect` and restore their protected encryption-key backup for that connection.

## Deployment and recovery

Use `Deploy-Cloudflare.cmd` to deploy/update from this folder. Wrangler authenticates through Cloudflare's official OAuth flow. Never paste a password, bot token or account token into a chat. The script does not purchase a plan or domain.

First setup keeps registration closed, creates a consistent read-only snapshot of the local database, imports it transactionally into an empty cloud database, and verifies account/message/profile/report counts. Message signatures are checked, IDs/public keys/password hashes are preserved, and session tokens/private device keys are excluded. Repeating the identical import is safe; a different snapshot cannot overwrite a nonempty cloud database. The source database and original device files are kept. `build/cloud-migration.json` includes sensitive account metadata, password hashes and encrypted history: keep it private and outside public assets.

After migration, the Windows release embeds the hosted connection. On this laptop the deployment verifies the existing private key against the migrated public identity, copies the DPAPI-protected key/history/pin to the new connection scope, backs up settings and clears the old session. Sign in again using the existing password. Close Illusion before deploying so settings can be updated safely. Private keys remain local and are never sent to Cloudflare. The backup/source data is separate from the hosted database; later cloud activity is not synchronized back to the old local database.

Only after public health/download checksum checks does the script open registration and configure Telegram's webhook/menu/commands. No real user messages are sent during setup verification. A partial failure keeps the recorded stage and can be resumed; inspect the error before retrying. Retrying does not merge later local changes into an already migrated cloud database.

`build/cloudflare-secrets.dpapi` stores the random administration/webhook secrets protected for the current Windows account. The bot token comes from local `.env` and is stored as a Worker secret. These files, the database and source snapshots are excluded from published assets. Keep the project and protected credentials backed up under your control; a different Windows account cannot decrypt the DPAPI file. If access is lost, authenticated Wrangler secret rotation is required. Never distribute `.env`, the `build/` folder or administration credentials.

## Moderation and deletion

`Review-Cloud-Reports.cmd` lists open reports. Run these from the project directory:

```
node scripts/cloud-admin.mjs status
node scripts/cloud-admin.mjs list
node scripts/cloud-admin.mjs show REPORT_ID
node scripts/cloud-admin.mjs resolve REPORT_ID
node scripts/cloud-admin.mjs dismiss REPORT_ID
node scripts/cloud-admin.mjs ban USERNAME
node scripts/cloud-admin.mjs unban USERNAME
node scripts/cloud-admin.mjs deletions
node scripts/cloud-admin.mjs fulfill-deletion REQUEST_ID EXACT_USERNAME
```

Deletion requires an existing authenticated deletion request and exact matching username. It removes active records and revokes sessions; the administration tool does not expose an arbitrary private-chat browser. Reports reveal only deliberately submitted evidence. Old operator-controlled local snapshots/backups and provider recovery copies require separate retention management. The policies remain drafts with operator details pending at the owner's request.

## Free-plan limits and security boundaries

Free hosting is quota-limited. Workers and Durable Objects each have request limits; SQLite has separate row-read, row-write and storage limits. The single-relay design is for a small preview, not an unbounded messenger. Reads/receipts, login, reports and downloads can consume resources in addition to messages. Static assets use Cloudflare's asset service. Large encrypted envelopes are split into bounded rows; the relay retains its existing storage caps. Exceeding a free quota can stop service until reset; it does not make an automatic paid upgrade.

Live clients use authenticated persistent WebSockets and Cloudflare's hibernation API instead of repeated idle history polls. Tokens are sent in the first socket message, never query strings. Origin checks, per-IP auth/upgrade rate limits, account socket caps and session checks apply. The bot checks Telegram's webhook secret and keeps update IDs/status for up to 24 hours; executable/invitation uploads use actual multipart bytes. A retry after an ambiguous Telegram delivery failure can still duplicate a reply.

Cloudflare terminates TLS and processes account credentials/API metadata and stores relay records. Chat bodies remain encrypted with device-held keys. This custom protocol is unaudited and has no forward secrecy. A compromised host can change browser code or the public-key directory; the Mini App must trust its served code. View-once/expiry cannot prevent recipient copies or instantly erase provider recovery copies. See the current draft policies for actual retention and data handling.

Sources: [Workers pricing](https://developers.cloudflare.com/workers/platform/pricing/), [Durable Objects pricing](https://developers.cloudflare.com/durable-objects/platform/pricing/), [WebSocket hibernation](https://developers.cloudflare.com/durable-objects/best-practices/websockets/), [Telegram Bot API](https://core.telegram.org/bots/api).

## 0.6.1 security update

New account password hashes use versioned scrypt (N=32768/r=8/p=3); legacy hashes upgrade after successful sign-in without changing account IDs or message keys. New protected key backups use version 2 with PBKDF2-HMAC-SHA256 at 600,000 iterations. Version-1 backups still restore; older app releases must be updated to read version-2 backups. These controls do not amount to an independent security audit.

Persistent abuse counters retain hashed security keys, including IP-derived registration keys, until expiry/maintenance (up to 25 hours). These are pseudonymous data, not anonymous records. The draft policy version is 2026-10-19.1. Preview limits include 1000 active accounts, 10 signups per IP/day, 10 active sessions/account, one password derivation at a time and a conservative 256 MiB aggregate storage budget. Legitimate requests can be throttled under load.
