// Guided Gmail OAuth setup for Illusion account recovery.
// Usage: node scripts/setup-gmail.mjs [--check]
// Google Cloud Console: create an OAuth client of type "Desktop app", then run this.
import { createServer } from 'node:http';
import { randomBytes } from 'node:crypto';
import { readFileSync, writeFileSync, existsSync } from 'node:fs';
import { createInterface } from 'node:readline/promises';
import { parseEnv } from 'node:util';

const PORT = 8765;
const REDIRECT = `http://127.0.0.1:${PORT}/callback`;
const SCOPES = 'https://www.googleapis.com/auth/gmail.send openid email';
const envPath = '.env';
const env = existsSync(envPath) ? parseEnv(readFileSync(envPath, 'utf8')) : {};

if (process.argv.includes('--check')) {
  const ready = Boolean(
    (env.GMAIL_CLIENT_ID && env.GMAIL_CLIENT_SECRET && env.GMAIL_REFRESH_TOKEN && env.GMAIL_SENDER && env.EMAIL_CODE_KEY) &&
    env.EMAIL_CODE_KEY.length >= 32
  );
  console.log(ready ? 'Gmail recovery is configured in .env.' : 'Gmail recovery is NOT configured. Run: npm run setup:gmail');
  process.exit(ready ? 0 : 1);
}

const rl = createInterface({ input: process.stdin, output: process.stdout });
const ask = async (question, current) => {
  if (current) return current;
  const answer = (await rl.question(question)).trim();
  if (!answer) throw new Error('A value is required.');
  return answer;
};

console.log('Illusion · Gmail recovery setup');
console.log('1) Open https://console.cloud.google.com/apis/credentials');
console.log('2) Create an OAuth client ID of type "Desktop app".');
console.log('3) Enable the Gmail API for the project.');
console.log(`   Authorized redirect URI: ${REDIRECT}\n`);

const clientId = await ask('OAuth client ID: ', env.GMAIL_CLIENT_ID);
const clientSecret = await ask('OAuth client secret: ', env.GMAIL_CLIENT_SECRET);
const sender = (await ask('Gmail address that sends codes: ', env.GMAIL_SENDER)).toLowerCase();

const authUrl = new URL('https://accounts.google.com/o/oauth2/v2/auth');
authUrl.searchParams.set('client_id', clientId);
authUrl.searchParams.set('redirect_uri', REDIRECT);
authUrl.searchParams.set('response_type', 'code');
authUrl.searchParams.set('scope', SCOPES);
authUrl.searchParams.set('access_type', 'offline');
authUrl.searchParams.set('prompt', 'consent');
authUrl.searchParams.set('login_hint', sender);

console.log('\nOpen this URL, sign in as ' + sender + ', and approve Gmail sending:\n');
console.log(authUrl.href + '\n');
console.log('Waiting for the callback on ' + REDIRECT + ' …');

const code = await new Promise((resolve, reject) => {
  const server = createServer((req, res) => {
    const url = new URL(req.url, REDIRECT);
    if (url.pathname !== '/callback') { res.writeHead(404).end(); return; }
    const error = url.searchParams.get('error');
    const value = url.searchParams.get('code');
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' })
      .end(`<body style="font-family:system-ui;background:#0b0e16;color:#e8effc;padding:40px"><h1>${error ? 'Authorization failed' : 'Illusion is authorized'}</h1><p>${error ? error : 'You can close this tab and return to the terminal.'}</p></body>`);
    server.close();
    error ? reject(new Error(error)) : resolve(value);
  });
  server.listen(PORT, '127.0.0.1');
  setTimeout(() => { server.close(); reject(new Error('Timed out waiting for the Google callback.')); }, 10 * 60 * 1000).unref();
});

const tokenResponse = await fetch('https://oauth2.googleapis.com/token', {
  method: 'POST',
  headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
  body: new URLSearchParams({
    code,
    client_id: clientId,
    client_secret: clientSecret,
    redirect_uri: REDIRECT,
    grant_type: 'authorization_code'
  })
});
const token = await tokenResponse.json();
if (!tokenResponse.ok || !token.refresh_token) {
  console.error('Google rejected the exchange:', token.error || tokenResponse.status, token.error_description || '');
  console.error('If no refresh token was returned, remove the app grant at https://myaccount.google.com/permissions and retry.');
  process.exit(1);
}

const emailCodeKey = env.EMAIL_CODE_KEY && env.EMAIL_CODE_KEY.length >= 32 ? env.EMAIL_CODE_KEY : randomBytes(32).toString('hex');
const values = {
  GMAIL_CLIENT_ID: clientId,
  GMAIL_CLIENT_SECRET: clientSecret,
  GMAIL_REFRESH_TOKEN: token.refresh_token,
  GMAIL_SENDER: sender,
  EMAIL_CODE_KEY: emailCodeKey
};

let text = existsSync(envPath) ? readFileSync(envPath, 'utf8') : '';
for (const [name, value] of Object.entries(values)) {
  const line = `${name}=${value}`;
  const pattern = new RegExp('^' + name + '=.*$', 'm');
  text = pattern.test(text) ? text.replace(pattern, line) : text.replace(/\s*$/, '') + '\n' + line + '\n';
}
writeFileSync(envPath, text, { mode: 0o600 });

rl.close();
console.log('\nSaved Gmail credentials to .env (not printed here).');
console.log('Next: npm run cloud:deploy — the deploy script pushes them as Worker secrets.');
console.log('Afterwards the "Connect Gmail for account recovery" dialog will accept codes.');
