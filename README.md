# ◈ Illusion 0.8.8

**Private. Customizable. Free.** A portable Windows messenger, native Android APK and Telegram Mini App with an original blue, white, black, checkerboard and orbital interface inspired by illusionary from Sol's RNG. Telegram delivers the Windows and Android apps.

## What's new in 0.8.8

The Windows app now shows its real version in the header. That label had been hardcoded at 0.8.4 since that release, so newer builds looked older than they were; it is now generated from the release version at build time and the About panel reads it from the same source. Existing accounts, chats and keys are unaffected.

## What's new in 0.8.7

Translation is now complete in all 28 non-English languages, not just the main screens. A string-level audit found every place where English bypassed the translation catalog — chat-list previews, the search placeholder, profile editor, report and deletion dialogs, settings policy links, notification text, connection statuses, clipboard and upload errors, theme and effect names, and the Windows app's runtime-created buttons and menus. All of it is translated, with a regression check that fails if any user-visible string loses its translation. Existing accounts, chats and keys are unaffected.

## What's new in 0.8.6

Photos and videos now appear as inline previews in the conversation instead of only an Open button: photos render directly, and videos show a thumbnail of their first frame with a play badge (extracted on your device; if extraction fails, a play badge remains). Clicking a preview opens the full viewer as before.

## What's new in 0.8.5

The interface is now fully translated in **29 languages** — the chat list, conversations, disappearing-message timers, settings, account recovery, reports and update notices no longer mix English into a translated UI, on the web, Android and Windows alike. Finnish, Swedish, Norwegian, Danish, Czech, Romanian and Hungarian join the catalog. You can also **paste a photo straight from the clipboard** (Ctrl+V on Windows, long-press → Paste on phones) to attach and send it like any photo. Existing accounts, chats and keys are unaffected.

## What's new in 0.8.4

Opening a chat (including Favorites) on a phone now switches the whole screen to the conversation; a conflicting mobile stylesheet rule previously kept the chat list on top and pushed the opened conversation below the visible screen. Deploying a new version now publishes an update notice to every account automatically (see Update notices below), and the Windows release file name now follows the package version. Existing accounts, chats and keys are unaffected.

## Security update 0.6.1

Web sign-out clears old content and revokes its session. The update adds session isolation, persistent abuse limits, stronger password/backup derivation and bounded image/content/storage processing. Existing chats/keys and old key backups remain compatible. New version-2 backups require Illusion 0.6.1 or later. Security boundaries and remaining risks are documented in [SECURITY.md](SECURITY.md).

## Android install

The Telegram bot's **`/apk`** command sends the Android installer, and **`/app`** opens Illusion inside Telegram. The app can also be installed from Chrome as a home-screen web app. All versions use the same hosted service and Illusion accounts; Android's encrypted key material stays in that app's private storage.

The Android app is a small native WebView shell around Illusion's hosted Mini App. It requires Android 6.0 or later and an internet connection. The preview APK uses Android's local debug signing key; the downloadable build is for testing and sideloading, and a production release should use a managed publisher signing key. New message alerts use the supplied Illusionsfx sound while the app is connected. Android system alerts require notification permission.

To rebuild it on Windows, run `npm run build:android`. The resulting `dist/Illusion-<version>.apk` and checksum manifest are included when preparing and deploying the Cloudflare release. The bot sends the file with **`/apk`**.

## Language support 0.8.3

The Windows app and Telegram Mini App share a 29-language catalog. The full interface — chat list, conversation, timers, settings, recovery, reports and notices — is translated in every language: English, Spanish, French, German, Brazilian Portuguese, Russian, Ukrainian, Turkish, Arabic, Simplified and Traditional Chinese, Japanese, Korean, Hindi, Indonesian, Vietnamese, Thai, Polish, Italian, Dutch, Bengali, Greek, Finnish, Swedish, Norwegian, Danish, Czech, Romanian and Hungarian. Illusion remains untranslated as the app name. In Windows, open **Settings → Language**, choose a language, then **Save appearance**. The language choice is saved with local settings.

## Public access

Illusion now runs on **Cloudflare**, with a stable HTTPS address and a Telegram webhook. Your laptop can sleep or shut down. Open **Start-Illusion.cmd** on this laptop; friends use **Open Illusion**, **/download** for Windows or **/apk** for Android in the Telegram bot. Fresh Windows downloads include the connection. Existing installations on other devices import the bot's **/connect** invitation and restore their protected key backup when needed.

**Start-Public.cmd**, **Start-Server.cmd** and **Start-Telegram-Bot.cmd** check the hosted service once cloud setup is recorded; no server window needs to stay open. **Deploy-Cloudflare.cmd** publishes updates. See [CLOUDFLARE.md](CLOUDFLARE.md) for migration, administration, recovery, quotas and security boundaries. Operator policies remain drafts.

The cloud relay uses a SQLite-backed Durable Object and authenticated hibernating WebSockets. Idle clients no longer send repeated history polls. Cloudflare terminates TLS and handles credentials/metadata; chat bodies remain client-encrypted. Static app/download assets, account data and bot webhook are hosted together. No paid plan, custom domain or network/VPN changes are required by this setup.

The original local database is preserved separately. It does not mirror later cloud messages. For isolated local development, use **npm start** and Advanced connection **http://127.0.0.1:3789**. The host tooling needs Node 24.13 or later; Windows users need Windows 10/11 with .NET Framework 4.8.

## Update notices

Every deploy that changes the hosted version automatically publishes an update notice to **all accounts**. Each signed-in user sees an **Illusion updates** entry (◈) at the top of their chat list with the latest notice, an unread badge, and a read-only notice history. Notices are labelled in the app as host service messages that are **not end-to-end encrypted**; only the host can publish them. Open the entry to mark notices as read on that device.

To publish a notice manually, use `node scripts/cloud-admin.mjs notice 0.8.4 Message text here` on the hosted relay, or `node --env-file-if-exists=.env scripts/moderate.mjs notice 0.8.4 Message text here` for a local relay. `Deploy-Cloudflare.cmd` sends its own notice (add `--notice "custom text"` to override the default message) whenever the deployed version differs from the previously announced one.

The old temporary tunnel tooling is retained for local experiments, but is not required for cloud hosting. The failed Happ routing experiment was [rolled back](HAPP-CLOUDFLARE.md). Do not reapply it.

## New in 0.5: profiles, themes and Telegram Mini App

Click **your bottom-left profile picture** to edit your profile. The separate profile glyph was removed. The editor has a fixed live preview, original hue/saturation/brightness color plane, RGB sliders/numbers and hex input. Nothing is uploaded before Publish. There are **28 named system fonts**, with device fallbacks and no redistributed font binaries.

Themes now change backgrounds, panels, controls and accent: Blue is dark blue, Ice uses teal surfaces, Silver uses charcoal/silver. Low-contrast message/profile text is adjusted for readability.

The relay serves **`/app/`**: a responsive Mini App with sign-up/login, client-side encryption, live chats, profiles/RGB preview, encrypted photos/MP4, view-once, timers, reports/blocking, key verification, backup/restore, policy links and deletion requests. It uses platform Web Crypto/IndexedDB, with no third-party JavaScript SDK, analytics, remote fonts or Telegram identity/contacts import. Tokens and decrypted messages stay in memory. Device keys are non-extractable in storage; save your protected backup during their creation/restoration session. Existing Windows users restore their `.illusionkey` in the browser before signing in. Different origins have separate browser storage.

**Account recovery:** when the device encryption key is still available on this device (or restored from `.illusionkey`), the user can sign a five-minute challenge with that key and reset the account password. This works for older accounts without a linked email. A verified Gmail address is an additional option: users link it from Settings while signed in, then can request a 10-minute password-reset code. Codes are one-use, limited to five guesses, and recovery revokes the account's sessions. Neither method recreates a missing encryption key; the device key or `.illusionkey` backup is still required to read old messages.

**Gmail recovery (not enabled until configured):** the Worker sends from a separately authorized Gmail mailbox using the Gmail API `gmail.send` scope. Configure `GMAIL_CLIENT_ID`, `GMAIL_CLIENT_SECRET`, `GMAIL_REFRESH_TOKEN`, `GMAIL_SENDER`, and a random 32-byte `EMAIL_CODE_KEY` as Cloudflare Worker secrets; do not put them in source control. Until these secrets are configured, email-code endpoints stay unavailable. Existing accounts have no email linked retroactively; they must be signed in somewhere or prove their device key to add one.

Telegram Mini Apps **are web apps inside Telegram**, requiring reachable HTTPS. This relay can serve the Mini App without a separate paid website. The cloud deployment configures the bot: it sets **Open Illusion** as the menu button; `/app` sends a Mini App launcher and `/download` retains the Windows version. An unconfigured/offline relay cannot offer a working public launch. Local preview: **http://127.0.0.1:3789/app/** with the updated relay running. The web client retains unsent text/retry IDs only in memory; reloading loses unsent web drafts, unlike the Windows durable outbox.

The Mini App and Windows client include a language picker with **29 options** and browser-language detection: Spanish, French, German, Brazilian Portuguese, Russian, Ukrainian, Turkish, Arabic, Simplified and Traditional Chinese, Japanese, Korean, Hindi, Indonesian, Vietnamese, Thai, Polish, Italian, Dutch, Bengali, Greek, Finnish, Swedish, Norwegian, Danish, Czech, Romanian and Hungarian, with English source text. Policy documents are still English.

## Policies and the 20-item review

Draft policies under **legal/** appear in the web footer and Windows Settings → Privacy and data. Operator name/country/contact remain pending; no company or address is invented and no legal-compliance guarantee is made.

Registration has separate unchecked Terms agreement/Privacy acknowledgement, version-checked by the relay, plus a neutral age choice. This preview rejects under-18 registration; it does not pretend that a checkbox is parental consent. Existing accounts are not retroactively age-verified. Children's-service eligibility, branding, controller details, legal bases, transfers and local obligations still need review. There are no payments or marketing emails; refund/unsubscribe notices explain that accurately. The storage notice explains essential storage, with no fabricated optional tracker consent.

Deletion requests require current password and typed username and enter a **manual host queue**. For cloud hosting, list with `node scripts/cloud-admin.mjs deletions` and fulfill only a matching request with `node scripts/cloud-admin.mjs fulfill-deletion ID EXACT_USERNAME`. The local-only administration tool remains `scripts/moderate.mjs`. Active account/session/profile/message/consent/report rows are removed transactionally; device data, backups and other users' copies cannot be remotely recalled. Reports/completed request records have a 90-day cleanup.

## Existing features

- **View-once text/photo/video:** tick **View once** before sending. The recipient gets a sealed **Open once** button. The relay provides the encrypted envelope to the recipient's first open request, then removes that body. Closing the viewer removes this app's opened copy. A failed opening/download can lose the message. Screenshots, recordings, modified clients and previously made copies cannot be prevented.
- **Image/video attachments:** photos and MP4 videos up to 4 MB are uploaded without end-to-end encryption; the relay operator can access their contents. Message text stays encrypted. Older encrypted attachments remain readable. Photos are resized/re-encoded to remove original metadata; videos retain their original bytes/metadata.
- **Shared disappearing timers:** **Profile / Timer** offers Off, 1 minute, 1 hour, 1 day, 1 week or 30 days. **Off is the default in every new chat.** Either participant can change it. New messages expire from relay acceptance time; existing messages keep their original deadlines. View-once and timer policy are signed. If the timer changed while a message was queued, stop its retries and send it again using the current timer.
- **Reports/blocking:** right-click an incoming message → **Report message**, or use **Profile / Timer** to report a profile/block a user. Reports share the reporter, target, reason, note and message identifier. Verified selected text/caption sharing is **optional and unchecked by default**. Profile reports snapshot the public profile. Other chats and account private keys are not shared. Photo/video bytes are not retained as report evidence. Blocking stops new messages in both directions until the blocker unblocks.
- **Profiles:** clicking your bottom-left profile picture edits your display name, description, picture, banner, profile font/colors and Orbit/Glow/Prism/Checkerboard effects. Signed-in people who find your username see this public profile. Images use bounded PNG/JPEG data, not tracking URLs. Public profiles are not end-to-end encrypted.
- **Appearance:** Settings offers the shared system-font choices, custom text/bubble colors, accents, size, spacing, artwork, animation and read-receipt controls. A contact's profile style does not override your chat appearance.

## Messages and keys

Usernames use **2–24 ASCII Latin letters, digits, `_` and `-`**, case-insensitively. Passwords need at least 10 characters. Find an exact username; Enter sends, Shift+Enter inserts a line. Ctrl+K finds someone; Ctrl+F searches loaded messages locally. Right-click normal messages to copy/reply, or chats to pin. The diamond opens Saved messages.

**Back up your encryption key in Settings.** Keys/tokens use Windows DPAPI for the current profile. Another Windows profile needs your password-protected `.illusionkey` backup. The account password alone cannot recover chats. Compare Verify keys fingerprints through another trusted channel.

Drafts/outbox survive restarts. Offline sends retry with the same ID. Up to 400 recent encrypted envelopes / 8 MB are cached; outbox limit is 100 messages / 8 MB. The app removes expired cache content when it runs, including before showing offline history. External backups, disk recovery, screenshots and malware remain outside that guarantee. Windows video playback uses a short-lived decrypted MP4 file, deleted on viewer close and cleaned after a crash at the next startup.

## Review reports

**Review-Reports.cmd** lists reports. In PowerShell in this folder:

```
node --env-file-if-exists=.env scripts/moderate.mjs show 1
node --env-file-if-exists=.env scripts/moderate.mjs resolve 1
node --env-file-if-exists=.env scripts/moderate.mjs dismiss 1
node --env-file-if-exists=.env scripts/moderate.mjs ban username
node --env-file-if-exists=.env scripts/moderate.mjs unban username
```

The host can read submitted evidence/public profiles, with no endpoint for browsing other people's decrypted chats. Bans revoke sessions and prevent login. Moderation is manual; reporting does not promise an immediate response.

## Privacy and verification

The relay stores ciphertext/public keys and sees accounts, contact pairs, times, sizes and IPs. It receives no account private keys. This custom RSA/AES/HMAC protocol has **no forward secrecy and no independent audit**. Compromised endpoints/keys or a malicious first-use directory can expose chats. Nobody can honestly guarantee that chats can never be obtained. See SECURITY.md.

Current encrypted bodies are limited to 64 MB per sender / 512 MB per relay. Authentication/sending/body limits are preview controls, not a production abuse-prevention system. Set `REGISTRATION_CODE` for an invite-only network. Accounts, passwords, key files and old encrypted envelopes survive the schema update. All participants should upgrade to 0.5 for version-2 messages/media/timers.

```
npm test
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -TestBuild -OutputName Illusion-0.5.0.exe
node scripts/test-desktop.mjs
node scripts/test-miniapp.mjs
```

Native tests need a regular Windows user for DPAPI and use isolated accounts, a real relay, deliberate outages/dropped acknowledgements, and actual WPF rendering. Telegram tests simulate uploads and do not message real users. Release hashes cover the exe, ZIP and invitation. The app is unsigned, portable and has no auto-updater.
