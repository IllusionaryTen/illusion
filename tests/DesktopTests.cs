using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Net.Http;
using System.Collections;

namespace Illusion {
  public static class DesktopTests {
    static int checks=0;
    static string root;
    static void Check(bool condition,string name) {if(!condition)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);checks++;}
    static async Task Wait(Func<bool> condition,string name) {var until=DateTime.UtcNow.AddSeconds(25);while(!condition()){if(DateTime.UtcNow>until)throw new Exception("Timed out: "+name);await Task.Delay(100);}Check(true,name);}
    static object Field(Messenger m,string name) {return typeof(Messenger).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(m);}
    static object Call(Messenger m,string name,params object[] args) {return typeof(Messenger).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(m,args);}
    static void Click(Messenger m,string name) {m.Get<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    static string AllText(DependencyObject o) {string value=o is TextBlock?((TextBlock)o).Text+"\n":"";for(int i=0;i<VisualTreeHelper.GetChildrenCount(o);i++)value+=AllText(VisualTreeHelper.GetChild(o,i));return value;}
    static async Task Register(Messenger m,string username,string displayName,string server,bool useConfigured=false) {
      m.Window.Show();if(!useConfigured)m.Get<TextBox>("ServerInput").Text=server;m.Get<TextBox>("UsernameInput").Text=username;m.Get<PasswordBox>("PasswordInput").Password="a strong testing password";
      Click(m,"SwitchAuthButton");m.Get<ComboBox>("AgeBand").SelectedIndex=4;m.Get<CheckBox>("AcceptTerms").IsChecked=true;m.Get<CheckBox>("AcknowledgePrivacy").IsChecked=true;m.Get<TextBox>("DisplayNameInput").Text=displayName;Click(m,"AuthButton");
      await Task.Delay(300);Console.WriteLine("Account status: "+m.Get<TextBlock>("AuthError").Text);
      try {await Wait(()=>m.Get<Border>("AuthOverlay").Visibility==Visibility.Collapsed,"Register native client @"+username);}
      catch(Exception e) {throw new Exception(e.Message+" / "+m.Get<TextBlock>("AuthError").Text,e);}
      Check(m.Get<TextBlock>("IdentityLabel").Text=="@"+username,"Signed-in identity @"+username);
    }
    static async Task Find(Messenger m,string username) {m.Get<TextBox>("SearchInput").Text=username;Click(m,"FindButton");await Wait(()=>m.Get<TextBlock>("ChatSubtitle").Text.StartsWith("@"+username+" "),"Find @"+username);await Task.Delay(250);}
    static async Task Send(Messenger m,string text) {m.Get<TextBox>("Composer").Text=text;Click(m,"SendButton");await Wait(()=>m.Get<TextBox>("Composer").Text=="","Send native encrypted message");}
    static Border Bubble(DependencyObject node,string text) {var border=node as Border;if(border!=null && border.ContextMenu!=null && AllText(border).Contains(text))return border;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var found=Bubble(VisualTreeHelper.GetChild(node,i),text);if(found!=null)return found;}return null;}
    static IEnumerable<T> Controls<T>(DependencyObject node) where T:DependencyObject {if(node is T)yield return (T)node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var value in Controls<T>(VisualTreeHelper.GetChild(node,i)))yield return value;}
    static async Task<Dictionary<string,object>> NativeApi(Messenger m,string method,string path,object value=null){return await (Task<Dictionary<string,object>>)Call(m,"Api",method,path,value,true);}
    static void Modal(Action show,string title,Action<Window> check) {
      Exception failure=null;bool seen=false;var tick=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};
      tick.Tick+=(s,e)=>{var dialog=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w.Title==title);if(dialog==null)return;tick.Stop();try{seen=true;dialog.UpdateLayout();check(dialog);}catch(Exception error){failure=error;}finally{dialog.Close();}};
      tick.Start();try{show();}finally{tick.Stop();}if(failure!=null)throw failure;Check(seen,"Native dialog renders: "+title);
    }
    static void CaptureWindow(Window window,string path){window.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=File.Create(path))png.Save(stream);}
    static void AppearanceChecks(Messenger m){
      Check(m.Window.FindName("ProfileButton")==null,"Separate profile glyph button is removed from the left rail");Check(m.Get<Button>("AvatarButton")!=null,"Own profile picture is a keyboard-accessible editor button");
      var fonts=(string[])typeof(Messenger).GetField("ProfileFonts",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);Check(fonts.Length>=25,"Desktop exposes at least 25 shared system font families");
      Modal(()=>Click(m,"AvatarButton"),"Your profile",w=>{var name=Controls<TextBox>(w).First(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Profile display name");name.Text="Live native preview";Check(AllText(w).Contains("Live native preview"),"Native profile name updates in live preview before publishing");var expand=Controls<Expander>(w).First();expand.IsExpanded=true;w.UpdateLayout();var studio=Controls<ColorStudio>(w).First();studio.Hex="#1A9EE8";Check(Controls<Slider>(studio).Count(c=>c.Maximum==255)==3,"RGB studio offers three keyboard-accessible channel sliders");Check(Controls<TextBox>(studio).Any(c=>c.Text=="#1A9EE8"),"RGB studio synchronizes the hex value with color selection");CaptureWindow(w,System.IO.Path.GetFullPath("artifacts/illusion-profile-editor.png"));});
      var settings=(Dictionary<string,string>)Field(m,"settings");string prior=settings.ContainsKey("theme")?settings["theme"]:null;var colors=new List<Color>();foreach(string theme in new[]{"blue","ice","silver"}){settings["theme"]=theme;Call(m,"ApplyTheme");m.Window.UpdateLayout();colors.Add(((SolidColorBrush)m.Window.Background).Color);}Check(colors.Distinct().Count()==3,"All three themes change the actual native window background");if(prior==null)settings.Remove("theme");else settings["theme"]=prior;Call(m,"ApplyTheme");
      Modal(()=>Call(m,"ShowPrivacyAndData"),"Privacy and data",w=>Check(AllText(w).Contains("Request account deletion"),"Native privacy center exposes policies and account deletion request"));
      var interopPath=System.IO.Path.GetFullPath("build/webcrypto-interop.json");if(File.Exists(interopPath)){var data=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(interopPath));string restored=Crypto.Restore((Dictionary<string,object>)data["backup"],(string)data["password"]);Check(Crypto.Public(restored)==(string)data["publicKey"],"Desktop restores a real browser Web Crypto .illusionkey backup");}
      string testKey=(string)Field(m,"privateKey"),testPassword="a native interop backup password";File.WriteAllText("build/nativecrypto-interop.json",new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{backup=Crypto.Backup(testKey,testPassword),password=testPassword,publicKey=Crypto.Public(testKey)}));
    }
    static async Task PrivacyChecks(Messenger alice,Messenger bob){
      var a=(Dictionary<string,object>)Field(alice,"me");var b=(Dictionary<string,object>)Field(bob,"me");string aid=(string)a["id"],bid=(string)b["id"];
      await alice.SelectPeer(b);await bob.SelectPeer(a);await alice.Refresh();await bob.Refresh();
      Check(alice.Get<TextBlock>("ComposerHint").Text.Contains("Off (default)"),"Chat auto-delete is off by default in the native composer");
      alice.Get<CheckBox>("ViewOnceCheck").IsChecked=true;await Send(alice,"never cache this view-once secret");
      await Wait(()=>AllText(bob.Get<StackPanel>("MessageList")).Contains("Open once"),"View-once native message displays a sealed Open once control");
      Check(!AllText(bob.Get<StackPanel>("MessageList")).Contains("never cache this"),"View-once content stays sealed in native history");
      var once=((SortedDictionary<long,Dictionary<string,object>>)Field(bob,"messages")).Values.Last(m=>m.ContainsKey("viewOnce") && (bool)m["viewOnce"]);
      Check(once["envelope"]==null,"Recipient's protected workspace has no view-once ciphertext or wrapped key");
      var opened=(Dictionary<string,object>)(await NativeApi(bob,"POST","/v1/open",new{id=once["id"]}))["message"];
      var decoded=Call(bob,"Decode",opened,bid,(string)Field(bob,"privateKey"),(string)a["publicKey"],true);
      Modal(()=>Call(bob,"ShowMessageContent",opened,decoded,a,true),"View once",w=>Check(AllText(w).Contains("never cache this view-once secret"),"View-once content decrypts in its temporary native viewer"));
      await bob.Refresh();Check(!AllText(bob.Get<StackPanel>("MessageList")).Contains("Open once"),"Consumed view-once control disappears after live refresh");
      Call(bob,"SaveLocalState");string workspace=Crypto.Unprotect(File.ReadAllText((string)Field(bob,"localStatePath")));Check(!workspace.Contains(Convert.ToString(((Dictionary<string,object>)opened["envelope"])["ciphertext"])),"Consumed view-once ciphertext is absent from the saved workspace");
      var pixels=new byte[32*32*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=240;pixels[i+1]=90;pixels[i+2]=45;pixels[i+3]=255;}
      var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(32,32,96,96,PixelFormats.Bgra32,null,pixels,128);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));byte[] photo;using(var stream=new MemoryStream()){png.Save(stream);photo=stream.ToArray();}
      var profile=new Dictionary<string,object>{{"bio","A dream beyond the noise."},{"avatar","data:image/png;base64,"+Convert.ToBase64String(photo)},{"banner","data:image/png;base64,"+Convert.ToBase64String(photo)},{"accent","#436FFF"},{"textColor","#FFFFFF"},{"font","Georgia"},{"effect","Prism"}};
      await NativeApi(alice,"PUT","/v1/profile",new{displayName="Alice",profile=profile});
      var published=(Dictionary<string,object>)(await NativeApi(bob,"GET","/v1/users?username=ai"))["user"];
      Modal(()=>Call(bob,"ShowProfile",published,false),"Profile",w=>{Check(AllText(w).Contains("A dream beyond the noise."),"Published description renders in a contact profile");Check(Controls<Border>(w).Any(x=>x.Background is ImageBrush),"Native avatar and banner decode from uploaded profile images");});
      var pending=new Dictionary<string,object>{{"kind","photo"},{"mime","image/png"},{"data",Convert.ToBase64String(photo)}};
      typeof(Messenger).GetField("pendingMedia",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(alice,pending);Call(alice,"UpdateAttachment");Check(alice.Get<Border>("AttachmentBar").Visibility==Visibility.Visible,"Native attachment composer indicates selected photo");
      await Send(alice,"a private photo caption");await Wait(()=>AllText(bob.Get<StackPanel>("MessageList")).Contains("Open photo"),"Encrypted photo reaches recipient's native chat");
      var photoButton=Controls<Button>(bob.Get<StackPanel>("MessageList")).First(x=>Convert.ToString(x.Content).Contains("Open photo"));
      Modal(()=>photoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),"Attachment",w=>Check(Controls<System.Windows.Controls.Image>(w).Any(x=>x.Source!=null),"Recipient renders decrypted photo in the native attachment viewer"));
      string videoPath=System.IO.Path.GetFullPath("tests/fixtures/video.mp4");if(!File.Exists(videoPath))throw new Exception("Missing original MP4 fixture: tests/fixtures/video.mp4");
      typeof(Messenger).GetField("pendingMedia",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(alice,new Dictionary<string,object>{{"kind","video"},{"mime","video/mp4"},{"data",Convert.ToBase64String(File.ReadAllBytes(videoPath))}});Call(alice,"UpdateAttachment");await Send(alice,"a private video caption");
      await Wait(()=>AllText(bob.Get<StackPanel>("MessageList")).Contains("Open video"),"Encrypted MP4 reaches recipient's native chat");
      var videoButton=Controls<Button>(bob.Get<StackPanel>("MessageList")).First(x=>Convert.ToString(x.Content).Contains("Open video"));bool played=false,started=false;Exception videoFailure=null;DateTime until=DateTime.UtcNow.AddSeconds(12);
      var videoTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};videoTimer.Tick+=(s,e)=>{var viewer=Application.Current.Windows.Cast<Window>().FirstOrDefault(w=>w.Title=="Attachment");if(viewer==null)return;var player=Controls<MediaElement>(viewer).FirstOrDefault();if(player!=null && !started){player.Play();started=true;}if(player!=null && player.NaturalDuration.HasTimeSpan && player.Position.TotalMilliseconds>50){played=true;videoTimer.Stop();viewer.Close();}else if(DateTime.UtcNow>until){videoFailure=new Exception("MP4 playback did not advance on this Windows profile.");videoTimer.Stop();viewer.Close();}};videoTimer.Start();try{videoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}finally{videoTimer.Stop();}if(videoFailure!=null)throw videoFailure;
      Check(played,"Decrypted H.264/AAC MP4 actually opens and playback advances in Windows");Check(!Directory.GetFiles(System.IO.Path.Combine(bob.ProfileDir,"playback"),"illusion-*.mp4").Any(),"Closing video viewer deletes its temporary decrypted file");
      await NativeApi(alice,"PUT","/v1/chat-settings",new{peer=bid,ttlSeconds=60});await bob.Refresh();Check(bob.Get<TextBlock>("ComposerHint").Text.Contains("1 minute"),"Shared chat timer change appears in recipient's native composer");
      await alice.Refresh();await Send(alice,"expiring native cache fixture");await Wait(()=>AllText(bob.Get<StackPanel>("MessageList")).Contains("expiring native cache fixture"),"Expiring native message arrives");
      var expiring=((SortedDictionary<long,Dictionary<string,object>>)Field(bob,"messages")).Values.Last();expiring["expiresAt"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-1;Call(bob,"ExpireLocal");Call(bob,"SaveLocalState");Check(!AllText(bob.Get<StackPanel>("MessageList")).Contains("expiring native cache fixture"),"Native expiry removes rendered content and local decrypted cache");
      await NativeApi(alice,"PUT","/v1/chat-settings",new{peer=bid,ttlSeconds=0});await alice.Refresh();await bob.Refresh();
      var message=((SortedDictionary<long,Dictionary<string,object>>)Field(bob,"messages")).Values.First(m=>(string)m["senderId"]==aid && m["envelope"]!=null);
      var evidence=new{envelope=message["envelope"],material=Crypto.ReportMaterial(message,bid,(string)Field(bob,"privateKey"))};
      Check((bool)(await NativeApi(bob,"POST","/v1/reports",new{targetId=aid,messageId=message["id"],reason="spam",note="QA selected evidence",evidence=evidence}))["ok"],"Native selected-message key material verifies at relay for an opt-in report");
      Modal(()=>Call(bob,"ReportDialog",published,message,null),"Report",w=>Check(Controls<CheckBox>(w).Any(c=>c.IsChecked==false),"Native report evidence sharing defaults to unchecked"));
      var settings=(Dictionary<string,string>)Field(bob,"settings");settings["chatFont"]="Consolas";settings["textColor"]="#C5E8FF";settings["bubbleColor"]="#264766";Call(bob,"ApplyTheme");Call(bob,"UpdateMessageViews",false,true,true);Check(Controls<TextBlock>(bob.Get<StackPanel>("MessageList")).Any(x=>x.FontFamily.Source=="Consolas"),"Native chat font customization applies to message content");
      bob.Capture(System.IO.Path.GetFullPath("artifacts/illusion-privacy.png"));
    }
    static async Task Network(string control,bool online) {using(var client=new HttpClient(new HttpClientHandler{UseProxy=false}))using(var response=await client.GetAsync(control+(online?"/online":"/offline")))response.EnsureSuccessStatusCode();}
    static async Task Run(string server,string redirectServer) {
      root=System.IO.Path.GetFullPath(System.IO.Path.Combine(Environment.CurrentDirectory,"build","desktop-qa-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
      Directory.CreateDirectory("artifacts");
      var alice=new Messenger(System.IO.Path.Combine(root,"alice"));alice.Window.Show();await Task.Delay(200);
      alice.Capture(System.IO.Path.GetFullPath("artifacts/illusion-login.png"));
      Check(alice.Window.Title=="Illusion","Exact app identity");
      Check(AllText(alice.Window).Contains("Private."),"Requested tagline visible");
      var invitation=ConnectionProfile.Parse("{\"format\":\"illusion.connection\",\"version\":1,\"name\":\"Illusion friends\",\"relay\":\"https://relay.example.test/\"}");
      Check(invitation.Relay=="https://relay.example.test","Native invitation normalizes HTTPS connection");
      foreach(string url in new[]{"http://example.com","https://user:secret@example.com","https://example.com/path","https://example.com/?secret=value"}) {bool invalid=false;try{ConnectionProfile.NormalizeRelay(url);}catch{invalid=true;}Check(invalid,"Native invitation rejects unsafe endpoint "+url);}
      var connectionCheck=new Messenger(System.IO.Path.Combine(root,"connection-check"));Call(connectionCheck,"ApplyConnection",invitation);
      Check(connectionCheck.Get<TextBox>("ServerInput").Text=="https://relay.example.test","Invitation configures the connection without typing an address");
      var rememberedConnection=new Messenger(connectionCheck.ProfileDir);
      Check(rememberedConnection.Get<TextBox>("ServerInput").Text=="https://relay.example.test","Connection is remembered on subsequent app launches");
      Check(!rememberedConnection.Get<Expander>("ConnectionAdvanced").IsExpanded,"Technical connection fields stay out of the normal sign-in flow");
      var connectionSettings=(Dictionary<string,string>)Field(connectionCheck,"settings");connectionSettings["token"]="old-token-fixture";connectionSettings["account"]="old-account-fixture";connectionSettings["username"]="old-user";
      Call(connectionCheck,"ApplyConnection",new ConnectionProfile{Name="Different network",Relay="https://other.example.test"});
      Check(!connectionSettings.ContainsKey("token") && !connectionSettings.ContainsKey("account") && !connectionSettings.ContainsKey("username"),"Changing networks clears remembered credentials rather than forwarding them");
      connectionCheck.Window.Close();rememberedConnection.Window.Close();
      alice.Get<TextBox>("ServerInput").Text="http://example.com:3789";
      bool rejected=false;try {Call(alice,"SetServer");}catch(TargetInvocationException){rejected=true;}Check(rejected,"Internet HTTP is rejected before credentials leave device");
      alice.Get<TextBox>("ServerInput").Text=redirectServer;Call(alice,"SetServer");bool redirected=false;
      try {await (Task)Call(alice,"Api","GET","/redirect",null,false);}catch {redirected=true;}Check(redirected,"Server redirects are rejected before forwarding any request");
      alice.Get<TextBox>("ServerInput").Text=server;alice.Get<PasswordBox>("PasswordInput").Password="a strong testing password";
      foreach(string invalid in new[]{"a","аб","Ka","a.b","a b"}) {
        alice.Get<TextBox>("UsernameInput").Text=invalid;bool invalidRejected=false;
        try {await (Task)Call(alice,"SignIn");}catch(Exception e){invalidRejected=e.Message.StartsWith("Usernames must be 2–24");}
        Check(invalidRejected,"Native username validation rejects "+invalid);
      }
      Call(alice,"ApplyConnection",new ConnectionProfile{Name="Illusion test network",Relay=server});
      await Register(alice,"ai","Alice",server,true);
      Call(alice,"ShowNotificationToast","QA contact","New message");var toast=Field(alice,"notificationToast") as Window;Check(toast!=null&&toast.IsVisible&&AllText(toast).Contains("QA contact")&&AllText(toast).Contains("New message"),"Incoming desktop alerts use Illusion's own floating notification bar");Check(!typeof(Messenger).GetFields(BindingFlags.Instance|BindingFlags.NonPublic).Any(f=>f.FieldType.FullName=="System.Windows.Forms.NotifyIcon"),"Desktop message alerts do not invoke Windows notification balloons or their sound");await Task.Delay(250);CaptureWindow(toast,System.IO.Path.GetFullPath("artifacts/illusion-notification.png"));toast.Close();
      bool signedInSwitch=false;try{Call(alice,"ApplyConnection",invitation);}catch(TargetInvocationException){signedInSwitch=true;}Check(signedInSwitch,"A signed-in account cannot silently switch to another network");
      alice.Capture(System.IO.Path.GetFullPath("artifacts/illusion-empty.png"));
      var bob=new Messenger(System.IO.Path.Combine(root,"bob"));await Register(bob,"bob-ui_7","Bob",server);
      await Find(alice,"bob-ui_7");
      await Send(alice,"Hey! Our own little universe. ◇");
      await Find(bob,"ai");await bob.Refresh();
      Check(AllText(bob.Get<StackPanel>("MessageList")).Contains("Our own little universe"),"Bob decrypts Alice's message through native UI");
      await Send(bob,"No noise. Just us. Привет из Illusion!");
      await Wait(()=>AllText(alice.Get<StackPanel>("MessageList")).Contains("Привет из Illusion"),"Live message arrives without a manual refresh");
      Check(AllText(alice.Get<StackPanel>("MessageList")).Contains("Привет из Illusion"),"Alice decrypts Bob's Unicode reply");
      Check(!AllText(alice.Get<StackPanel>("MessageList")).Contains("Unable to verify"),"Message signatures and MACs verify in native UI");
      Check(Bubble(alice.Get<StackPanel>("MessageList"),"Our own little universe").ContextMenu.Items.Count==2,"Acknowledged message removes the queued-only action");
      var stable=alice.Get<StackPanel>("MessageList").Children[1];await alice.Refresh();
      Check(object.ReferenceEquals(stable,alice.Get<StackPanel>("MessageList").Children[1]),"Unchanged refresh preserves existing message controls");
      var stableChat=alice.Get<ListBox>("ChatList").Items[0];await alice.Refresh();
      Check(object.ReferenceEquals(stableChat,alice.Get<ListBox>("ChatList").Items[0]),"Unchanged refresh preserves conversation list items");
      alice.Window.Activate();alice.Get<ScrollViewer>("MessageScroll").ScrollToEnd();await Task.Delay(150);await (Task)Call(alice,"MarkVisibleRead");
      await Wait(()=>Convert.ToInt64(Field(bob,"readThrough"))>0,"Read receipt reaches the sender through live updates");
      var bobPeer=(Dictionary<string,object>)Field(alice,"peer");
      alice.Get<TextBox>("Composer").Text="a private unfinished draft";
      await alice.SelectPeer((Dictionary<string,object>)Field(alice,"me"));
      Check(alice.Get<TextBox>("Composer").Text=="","Draft does not leak into another conversation");
      await alice.SelectPeer(bobPeer);
      Check(alice.Get<TextBox>("Composer").Text=="a private unfinished draft","Switching conversations restores the correct draft");
      Check(!File.ReadAllText((string)Field(alice,"localStatePath")).Contains("private unfinished"),"Draft is protected on disk");
      alice.Get<TextBox>("Composer").Clear();
      var quote=Bubble(alice.Get<StackPanel>("MessageList"),"No noise. Just us.");
      ((MenuItem)quote.ContextMenu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
      Check(alice.Get<Border>("ReplyBar").Visibility==Visibility.Visible,"Reply context action opens the quote composer");
      await Send(alice,"An encrypted reply, in our own dream state.");
      await Wait(()=>AllText(bob.Get<StackPanel>("MessageList")).Contains("An encrypted reply"),"Encrypted reply arrives live");
      var replyBubble=Bubble(bob.Get<StackPanel>("MessageList"),"An encrypted reply");
      Check(AllText(replyBubble).Contains("No noise. Just us."),"Recipient decrypts the quoted reply");
      Click(alice,"SearchChatButton");alice.Get<TextBox>("ChatSearchInput").Text="encrypted reply";
      Check(alice.Get<TextBlock>("SearchCount").Text=="1 / 1","Conversation search finds decrypted text locally");Click(alice,"CloseSearchButton");
      var chatItems=(Dictionary<string,ListBoxItem>)Field(alice,"chatItems");string bobId=(string)bobPeer["id"];
      ((MenuItem)chatItems[bobId].ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
      Check(((HashSet<string>)Field(alice,"pinned")).Contains(bobId),"Conversation can be pinned");
      await Task.Delay(200);
      alice.Capture(System.IO.Path.GetFullPath("artifacts/illusion-chat.png"));
      // Long messages make a real scrollable thread without a synthetic UI fixture.
      for(int i=0;i<5;i++){alice.Get<TextBox>("Composer").Text="Scroll test "+i+" · "+string.Join(" ",Enumerable.Repeat("A quieter little universe.",35));await alice.Send();}
      alice.Get<ScrollViewer>("MessageScroll").UpdateLayout();alice.Get<ScrollViewer>("MessageScroll").ScrollToTop();await Task.Delay(150);
      Console.WriteLine("Reader before delivery: "+alice.Get<ScrollViewer>("MessageScroll").VerticalOffset+" / "+alice.Get<ScrollViewer>("MessageScroll").ScrollableHeight);
      await Send(bob,"A new message while you are reading above.");
      await Wait(()=>AllText(alice.Get<StackPanel>("MessageList")).Contains("while you are reading above"),"Live message arrives while reader is scrolled up");
      Console.WriteLine("Reader after delivery: "+alice.Get<ScrollViewer>("MessageScroll").VerticalOffset+" / "+alice.Get<ScrollViewer>("MessageScroll").ScrollableHeight);
      Check(alice.Get<ScrollViewer>("MessageScroll").VerticalOffset<10,"Incoming message preserves the reading position");
      Check(alice.Get<Button>("NewMessagesButton").Visibility==Visibility.Visible,"New-message jump button appears without scrolling the reader");Click(alice,"NewMessagesButton");
      await Wait(()=>((ICollection)Field(alice,"outbox")).Count==0,"Online sends finish before the outage test");
      await Network(redirectServer,false);
      await Wait(()=>!((bool)Field(alice,"sessionConnected")),"Connection outage is detected");
      await Send(alice,"A queued message that must survive restarting offline.");
      await Wait(()=>((ICollection)Field(alice,"outbox")).Count==1,"Offline send remains in the durable outbox");
      Check(AllText(alice.Get<StackPanel>("MessageList")).Contains("Queued"),"Queued message has an explicit delivery state");
      await Send(alice,"An unsent message whose retries I want to stop.");
      await Wait(()=>Convert.ToString(Field(alice,"activeSend"))=="","Outbox attempt finishes before retry cancellation");
      var cancelled=Bubble(alice.Get<StackPanel>("MessageList"),"whose retries I want to stop");
      ((MenuItem)cancelled.ContextMenu.Items[cancelled.ContextMenu.Items.Count-1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
      Check(((ICollection)Field(alice,"outbox")).Count==1,"Stopping retries removes only the chosen unsent envelope");
      alice.Get<TextBox>("Composer").Text="a draft surviving an offline restart";string offlineProfile=alice.ProfileDir;alice.Window.Close();
      alice=new Messenger(offlineProfile);alice.Window.Show();
      await Wait(()=>alice.Get<Border>("AuthOverlay").Visibility==Visibility.Collapsed,"Protected workspace opens while the server is offline");
      await Wait(()=>alice.Get<TextBox>("Composer").Text=="a draft surviving an offline restart","Draft survives an offline restart");
      await Wait(()=>AllText(alice.Get<StackPanel>("MessageList")).Contains("Привет из Illusion"),"Recent history decrypts from the local cache while offline");
      Check(((ICollection)Field(alice,"outbox")).Count==1,"Unsent envelope survives an offline restart");
      Check(((HashSet<string>)Field(alice,"pinned")).Contains(bobId),"Pinned conversation survives restart");
      await Network(redirectServer,true);
      await Wait(()=>((ICollection)Field(alice,"outbox")).Count==0,"Reconnection drains the durable outbox automatically");
      await Wait(()=>AllText(bob.Get<StackPanel>("MessageList")).Contains("must survive restarting offline"),"Queued message reaches the recipient after reconnection");
      Check(alice.Get<TextBox>("Composer").Text=="a draft surviving an offline restart","Background delivery preserves the current draft");
      using(var control=new HttpClient(new HttpClientHandler{UseProxy=false}))using(var response=await control.GetAsync(redirectServer+"/lose-ack"))response.EnsureSuccessStatusCode();
      await Send(alice,"Exactly once, even when the delivery acknowledgement is lost.");
      await Wait(()=>((ICollection)Field(alice,"outbox")).Count==0,"Lost acknowledgement is recovered without manual resending");
      await Wait(()=>AllText(bob.Get<StackPanel>("MessageList")).Contains("Exactly once, even"),"Recipient receives the message despite a lost acknowledgement");
      Check(AllText(bob.Get<StackPanel>("MessageList")).Split(new[]{"Exactly once, even"},StringSplitOptions.None).Length==2,"Lost acknowledgement does not duplicate the displayed message");
      var privacy=(Dictionary<string,string>)Field(alice,"settings");privacy["receipts"]="false";await bob.Refresh();long privateRead=Convert.ToInt64(Field(bob,"readThrough"));
      alice.Window.Activate();await Send(bob,"A message read with receipt sharing disabled.");
      await Wait(()=>AllText(alice.Get<StackPanel>("MessageList")).Contains("receipt sharing disabled"),"Messages remain readable with read receipts disabled");
      alice.Get<ScrollViewer>("MessageScroll").ScrollToEnd();await Task.Delay(150);await (Task)Call(alice,"MarkVisibleRead");await bob.Refresh();
      Check(Convert.ToInt64(Field(bob,"readThrough"))==privateRead,"Native privacy setting prevents advancing the shared read receipt");privacy["receipts"]="true";
      Call(alice,"StopExperience");Call(bob,"StopExperience");((System.Windows.Threading.DispatcherTimer)Field(alice,"timer")).Stop();((System.Windows.Threading.DispatcherTimer)Field(bob,"timer")).Stop();
      // Populate real authenticated history without running a polling cycle for each seed.
      for(int i=0;i<105;i++) {
        var writer=i%2==0?alice:bob;var reader=i%2==0?bob:alice;
        var writerUser=(Dictionary<string,object>)Field(writer,"me");var readerUser=(Dictionary<string,object>)Field(reader,"me");string clientId=Guid.NewGuid().ToString();
        var historyEnvelope=Crypto.Encrypt("History anchor "+i+" · "+string.Join(" ",Enumerable.Repeat("A quieter universe.",8)),(string)writerUser["id"],(string)readerUser["id"],clientId,(string)Field(writer,"privateKey"),(string)readerUser["publicKey"]);
        await (Task)Call(writer,"Api","POST","/v1/messages",new{recipientId=readerUser["id"],clientId=clientId,envelope=historyEnvelope},true);
      }
      var cachedHistory=(Dictionary<string,List<Dictionary<string,object>>>)Field(alice,"history");cachedHistory.Remove(bobId);
      await alice.SelectPeer((Dictionary<string,object>)Field(alice,"me"));await alice.SelectPeer(bobPeer);
      Check(((IDictionary)Field(alice,"messages")).Count==100,"Opening uncached long history loads a bounded page");
      Check(alice.Get<Button>("OlderButton").Visibility==Visibility.Visible,"Older history is available after the first page");
      var historyScroll=alice.Get<ScrollViewer>("MessageScroll");historyScroll.UpdateLayout();historyScroll.ScrollToVerticalOffset(160);await Task.Delay(150);
      var anchor=alice.Get<StackPanel>("MessageList").Children.OfType<StackPanel>().Select(p=>(FrameworkElement)p.Children[p.Children.Count-1]).First(p=>p.TranslatePoint(new Point(0,p.ActualHeight),historyScroll).Y>0);double anchorBefore=anchor.TranslatePoint(new Point(),historyScroll).Y;
      await (Task)Call(alice,"LoadOlder");await Task.Delay(150);double anchorAfter=anchor.TranslatePoint(new Point(),historyScroll).Y;
      Console.WriteLine("Visible history anchor: "+anchorBefore+" -> "+anchorAfter);
      Check(((IDictionary)Field(alice,"messages")).Count>100,"Older page extends actual authenticated history");
      Check(Math.Abs(anchorAfter-anchorBefore)<3,"Prepending older messages preserves the visible reading anchor");
      Check(!AllText(alice.Get<StackPanel>("MessageList")).Contains("Unable to verify"),"Loaded history pages retain valid message encryption");
      Call(alice,"StartExperience");Call(bob,"StartExperience");((System.Windows.Threading.DispatcherTimer)Field(alice,"timer")).Start();((System.Windows.Threading.DispatcherTimer)Field(bob,"timer")).Start();
      string key=(string)Field(alice,"privateKey"),pub=Crypto.Public(key);
      var backup=Crypto.Backup(key,"a long backup passphrase");
      Check(Crypto.Public(Crypto.Restore(backup,"a long backup passphrase"))==pub,"Password-protected key backup round trip");
      var legacyKey=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText("tests/fixtures/legacy-key.json"));Check(Crypto.Public(Crypto.Restore((Dictionary<string,object>)legacyKey["backup"],(string)legacyKey["password"]))==(string)legacyKey["publicKey"],"Native client still restores the original version-1 protected backup format");
      bool wrong=false;try{Crypto.Restore(backup,"incorrect backup passphrase");}catch(System.Security.Cryptography.CryptographicException){wrong=true;}Check(wrong,"Wrong key-backup password fails closed");
      Check(Crypto.Unprotect(Crypto.Protect(key))==key,"Windows DPAPI key protection round trip");
      byte[] bomb=new byte[33];byte[] pngHeader=new byte[]{137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82};Array.Copy(pngHeader,bomb,pngHeader.Length);bomb[19]=1;bomb[20]=0;bomb[21]=15;bomb[22]=66;bomb[23]=64;int imageWidth,imageHeight;bool rejectedImage=false;try{ImageSafety.Dimensions(bomb,out imageWidth,out imageHeight);}catch{rejectedImage=true;}Check(rejectedImage,"Native image dimension guard rejects a tiny oversized-image header before decoding");
      bool rejectedXml=false;try{Crypto.Public("<!DOCTYPE RSAKeyValue>"+pub);}catch(System.Security.Cryptography.CryptographicException){rejectedXml=true;}Check(rejectedXml,"Native RSA parser rejects active XML declarations");
      var me=(Dictionary<string,object>)Field(alice,"me");var peer=(Dictionary<string,object>)Field(alice,"peer");
      string mid=Guid.NewGuid().ToString();
      var envelope=Crypto.Encrypt("Integrity test",(string)me["id"],(string)peer["id"],mid,key,(string)peer["publicKey"]);
      var message=new Dictionary<string,object>{{"senderId",me["id"]},{"recipientId",peer["id"]},{"clientId",mid},{"envelope",envelope}};
      Check(Crypto.Decrypt(message,(string)me["id"],key,pub)=="Integrity test","Native encryption round trip");
      var unsafeEnvelope=Crypto.Encrypt("{\"format\":\"illusion.message.v2\",\"text\":\""+new string('x',2001)+"\"}",(string)me["id"],(string)peer["id"],mid,key,(string)peer["publicKey"],new Dictionary<string,object>{{"viewOnce",false},{"ttlSeconds",0}});
      var unsafeMessage=new Dictionary<string,object>{{"senderId",me["id"]},{"recipientId",peer["id"]},{"clientId",mid},{"envelope",unsafeEnvelope}};var invalidContent=Call(alice,"Decode",unsafeMessage,(string)me["id"],key,pub,false);Check(!(bool)invalidContent.GetType().GetField("Valid").GetValue(invalidContent),"Native client rejects oversized text even when encryption and signatures are valid");
      var guardedFont=(FontFamily)typeof(Messenger).GetMethod("ProfileFont",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{"Unlisted Synthetic Font"});Check(guardedFont.Source=="Segoe UI","Untrusted profile font names are restricted to the shared system-font list");
      envelope["ciphertext"]=Convert.ToBase64String(new byte[16]);bool tampered=false;
      try{Crypto.Decrypt(message,(string)me["id"],key,pub);}catch(System.Security.Cryptography.CryptographicException){tampered=true;}Check(tampered,"Tampered native message is rejected");
      var settings=(Dictionary<string,string>)Field(alice,"settings");settings["accent"]="#297C91";settings["font"]="16";settings["compact"]="true";Call(alice,"SaveSettings");Call(alice,"ApplyTheme");
      Check(((SolidColorBrush)alice.Window.Resources["Accent"]).Color==(Color)ColorConverter.ConvertFromString("#297C91"),"Accent customization applies");
      Click(alice,"SavedButton");await Wait(()=>alice.Get<TextBlock>("ChatTitle").Text=="Saved messages","Saved messages opens");await Task.Delay(250);await Send(alice,"Remember to back up the encryption key.");
      Check(AllText(alice.Get<StackPanel>("MessageList")).Contains("Remember to back up"),"Saved message is encrypted and readable");
      await PrivacyChecks(alice,bob);AppearanceChecks(alice);
      string aliceProfile=alice.ProfileDir;alice.Window.Close();bob.Window.Close();
      var resumed=new Messenger(aliceProfile);resumed.Window.Show();
      await Wait(()=>resumed.Get<Border>("AuthOverlay").Visibility==Visibility.Collapsed,"Session resumes after native app restart");
      Check(((SolidColorBrush)resumed.Window.Resources["Accent"]).Color==(Color)ColorConverter.ConvertFromString("#297C91"),"Appearance persists after restart");
      Click(resumed,"SavedButton");await Task.Delay(600);
      Check(AllText(resumed.Get<StackPanel>("MessageList")).Contains("Remember to back up"),"Saved history decrypts after restart");
      await (Task)Call(resumed,"Api","POST","/v1/logout",new{},true);var staleResponse=(Task<Dictionary<string,object>>)Call(resumed,"Api","GET","/v1/me",null,true);System.Threading.Thread.Sleep(80);Call(resumed,"SignOutLocal");bool staleRejected=false;try{await staleResponse;}catch(OperationCanceledException){staleRejected=true;}Check(staleRejected,"A completed old-session response cannot affect the next native sign-in");
      Check(resumed.Get<Border>("AuthOverlay").Visibility==Visibility.Visible,"Sign out returns to account screen");
      Check(!File.ReadAllText(System.IO.Path.Combine(aliceProfile,"settings.json")).Contains("\"token\""),"Sign out clears locally stored session token");
      resumed.Get<TextBox>("UsernameInput").Text="AI";resumed.Get<PasswordBox>("PasswordInput").Password="a strong testing password";await Wait(()=>resumed.Get<Button>("AuthButton").IsEnabled,"Native sign-in button is enabled after logout");Click(resumed,"AuthButton");
      try{await Wait(()=>resumed.Get<Border>("AuthOverlay").Visibility==Visibility.Collapsed,"Native login accepts two-character uppercase username");}catch(Exception e){throw new Exception(e.Message+" / Auth: "+resumed.Get<TextBlock>("AuthError").Text+" / Status: "+resumed.Get<TextBlock>("StatusLabel").Text,e);}
      Check(resumed.Get<TextBlock>("IdentityLabel").Text=="@ai","Native login preserves case-insensitive identity");resumed.Window.Close();
      File.WriteAllText("artifacts/desktop-qa.txt",checks+" native desktop checks passed.\nUI screenshots are generated by the actual WPF renderer.\nTest profiles: "+root+"\n");
      Console.WriteLine(checks+" native desktop checks passed.");
    }
    [STAThread] public static int Main(string[] args) {
      if(args.Length!=2){Console.Error.WriteLine("Pass isolated test and redirect server URLs.");return 2;}
      var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};int exit=0;
      app.Startup+=async(s,e)=>{try{await Run(args[0],args[1]);}catch(Exception error){Console.Error.WriteLine(error);exit=1;}finally{app.Shutdown();}};
      app.Run();return exit;
    }
  }
}
