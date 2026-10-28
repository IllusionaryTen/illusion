using System;
using System.Text;
using System.IO;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Illusion {
  public partial class Messenger {
    bool cloudLiveActive;
    async Task<bool> CloudLive(CancellationToken cancellation,int gen) {
      Dictionary<string,object> config;
      try {config=await Api("GET","/v1/public");} catch {return false;}
      if(!config.ContainsKey("liveTransport") || (string)config["liveTransport"]!="websocket")return false;
      int delay=1000;
      while(!cancellation.IsCancellationRequested && gen==generation && !closing) {
        using(var socket=new ClientWebSocket())
        using(var lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancellation)) {
          Task heartbeat=null;
          try {
            socket.Options.Proxy=null;
            var uri=new UriBuilder(server+"/v1/live");uri.Scheme=uri.Scheme=="https"?"wss":"ws";
            using(var connect=CancellationTokenSource.CreateLinkedTokenSource(cancellation)) {connect.CancelAfter(10000);await socket.ConnectAsync(uri.Uri,connect.Token);}
            var auth=Encoding.UTF8.GetBytes(json.Serialize(new{type="authenticate",token=token}));
            await socket.SendAsync(new ArraySegment<byte>(auth),WebSocketMessageType.Text,true,cancellation);
            heartbeat=CloudHeartbeat(socket,lifetime.Token);
            bool ready=false;
            while(socket.State==WebSocketState.Open && !cancellation.IsCancellationRequested && gen==generation) {
              string text=await CloudReceive(socket,cancellation,ready?90000:15000);
              if(gen!=generation||cancellation.IsCancellationRequested)return true;
              if(text==null) {if(socket.CloseStatus.HasValue && (int)socket.CloseStatus.Value==4001){SignOutLocal();Get<System.Windows.Controls.TextBlock>("AuthError").Text=T("Your session expired. Sign in again.");return true;}break;}
              if(text=="pong")continue;
              var message=json.Deserialize<Dictionary<string,object>>(text);
              if(!message.ContainsKey("type"))continue;
              if((string)message["type"]=="ready" || (string)message["type"]=="changed") {
                ready=true;cloudLiveActive=true;await Refresh();if(gen!=generation || cancellation.IsCancellationRequested)return true;
                Connection(true);delay=1000;
                if(peer==null && group==null && lastPeer!="" && contacts.ContainsKey(lastPeer))await SelectPeer(contacts[lastPeer]);
                await FlushOutbox();
              }
            }
          }catch(OperationCanceledException){if(cancellation.IsCancellationRequested)return true;}
          catch(ApiException error){if(gen!=generation||cancellation.IsCancellationRequested)return true;if(error.Status==401){SignOutLocal();return true;}}
          catch {if(cancellation.IsCancellationRequested || gen!=generation)return true;}
          finally {if(gen==generation)cloudLiveActive=false;lifetime.Cancel();if(heartbeat!=null)ObserveCloudHeartbeat(heartbeat);}
        }
        if(gen!=generation || cancellation.IsCancellationRequested)return true;
        Connection(false);try{await Task.Delay(delay,cancellation);}catch(OperationCanceledException){return true;}delay=Math.Min(delay*2,15000);
      }
      return true;
    }
    async Task CloudHeartbeat(ClientWebSocket socket,CancellationToken cancellation) {
      var ping=new ArraySegment<byte>(Encoding.UTF8.GetBytes("ping"));
      while(!cancellation.IsCancellationRequested) {await Task.Delay(30000,cancellation);await socket.SendAsync(ping,WebSocketMessageType.Text,true,cancellation);}
    }
    static void ObserveCloudHeartbeat(Task task){task.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
    async Task<string> CloudReceive(ClientWebSocket socket,CancellationToken cancellation,int timeout) {
      using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancellation))
      using(var text=new MemoryStream()) {
        deadline.CancelAfter(timeout);var buffer=new byte[2048];
        while(true) {var part=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),deadline.Token);if(part.MessageType==WebSocketMessageType.Close)return null;if(part.MessageType!=WebSocketMessageType.Text || text.Length+part.Count>4096)throw new Exception("Invalid live response.");text.Write(buffer,0,part.Count);if(part.EndOfMessage)return Encoding.UTF8.GetString(text.ToArray());}
      }
    }
  }
}
