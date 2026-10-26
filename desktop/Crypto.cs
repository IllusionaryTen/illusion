using System;
using System.Text;
using System.Linq;
using System.Security.Cryptography;
using System.Collections.Generic;

namespace Illusion {
  public static class Crypto {
    public static byte[] Random(int size) { byte[] b = new byte[size]; using(var rng=RandomNumberGenerator.Create()) rng.GetBytes(b); return b; }
    public static string B64(byte[] b) { return Convert.ToBase64String(b); }
    public static byte[] Un64(object b) { return Convert.FromBase64String((string)b); }
    public static RSACryptoServiceProvider Rsa(string xml) { var rsa = new RSACryptoServiceProvider(3072); rsa.PersistKeyInCsp=false;try{if(xml!=null){if(xml.Length>8192 || xml.Contains("<!") || xml.Contains("<?") || xml.Contains("&"))throw new CryptographicException("Invalid RSA key XML.");rsa.FromXmlString(xml);if(rsa.KeySize!=3072)throw new CryptographicException("A 3072-bit RSA key is required.");}return rsa;}catch{rsa.Dispose();throw;} }
    public static string NewKey() { using(var rsa=Rsa(null)) return rsa.ToXmlString(true); }
    public static string Public(string key) { using(var rsa=Rsa(key)) return rsa.ToXmlString(false); }
    public static string Fingerprint(string key) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "").ToLowerInvariant(); }
    public static string SignRecovery(string challengeId,string username,string nonce,long expiresAt,string privateKey) { string text="illusion:account-recovery:v1\n"+challengeId+"\n"+username+"\n"+nonce+"\n"+expiresAt.ToString(System.Globalization.CultureInfo.InvariantCulture);using(var rsa=Rsa(privateKey))return B64(rsa.SignData(Encoding.UTF8.GetBytes(text),"SHA256")); }
    public static string Protect(string value) { return B64(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser)); }
    public static string Unprotect(string value) { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value),null,DataProtectionScope.CurrentUser)); }
    static byte[] Aes(byte[] input, byte[] key, byte[] iv, bool encrypt) {
      using(var aes=AesManaged.Create()) { aes.Key=key; aes.IV=iv; aes.Mode=CipherMode.CBC; aes.Padding=PaddingMode.PKCS7; using(var transform=encrypt?aes.CreateEncryptor():aes.CreateDecryptor()) return transform.TransformFinalBlock(input,0,input.Length); }
    }
    static byte[] Mac(byte[] key, byte[] iv, byte[] ciphertext) { using(var h=new HMACSHA256(key)) return h.ComputeHash(iv.Concat(ciphertext).ToArray()); }
    static bool Equal(byte[] a, byte[] b) { int different=a.Length^b.Length; for(int i=0;i<Math.Min(a.Length,b.Length);i++) different|=a[i]^b[i]; return different==0; }
    public static string SignedText(string sender,string recipient,string clientId,Dictionary<string,object> e) {
      var keys=(Dictionary<string,object>)e["keys"];
      var parts=new List<string>{"illusion:v"+Convert.ToString(e["v"]),sender,recipient,clientId,(string)e["iv"],(string)e["ciphertext"],(string)e["mac"],(string)keys[sender],(string)keys[recipient]};
      if(Convert.ToInt32(e["v"])==2){var p=(Dictionary<string,object>)e["policy"];parts.Add((bool)p["viewOnce"]?"1":"0");parts.Add(Convert.ToString(p["ttlSeconds"],System.Globalization.CultureInfo.InvariantCulture));}return string.Join("\n",parts);
    }
    public static Dictionary<string,object> Encrypt(string text,string sender,string recipient,string clientId,string privateKey,string peerPublic,Dictionary<string,object> policy=null) {
      byte[] material=Random(64), iv=Random(16);
      byte[] ciphertext=Aes(Encoding.UTF8.GetBytes(text),material.Take(32).ToArray(),iv,true);
      var keys=new Dictionary<string,object>();
      using(var own=Rsa(privateKey)) keys[sender]=B64(own.Encrypt(material,true));
      using(var peer=Rsa(peerPublic)) keys[recipient]=B64(peer.Encrypt(material,true));
      var e=new Dictionary<string,object>{{"v",1},{"iv",B64(iv)},{"ciphertext",B64(ciphertext)},{"mac",B64(Mac(material.Skip(32).ToArray(),iv,ciphertext))},{"keys",keys}};
      if(policy!=null){e["v"]=2;e["policy"]=policy;}
      using(var own=Rsa(privateKey)) e["signature"]=B64(own.SignData(Encoding.UTF8.GetBytes(SignedText(sender,recipient,clientId,e)),"SHA256"));
      Array.Clear(material,0,material.Length);
      return e;
    }
    public static string Decrypt(Dictionary<string,object> message,string myId,string privateKey,string senderPublic) {
      var e=(Dictionary<string,object>)message["envelope"];
      if(Convert.ToInt32(e["v"])!=1 && Convert.ToInt32(e["v"])!=2) throw new CryptographicException("Unsupported encryption version.");
      using(var sender=Rsa(senderPublic)) if(!sender.VerifyData(Encoding.UTF8.GetBytes(SignedText((string)message["senderId"],(string)message["recipientId"],(string)message["clientId"],e)),"SHA256",Un64(e["signature"]))) throw new CryptographicException("Message signature failed.");
      var keys=(Dictionary<string,object>)e["keys"]; byte[] material;
      using(var own=Rsa(privateKey)) material=own.Decrypt(Un64(keys[myId]),true);
      if(material.Length!=64) throw new CryptographicException("Invalid key.");
      byte[] iv=Un64(e["iv"]), ciphertext=Un64(e["ciphertext"]);
      if(!Equal(Mac(material.Skip(32).ToArray(),iv,ciphertext),Un64(e["mac"]))) throw new CryptographicException("Message integrity failed.");
      string result=Encoding.UTF8.GetString(Aes(ciphertext,material.Take(32).ToArray(),iv,false));
      Array.Clear(material,0,material.Length); return result;
    }
    public static string ReportMaterial(Dictionary<string,object> message,string myId,string privateKey) {
      var e=(Dictionary<string,object>)message["envelope"];var keys=(Dictionary<string,object>)e["keys"];
      using(var own=Rsa(privateKey)){var material=own.Decrypt(Un64(keys[myId]),true);try{return B64(material);}finally{Array.Clear(material,0,material.Length);}}
    }
    public static Dictionary<string,object> Backup(string privateKey,string password) {
      byte[] salt=Random(16),iv=Random(16),material;
      using(var kdf=new Rfc2898DeriveBytes(password,salt,600000,HashAlgorithmName.SHA256)) material=kdf.GetBytes(64);
      byte[] ciphertext=Aes(Encoding.UTF8.GetBytes(privateKey),material.Take(32).ToArray(),iv,true);
      var result=new Dictionary<string,object>{{"version",2},{"salt",B64(salt)},{"iv",B64(iv)},{"ciphertext",B64(ciphertext)},{"mac",B64(Mac(material.Skip(32).ToArray(),iv,ciphertext))}};
      Array.Clear(material,0,material.Length);return result;
    }
    public static string Restore(Dictionary<string,object> b,string password) {
      int version=Convert.ToInt32(b["version"]);if(version!=1 && version!=2) throw new CryptographicException("Unsupported key file.");
      byte[] salt=Un64(b["salt"]),iv=Un64(b["iv"]),ciphertext=Un64(b["ciphertext"]),material;
      if(salt.Length!=16 || iv.Length!=16 || ciphertext.Length>10000 || ciphertext.Length==0 || ciphertext.Length%16!=0 || Un64(b["mac"]).Length!=32) throw new CryptographicException("Invalid key file.");
      using(var kdf=new Rfc2898DeriveBytes(password,salt,version==1?250000:600000,HashAlgorithmName.SHA256)) material=kdf.GetBytes(64);
      try{if(!Equal(Mac(material.Skip(32).ToArray(),iv,ciphertext),Un64(b["mac"]))) throw new CryptographicException("Wrong backup password or damaged key file.");
        string result=Encoding.UTF8.GetString(Aes(ciphertext,material.Take(32).ToArray(),iv,false));Public(result);return result;
      }finally{Array.Clear(material,0,material.Length);}
    }

    public static byte[] SealMedia(byte[] plain,out string keyB64,out string ivB64,out string macKeyB64,out string macB64){
      byte[] key=Random(32),macKey=Random(32),iv=Random(16);
      using(var aes=new AesManaged{KeySize=256,BlockSize=128,Mode=CipherMode.CBC,Padding=PaddingMode.PKCS7}){aes.Key=key;aes.IV=iv;using(var enc=aes.CreateEncryptor()){byte[] cipher=enc.TransformFinalBlock(plain,0,plain.Length);var input=new byte[iv.Length+cipher.Length];Buffer.BlockCopy(iv,0,input,0,iv.Length);Buffer.BlockCopy(cipher,0,input,iv.Length,cipher.Length);string mac;using(var hmac=new HMACSHA256(macKey))mac=B64(hmac.ComputeHash(input));keyB64=B64(key);ivB64=B64(iv);macKeyB64=B64(macKey);macB64=mac;return cipher;}}
    }
    public static byte[] OpenMedia(byte[] cipher,string keyB64,string ivB64,string macKeyB64,string macB64){
      byte[] key=Convert.FromBase64String(keyB64),iv=Convert.FromBase64String(ivB64),macKey=Convert.FromBase64String(macKeyB64);
      if(key.Length!=32||iv.Length!=16||macKey.Length!=32)throw new CryptographicException("Invalid attachment key.");
      var input=new byte[iv.Length+cipher.Length];Buffer.BlockCopy(iv,0,input,0,iv.Length);Buffer.BlockCopy(cipher,0,input,iv.Length,cipher.Length);
      string expected;using(var hmac=new HMACSHA256(macKey))expected=B64(hmac.ComputeHash(input));
      if(!string.Equals(expected,Convert.ToString(macB64),StringComparison.Ordinal))throw new CryptographicException("Attachment integrity failed.");
      using(var aes=new AesManaged{KeySize=256,BlockSize=128,Mode=CipherMode.CBC,Padding=PaddingMode.PKCS7}){aes.Key=key;aes.IV=iv;using(var dec=aes.CreateDecryptor())return dec.TransformFinalBlock(cipher,0,cipher.Length);}
    }
  }
}
