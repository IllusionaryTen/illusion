using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Illusion {
  public sealed class ConnectionProfile {
    public string Name,Relay;
    public static string NormalizeRelay(string value) {
      Uri uri;
      if(string.IsNullOrWhiteSpace(value) || value.Length>2048 || !Uri.TryCreate(value.Trim(),UriKind.Absolute,out uri) || uri.UserInfo!="" || uri.Query!="" || uri.Fragment!="" || uri.AbsolutePath!="/" || !(uri.Scheme=="https" || (uri.Scheme=="http" && uri.IsLoopback)))throw new Exception("Connection invitations require an HTTPS relay address. HTTP loopback is allowed only for testing on the host PC.");
      return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
    public static ConnectionProfile Parse(string text,bool allowEmpty=false) {
      if(text==null || text.Length>6000)throw new Exception("This is not a valid Illusion connection file.");
      var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text);
      if(data==null || data.Keys.Any(k=>!new[]{"format","version","name","relay"}.Contains(k)) || !data.ContainsKey("format") || (string)data["format"]!="illusion.connection" || !data.ContainsKey("version") || Convert.ToInt32(data["version"])!=1)throw new Exception("Unsupported Illusion connection file.");
      string name=data.ContainsKey("name")?data["name"] as string:null;
      if(string.IsNullOrWhiteSpace(name) || name.Length>48 || name.Any(char.IsControl))throw new Exception("Connection name must be 1–48 characters.");
      string relay=data.ContainsKey("relay")?data["relay"] as string:null;
      if(string.IsNullOrWhiteSpace(relay) && allowEmpty)return new ConnectionProfile{Name=name.Trim(),Relay=""};
      return new ConnectionProfile{Name=name.Trim(),Relay=NormalizeRelay(relay)};
    }
    public static ConnectionProfile Embedded() {
      using(var stream=typeof(ConnectionProfile).Assembly.GetManifestResourceStream("connection.json")) {
        if(stream==null)return new ConnectionProfile{Name="Illusion",Relay=""};
        using(var reader=new StreamReader(stream))return Parse(reader.ReadToEnd(),true);
      }
    }
  }
}
