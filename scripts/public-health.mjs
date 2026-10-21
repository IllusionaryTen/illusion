// Only /health is requested: never send account credentials to a tunnel probe.
export async function probePublicRelay(origin, expected, fetcher = fetch) {
  try {
    const response = await fetcher(origin + '/health', {
      redirect: 'error', signal: AbortSignal.timeout(5000)
    });
    if (response.status !== 200) {
      const body = await response.text();
      const cloudflareCode = body.match(/(?:error\s+code\s*:\s*|error-code[^>]*>\s*)(\d{4})/i)?.[1];
      return { ready: false, reason: 'HTTP ' + response.status + (cloudflareCode ? ' (Cloudflare ' + cloudflareCode + ')' : '') };
    }
    let health;
    try { health = await response.json(); }
    catch { return { ready: false, reason: 'HTTP 200, but the response is not relay health JSON' }; }
    if (health?.app !== 'Illusion') return { ready: false, reason: 'The address does not return an Illusion relay' };
    if (health.version !== expected.version) return { ready: false, reason: 'The public relay version does not match the local relay' };
    if (!expected.instance || health.instance !== expected.instance) return { ready: false, reason: 'The address does not reach this running relay instance' };
    return { ready: true, reason: 'This relay is reachable through public HTTPS' };
  } catch (error) {
    // DOMException.code is a legacy number (23 for TimeoutError); preserve the
    // useful exception name instead of displaying that number as a socket code.
    const code = error.cause?.code || (typeof error.code === 'string' ? error.code : error.name);
    const hint = ['ENOTFOUND', 'EAI_AGAIN', 'ENODATA'].includes(code) ? ' (DNS lookup failed)' :
      ['TimeoutError', 'AbortError', 'UND_ERR_CONNECT_TIMEOUT'].includes(code) ? ' (request timed out)' : '';
    return { ready: false, reason: 'HTTPS request failed: ' + (code || 'connection error') + hint };
  }
}
