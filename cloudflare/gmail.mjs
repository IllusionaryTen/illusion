const tokenUrl='https://oauth2.googleapis.com/token';
const sendUrl='https://gmail.googleapis.com/gmail/v1/users/me/messages/send';

// Gmail is the service's sender mailbox. Only Gmail's send scope is needed.
// Secrets are configured as Worker secrets and are never logged or returned.
export function createGmailSender(env) {
  let accessToken='',refreshAt=0;
  return async ({to,subject,code,expiresMinutes})=>{
    if(!env.GMAIL_CLIENT_ID||!env.GMAIL_CLIENT_SECRET||!env.GMAIL_REFRESH_TOKEN||!env.GMAIL_SENDER)throw Error('Gmail sender is not configured.');
    if(Date.now()>=refreshAt){
      const response=await fetch(tokenUrl,{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded'},body:new URLSearchParams({client_id:env.GMAIL_CLIENT_ID,client_secret:env.GMAIL_CLIENT_SECRET,refresh_token:env.GMAIL_REFRESH_TOKEN,grant_type:'refresh_token'}),signal:AbortSignal.timeout(10000)});
      const result=await response.json();if(!response.ok||typeof result.access_token!=='string')throw Error('Gmail authorization failed.');
      accessToken=result.access_token;refreshAt=Date.now()+Math.max(60,Number(result.expires_in||3600)-60)*1000;
    }
    const safeTo=String(to).replace(/[\r\n<>]/g,''),safeFrom=String(env.GMAIL_SENDER).replace(/[\r\n<>]/g,'');
    if(safeTo!==to||safeFrom!==env.GMAIL_SENDER)throw Error('Invalid email address.');
    const text=`Your Illusion verification code is ${code}.\n\nIt expires in ${expiresMinutes} minutes. If you did not request this, you can ignore this email. Never share this code.`;
    const raw=Buffer.from(`To: ${safeTo}\r\nFrom: ${safeFrom}\r\nSubject: ${subject}\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=UTF-8\r\n\r\n${text}`).toString('base64url');
    const response=await fetch(sendUrl,{method:'POST',headers:{Authorization:'Bearer '+accessToken,'Content-Type':'application/json'},body:JSON.stringify({raw}),signal:AbortSignal.timeout(10000)});
    if(!response.ok)throw Error('Gmail could not send the verification email.');
  };
}
