using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Illusion {
  public class ApiException : Exception { public int Status; public ApiException(int status,string message):base(message){Status=status;} }
  public partial class Messenger {
    public Window Window;
    public string ProfileDir;
    readonly JavaScriptSerializer json=new JavaScriptSerializer { MaxJsonLength=32000000 };
    readonly HttpClient http=new HttpClient(new HttpClientHandler { UseProxy=false, UseCookies=false, AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(12), MaxResponseContentBufferSize=12000000 };
    readonly Dictionary<string,string> settings=new Dictionary<string,string>();
    readonly List<Action> languageBindings=new List<Action>();
    ArrayList translationEnglish;
    ArrayList translationLanguages;
    Dictionary<string,object> translationRows;
    string currentLanguage="en";
    readonly SortedDictionary<long,Dictionary<string,object>> messages=new SortedDictionary<long,Dictionary<string,object>>();
    Dictionary<string,object> me,peer;
    string token="",server="",privateKey="";
    bool register=false, sending=false, closing=false;
    int generation=0;
    DispatcherTimer timer;
    public T Get<T>(string name) where T:class { return Window.FindName(name) as T; }
    public Messenger(string profileDir=null) {
      ProfileDir=profileDir??System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Illusion");
      Directory.CreateDirectory(ProfileDir);
      using(var stream=typeof(Messenger).Assembly.GetManifestResourceStream("Main.xaml")) Window=(Window)XamlReader.Load(stream);
      using(var stream=typeof(Messenger).Assembly.GetManifestResourceStream("Illusion.ico")) if(stream!=null)Window.Icon=BitmapFrame.Create(stream);
      try { if(File.Exists(ConfigPath)) foreach(var item in json.Deserialize<Dictionary<string,string>>(File.ReadAllText(ConfigPath))) settings[item.Key]=item.Value; } catch { }
      InitializeLocalization();CaptureLocalizedElements(Window.Content as DependencyObject,false);ApplyWindowLanguage();
      Get<TextBlock>("VersionLabel").Text=BuildInfo.Version;Get<Button>("AuthButton").Content=T("Sign in");Get<Button>("SwitchAuthButton").Content=T("Create an account");Get<Button>("TermsLink").Content=T("Terms");Get<Button>("PrivacyLink").Content=T("Privacy");
      InitializeConnection();
      Get<TextBox>("UsernameInput").Text=Setting("username","");
      Get<CheckBox>("LanConsent").IsChecked=Setting("lan","false")=="true";
      ApplyTheme(); DrawAura();
      Get<Canvas>("ChatWallpaper").SizeChanged+=(s,e)=>DrawWallpaper();
      Get<Button>("SwitchAuthButton").Click+=(s,e)=>ToggleAuth();
      Get<Button>("AuthButton").Click+=async(s,e)=>await Guard(SignIn);
      Get<PasswordBox>("PasswordInput").KeyDown+=async(s,e)=>{if(e.Key==Key.Enter) await Guard(SignIn);};
      Get<Button>("FindButton").Click+=async(s,e)=>await Guard(FindPeer);
      Get<TextBox>("SearchInput").KeyDown+=async(s,e)=>{if(e.Key==Key.Enter)await Guard(FindPeer);};
      Get<ListBox>("ChatList").SelectionChanged+=async(s,e)=>{
        var item=Get<ListBox>("ChatList").SelectedItem as ListBoxItem;
        if(!updatingChats && item!=null && item.Tag is Dictionary<string,object>){
          var target=(Dictionary<string,object>)item.Tag;
          if(target.ContainsKey("username"))await Guard(()=>SelectPeer(target));else await Guard(()=>SelectGroup(target));
        }
      };
      Get<Button>("SavedButton").Click+=async(s,e)=>{if(me!=null)await Guard(()=>SelectPeer(me));};
      Get<Button>("ChatsButton").Click+=(s,e)=>Get<TextBox>("SearchInput").Focus();
      Get<Button>("SendButton").Click+=async(s,e)=>await Guard(Send);
      Get<TextBox>("Composer").PreviewKeyDown+=async(s,e)=>{if(e.Key==Key.Enter && (Keyboard.Modifiers&ModifierKeys.Shift)==0){e.Handled=true;await Guard(Send);}};
      Get<Button>("OlderButton").Click+=async(s,e)=>await Guard(LoadOlder);
      Get<Button>("VerifyButton").Click+=(s,e)=>ShowSafety();
      Get<Button>("SettingsButton").Click+=(s,e)=>ShowSettings();
      Get<Button>("ImportKeyButton").Click+=async(s,e)=>await Guard(ImportKey);
      Get<Button>("RecoverButton").Click+=(s,e)=>ShowAccountRecovery();
      Window.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.K && Keyboard.Modifiers==ModifierKeys.Control){Get<TextBox>("SearchInput").Focus();e.Handled=true;}};
      timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
      timer.Tick+=async(s,e)=>await Guard(ExperienceTick);
      InitializeExperience();
      InitializeGroups();InitializePrivacy();InitializeConsent();
      Window.Closed+=(s,e)=>{StopExperience();closing=true;generation++;timer.Stop();http.Dispose();};
      Window.ContentRendered+=async(s,e)=>{await Guard(Resume);CheckForUpdates();};
    }
    string ConfigPath { get { return System.IO.Path.Combine(ProfileDir,"settings.json"); } }
    string Setting(string key,string fallback) { string value; return settings.TryGetValue(key,out value)?value:fallback; }
    void InitializeLocalization(){
      using(var stream=typeof(Messenger).Assembly.GetManifestResourceStream("translations.json"))if(stream!=null)using(var reader=new StreamReader(stream)){var catalog=json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());translationEnglish=(ArrayList)catalog["english"];translationLanguages=(ArrayList)catalog["languages"];translationRows=(Dictionary<string,object>)catalog["rows"];}
      currentLanguage=Setting("language","en");if(currentLanguage!="en" && (translationRows==null || !translationRows.ContainsKey(currentLanguage)))currentLanguage="en";
    }
    string T(string source){if(translationEnglish==null || translationRows==null || currentLanguage=="en")return source;string core=source,prefix="",suffix="";char[] decorations={'◇','↓','+','＋','●','○','⚪','◷','✓','•','↑','×','▧','▶','◈'};while(core.Length>0 && Array.IndexOf(decorations,core[0])>=0){prefix+=core[0];core=core.Substring(1).TrimStart();}if(core.EndsWith("↗")){core=core.Substring(0,core.Length-1).TrimEnd();suffix="  ↗";}int index=translationEnglish.IndexOf(core);if(index<0){core=System.Text.RegularExpressions.Regex.Replace(core,"\\s+"," ").Trim();index=translationEnglish.IndexOf(core);if(index<0)index=NormalizedIndex(core);}if(index<0)return source;ArrayList values=translationRows[currentLanguage] as ArrayList;return values!=null && index<values.Count?prefix+Convert.ToString(values[index])+suffix:source;}
    Dictionary<string,int> normalizedIndex;
    int NormalizedIndex(string core){if(normalizedIndex==null){normalizedIndex=new Dictionary<string,int>();for(int i=0;i<translationEnglish.Count;i++){string key=System.Text.RegularExpressions.Regex.Replace(Convert.ToString(translationEnglish[i]),"\\s+"," ").Trim().Replace('’','\'');if(!normalizedIndex.ContainsKey(key))normalizedIndex[key]=i;}}int index;return normalizedIndex.TryGetValue(core.Replace('’','\''),out index)?index:-1;}
    void BindLanguage(string source,Action<string> setter){languageBindings.Add(()=>setter(T(source)));}
    void CaptureLocalizedElements(DependencyObject root,bool excluded){
      if(root==null)return;var element=root as FrameworkElement;if(element!=null){bool skip=excluded || new[]{"ChatList","MessageList","ChatTitle","ChatSubtitle","IdentityLabel","ConnectionLabel","StatusLabel","AuthError","AuthHeading","AuthButton","SwitchAuthButton","TermsLink","PrivacyLink","ComposerHint","CharacterCount","AttachmentLabel","ReplyText","NoticeText","SearchCount","NetworkDescription","VersionLabel","Avatar","Composer","UsernameInput","PasswordInput","InviteInput","ServerInput","SearchInput","ChatSearchInput"}.Contains(element.Name);if(!skip){var text=element as TextBlock;if(text!=null && !string.IsNullOrEmpty(text.Text)){string original=text.Text;BindLanguage(original,value=>text.Text=value);}var button=element as Button;if(button!=null && button.Content is string){string original=(string)button.Content;BindLanguage(original,value=>button.Content=value);}var check=element as CheckBox;if(check!=null && check.Content is string){string original=(string)check.Content;BindLanguage(original,value=>check.Content=value);}var expander=element as Expander;if(expander!=null && expander.Header is string){string original=(string)expander.Header;BindLanguage(original,value=>expander.Header=value);}if(element.ToolTip is string){string original=(string)element.ToolTip;BindLanguage(original,value=>element.ToolTip=value);}}
      }
      foreach(object child in LogicalTreeHelper.GetChildren(root))if(child is DependencyObject)CaptureLocalizedElements((DependencyObject)child,excluded || (element!=null && (element.Name=="ChatList" || element.Name=="MessageList")));
    }
    void RelocalizeEverything(){ApplyWindowLanguage();Get<Button>("FindButton").Content="+  "+T("Start a conversation");Get<Button>("AuthButton").Content=T("Sign in");Get<Button>("SwitchAuthButton").Content=T("Create an account");UpdateAttachment();if(me==null)return;UpdateChats(latestChats);if(group!=null){Get<TextBlock>("ChatTitle").Text=GroupName(group);Get<TextBlock>("ChatSubtitle").Text=GroupSubtitle(group);Get<Button>("ChatOptionsButton").Content=T("Group settings");UpdateTimerHint();UpdateGroupEventsPanel();}else if(peer!=null){Get<TextBlock>("ChatTitle").Text=(string)peer["id"]==(string)me["id"]?T("Saved messages"):Convert.ToString(peer["displayName"]);Get<TextBlock>("ChatSubtitle").Text=ChatSubtitleText(peer);Get<Button>("ChatOptionsButton").Content=T("Profile / Timer");UpdateTimerHint();}UpdateMessageViews(false,true);Connection(sessionConnected);}
    void ApplyWindowLanguage(){Window.FlowDirection=Array.IndexOf(new[]{"ar","he","fa"},currentLanguage)>=0?FlowDirection.RightToLeft:FlowDirection.LeftToRight;foreach(var apply in languageBindings)apply();Get<Button>("OlderButton").Content=T("Load earlier messages");Get<Button>("FindButton").Content="+  "+T("Start a conversation");Get<TextBlock>("VersionLabel").Text=BuildInfo.Version;}
    sealed class LanguageChoice{public string Code{get;set;}public string Name{get;set;}public override string ToString(){return Name;}}
    List<LanguageChoice> GetLanguageChoices(){var result=new List<LanguageChoice>();if(translationLanguages!=null)foreach(ArrayList item in translationLanguages)if(item.Count>=2)result.Add(new LanguageChoice{Code=Convert.ToString(item[0]),Name=Convert.ToString(item[1])});return result;}
    void SaveSettings() { File.WriteAllText(ConfigPath+".tmp",json.Serialize(settings)); if(File.Exists(ConfigPath))File.Replace(ConfigPath+".tmp",ConfigPath,null);else File.Move(ConfigPath+".tmp",ConfigPath); }
    string KeyPath(string username) { return System.IO.Path.Combine(ProfileDir,"key-"+Crypto.Fingerprint(server+"/"+username.ToLowerInvariant())+".dat"); }
    string PinPath(string id) { return System.IO.Path.Combine(ProfileDir,"pin-"+Crypto.Fingerprint(server+"/"+id)+".txt"); }
    void Pin(Dictionary<string,object> user) {
      string path=PinPath((string)user["id"]),value=Crypto.Fingerprint((string)user["publicKey"]);
      if(File.Exists(path) && File.ReadAllText(path)!=value) throw new Exception("This account's encryption key changed. Connection stopped. Verify the key directly with your contact.");
      if(!File.Exists(path))File.WriteAllText(path,value);
    }
    async Task Guard(Func<Task> task) {
      int gen=generation;
      try { await task(); } catch(Exception e) {
        if(closing || gen!=generation)return;
        string message=e is TaskCanceledException?"The server took too long to respond. Check its address and connection.":e is HttpRequestException?"Cannot reach the server. Make sure it is running and the address is correct.":e.Message;
        Get<TextBlock>("StatusLabel").Text=message;
        ShowNotice(message,true);
        Get<TextBlock>("AuthError").Text=message;
        var api=e as ApiException;
        if(api!=null && api.Status==401 && me!=null) { SignOutLocal(); Get<TextBlock>("AuthError").Text="Your session expired. Sign in again."; }
      }
    }
    void SetServer() {
      Uri uri;
      string value=Get<TextBox>("ServerInput").Text.Trim().TrimEnd('/');
      if(!Uri.TryCreate(value,UriKind.Absolute,out uri) || (uri.Scheme!="http" && uri.Scheme!="https") || uri.AbsolutePath!="/" || uri.Query!="" || uri.Fragment!="" || uri.UserInfo!="")throw new Exception("Enter a server address such as https://your-server:3789.");
      if(uri.Scheme=="http" && !uri.IsLoopback) {
        IPAddress ip;
        bool local=false;
        if(IPAddress.TryParse(uri.Host,out ip)) { var bytes=ip.GetAddressBytes(); local=bytes.Length==4 && (bytes[0]==10 || (bytes[0]==192&&bytes[1]==168) || (bytes[0]==172&&bytes[1]>=16&&bytes[1]<=31)); }
        if(!local || Get<CheckBox>("LanConsent").IsChecked!=true)throw new Exception("Use HTTPS for internet servers. For a trusted LAN, use its private IPv4 address and allow local HTTP.");
      }
      server=uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
      ServicePointManager.FindServicePoint(uri).ConnectionLimit=12;
    }
    async Task<Dictionary<string,object>> Api(string method,string path,object body=null,bool auth=true) {
      int requestGeneration=generation;
      using(var req=new HttpRequestMessage(new HttpMethod(method),server+path)) {
        if(auth && token!="")req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        if(body!=null)req.Content=new StringContent(json.Serialize(body),Encoding.UTF8,"application/json");
        using(var response=await http.SendAsync(req,auth && sessionStop!=null?sessionStop.Token:CancellationToken.None)) {
          string text=await response.Content.ReadAsStringAsync();if(requestGeneration!=generation)throw new OperationCanceledException("Session changed."); Dictionary<string,object> data;
          try { data=json.Deserialize<Dictionary<string,object>>(text); if(data==null)throw new Exception(); }
          catch { throw new Exception("The address did not return an Illusion response."); }
          if(!response.IsSuccessStatusCode)throw new ApiException((int)response.StatusCode,data.ContainsKey("error")?(string)data["error"]:"Request failed.");
          return data;
        }
      }
    }
    void ToggleAuth() {
      register=!register;
      Get<StackPanel>("RegisterFields").Visibility=register?Visibility.Visible:Visibility.Collapsed;
      Get<TextBlock>("AuthHeading").Text=T(register?"Your people. Your space.":"Sign in");
      Get<Button>("AuthButton").Content=T(register?"Create an account":"Sign in");
      Get<Button>("SwitchAuthButton").Content=T(register?"Sign in":"Create an account");
      Get<TextBlock>("AuthError").Text="";
    }
    async Task SignIn() {
      if(!Get<Button>("AuthButton").IsEnabled)return;
      int signInGeneration=generation;
      Get<Button>("AuthButton").IsEnabled=false;
      Get<Button>("SwitchAuthButton").IsEnabled=false;
      Get<Button>("ImportKeyButton").IsEnabled=false;
      Get<Button>("ImportConnectionButton").IsEnabled=false;
      try {
        SetServer();
        string username=Get<TextBox>("UsernameInput").Text.Trim();
        string password=Get<PasswordBox>("PasswordInput").Password;
        if(!System.Text.RegularExpressions.Regex.IsMatch(username,@"\A[A-Za-z0-9_-]{2,24}\z"))throw new Exception("Usernames must be 2–24 characters: Latin letters (A–Z), numbers, _ or -.");
        if(password.Length<10)throw new Exception("Use a password of at least 10 characters.");
        if(register && (Get<ComboBox>("AgeBand").SelectedIndex!=4 || Get<CheckBox>("AcceptTerms").IsChecked!=true || Get<CheckBox>("AcknowledgePrivacy").IsChecked!=true))throw new Exception("This preview is for adults 18+. Read the policies and select both acknowledgements before registering.");
        username=username.ToLowerInvariant();
        string keyPath=KeyPath(username);
        string identity;
        if(File.Exists(keyPath)) identity=Crypto.Unprotect(File.ReadAllText(keyPath));
        else if(register) {
          Get<TextBlock>("AuthError").Text="Creating your encryption key…";
          identity=await Task.Run(()=>Crypto.NewKey());if(signInGeneration!=generation||closing)return;
          File.WriteAllText(keyPath,Crypto.Protect(identity));
        } else throw new Exception("This device needs your encryption key. Restore a .illusionkey backup, or sign in on the device where you created the account.");
        var b=new Dictionary<string,object>{{"username",username},{"password",password}};
        if(register){b["ageBand"]="18+";b["acceptTerms"]=true;b["acknowledgePrivacy"]=true;b["policyVersion"]=PolicyVersion;b["displayName"]=Get<TextBox>("DisplayNameInput").Text.Trim();b["publicKey"]=Crypto.Public(identity);b["registrationCode"]=Get<PasswordBox>("InviteInput").Password;}
        var response=await Api("POST",register?"/v1/register":"/v1/login",b,false);
        var user=(Dictionary<string,object>)response["user"];
        if(signInGeneration!=generation||closing)return;if(Crypto.Public(identity)!=(string)user["publicKey"])throw new Exception("Your local encryption key doesn't match this account. Restore the correct backup.");
        privateKey=identity;me=user;token=(string)response["token"];Pin(me);
        settings["server"]=server;settings["username"]=username;settings["token"]=Crypto.Protect(token);settings["account"]=Crypto.Protect(json.Serialize(me));settings["lan"]=Get<CheckBox>("LanConsent").IsChecked==true?"true":"false";SaveSettings();
        Get<PasswordBox>("PasswordInput").Clear();Get<PasswordBox>("InviteInput").Clear();
        Enter(); await Refresh();
      } finally {Get<Button>("AuthButton").IsEnabled=true;Get<Button>("SwitchAuthButton").IsEnabled=true;Get<Button>("ImportKeyButton").IsEnabled=true;Get<Button>("ImportConnectionButton").IsEnabled=true;}
    }
    async Task Resume() {
      if(Setting("token","")=="")return;
      Get<Button>("AuthButton").IsEnabled=false;Get<Button>("SwitchAuthButton").IsEnabled=false;Get<Button>("ImportConnectionButton").IsEnabled=false;
      try {
        SetServer();token=Crypto.Unprotect(settings["token"]);
        var response=await Api("GET","/v1/me");var user=(Dictionary<string,object>)response["user"];
        privateKey=Crypto.Unprotect(File.ReadAllText(KeyPath((string)user["username"])));
        if(Crypto.Public(privateKey)!=(string)user["publicKey"])throw new Exception("Restore the correct encryption key for this account.");
        Pin(user);me=user;settings["account"]=Crypto.Protect(json.Serialize(user));SaveSettings();Enter();
      } catch(Exception error) {
        var api=error as ApiException;
        bool offline=error is HttpRequestException || error is TaskCanceledException || (api!=null && (api.Status>=500 || api.Status==429));
        if(offline && Setting("account","")!="") {
          try {
            var user=json.Deserialize<Dictionary<string,object>>(Crypto.Unprotect(settings["account"]));
            privateKey=Crypto.Unprotect(File.ReadAllText(KeyPath((string)user["username"])));
            if(Crypto.Public(privateKey)!=(string)user["publicKey"])throw new Exception("Encryption key mismatch.");
            token=Crypto.Unprotect(settings["token"]);Pin(user);me=user;Enter();Connection(false);ShowNotice("Offline workspace · Your recent messages, drafts, and outbox are available. Reconnecting automatically.",false);return;
          } catch { }
        }
        timer.Stop();if(sessionStop!=null)sessionStop.Cancel();token="";me=null;peer=null;Get<Border>("AuthOverlay").Visibility=Visibility.Visible;Get<TextBlock>("AuthError").Text="Sign in to reconnect. Your local encryption key is safe.";
      } finally {Get<Button>("AuthButton").IsEnabled=true;Get<Button>("SwitchAuthButton").IsEnabled=true;Get<Button>("ImportConnectionButton").IsEnabled=true;}
    }
    void Enter() {
      LoadLocalState();
      Get<Border>("AuthOverlay").Visibility=Visibility.Collapsed;
      Get<TextBlock>("IdentityLabel").Text="@"+(string)me["username"];
      Get<TextBlock>("Avatar").Text=((string)me["displayName"]).Substring(0,1).ToUpperInvariant();
      UpdateSelfProfile();
      Get<TextBlock>("StatusLabel").Text=T("Connected · Message text encrypted on your device.");
      Get<TextBlock>("AuthError").Text="";StartExperience();timer.Start();
    }
    public Task Refresh() { return RefreshExperience(); }
    async Task FindPeer() {
      if(me==null)return;
      int gen=generation;
      string name=Get<TextBox>("SearchInput").Text.Trim().TrimStart('@');
      var response=await Api("GET","/v1/users?username="+Uri.EscapeDataString(name));
      if(gen!=generation || me==null)return;
      await SelectPeer((Dictionary<string,object>)response["user"]);
    }
    public Task SelectPeer(Dictionary<string,object> user) { return SelectConversation(user); }
    Task LoadMessages(bool initial) { return FetchConversation(initial); }
    Task LoadOlder() { return group!=null?FetchGroupOlder():FetchOlder(); }
    public Task Send() { return group!=null?QueueGroupMessage():QueueMessage(); }
    static SolidColorBrush Brush(string color) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
    void RenderMessages(bool bottom) { UpdateMessageViews(bottom,false); }
    void ApplyTheme() {ApplyPalette();ApplyUiStyle();string[] palette;if(!ThemeColors.TryGetValue(CurrentThemeId(),out palette))palette=ThemeColors["blue"];string saved=Setting("accent",palette[4]);if(string.IsNullOrEmpty(Setting("theme",""))&&(saved=="#297C91"||saved=="#56657C"))saved=palette[4];Window.Resources["Accent"]=Brush(saved);Window.FontFamily=new FontFamily(Setting("chatFont","Segoe UI"));}
    static readonly Dictionary<string,string> AuraCaptions=new Dictionary<string,string>{{"blue","ドリームステート / DREAM STATE"},{"astraios","アストライオス / ASTRAIOS"},{"crimson-moon","クリムゾンムーン / CRIMSON MOON"},{"dreammetric","ドリームメトリック / DREAMMETRIC"},{"eden","エデン / EDEN"},{"hellborn","ヘルボーン / HELLBORN"},{"monarch","モナーク / MONARCH"},{"oblivion","オブリヴィオン / OBLIVION"},{"oppression","オプレッション / OPPRESSION"},{"tomorrow","トゥモロー / TOMORROW"}};
    static string MotifFor(string theme){switch(theme){case "astraios":return "constellation";case "crimson-moon":return "bloodmoon";case "dreammetric":return "dreamspiral";case "eden":return "halo";case "hellborn":return "inferno";case "monarch":return "throne";case "oblivion":return "blackhole";case "oppression":return "cage";case "tomorrow":return "horizon";default:return "orbit";}}
    void AuraLine(Canvas c,double x1,double y1,double x2,double y2,double opacity,string color,double thickness=1.5){c.Children.Add(new Line{X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=Brush(color),StrokeThickness=thickness,Opacity=opacity,IsHitTestVisible=false});}
    void AuraEllipse(Canvas c,double cx,double cy,double w,double h,double angle,double opacity,string color,double thickness=1.5){var e=new Ellipse{Width=w,Height=h,Stroke=Brush(color),StrokeThickness=thickness,Opacity=opacity,RenderTransform=new RotateTransform(angle,w/2,h/2),IsHitTestVisible=false};Canvas.SetLeft(e,cx-w/2);Canvas.SetTop(e,cy-h/2);c.Children.Add(e);}
    void AuraDot(Canvas c,double x,double y,double r,double opacity,string color){var d=new Ellipse{Width=r*2,Height=r*2,Fill=Brush(color),Opacity=opacity,IsHitTestVisible=false};Canvas.SetLeft(d,x-r);Canvas.SetTop(d,y-r);c.Children.Add(d);}
    void AuraCaption(Canvas c,string text,double x,double y,double size,double opacity){var t=new TextBlock{Text=text,FontFamily=new FontFamily("Consolas"),FontSize=size,Foreground=Brush("#8CA5D6"),Opacity=opacity,IsHitTestVisible=false};Canvas.SetLeft(t,x);Canvas.SetTop(t,y);c.Children.Add(t);}
    string AuraAccent(){return Setting("accent","#467AFF");}
    void AuraPath(Canvas c,string data,double ox,double oy,double scale,double opacity,string color,double thickness=1.5,bool fill=false){
      var shape=new System.Windows.Shapes.Path{Data=Geometry.Parse(data),Stroke=Brush(color),StrokeThickness=thickness,Opacity=opacity,Fill=fill?Brush(color):Brushes.Transparent,IsHitTestVisible=false,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};
      var transform=new TransformGroup();transform.Children.Add(new ScaleTransform(scale,scale));transform.Children.Add(new TranslateTransform(ox,oy));shape.RenderTransform=transform;c.Children.Add(shape);
    }
    void AuraPoly(Canvas c,Point[] points,double ox,double oy,double scale,double opacity,string color,double thickness=1.5,bool fill=true){
      var poly=new Polygon{Fill=fill?Brush(color):Brushes.Transparent,Stroke=Brush(color),StrokeThickness=fill?0:thickness,Opacity=opacity,IsHitTestVisible=false};
      foreach(var point in points)poly.Points.Add(new Point(ox+point.X*scale,oy+point.Y*scale));c.Children.Add(poly);
    }
    void AuraDisc(Canvas c,double cx,double cy,double r,double opacity,Brush fill,Brush stroke=null,double thickness=1.5){
      var dot=new Ellipse{Width=r*2,Height=r*2,Fill=fill,Stroke=stroke??Brushes.Transparent,StrokeThickness=thickness,Opacity=opacity,IsHitTestVisible=false};Canvas.SetLeft(dot,cx-r);Canvas.SetTop(dot,cy-r);c.Children.Add(dot);
    }
    void DrawAuraMotif(Canvas c,string motif,double ox,double oy,double scale,double opacity){
      string a=AuraAccent(),m="#8CA5D6",s;ThemeSecondary.TryGetValue(CurrentThemeId(),out s);if(string.IsNullOrEmpty(s))s="#79E6FF";
      switch(motif){
        case "constellation":{
          var nodes=new[]{new Point(300,64),new Point(382,128),new Point(474,92),new Point(512,196),new Point(430,252),new Point(326,300),new Point(220,248),new Point(132,188),new Point(170,106)};
          foreach(var p in nodes)AuraDot(c,ox+p.X*scale,oy+p.Y*scale,2.6*scale,opacity*.9,a);
          foreach(var pair in new[]{new[]{0,1},new[]{1,2},new[]{1,4},new[]{4,3},new[]{4,5},new[]{5,6},new[]{6,7},new[]{7,0},new[]{6,1},new[]{5,1}})
            AuraLine(c,ox+nodes[pair[0]].X*scale,oy+nodes[pair[0]].Y*scale,ox+nodes[pair[1]].X*scale,oy+nodes[pair[1]].Y*scale,opacity*.42,a);
          AuraPoly(c,new[]{new Point(300,92),new Point(309,171),new Point(388,180),new Point(309,189),new Point(300,268),new Point(291,189),new Point(212,180),new Point(291,171)},ox,oy,scale,opacity*.28,s);
          AuraPoly(c,new[]{new Point(300,112),new Point(306,174),new Point(368,180),new Point(306,186),new Point(300,248),new Point(294,186),new Point(232,180),new Point(294,174)},ox,oy,scale,opacity*.85,s);
          foreach(var p in new[]{new Point(80,90),new Point(120,240),new Point(470,300),new Point(520,60),new Point(210,40),new Point(520,300),new Point(70,300),new Point(410,60)})AuraDot(c,ox+p.X*scale,oy+p.Y*scale,1.8*scale,opacity*.8,s);
          break;}
        case "bloodmoon":{
          var moon=new RadialGradientBrush{GradientOrigin=new Point(.38,.34),Center=new Point(.5,.5),RadiusX=.62,RadiusY=.62};
          moon.GradientStops.Add(new GradientStop(Mix(ColorOf(a),Colors.White,.25),0));moon.GradientStops.Add(new GradientStop(ColorOf(a),.58));moon.GradientStops.Add(new GradientStop(ColorOf("#250208"),1));
          AuraDisc(c,ox+342*scale,oy+150*scale,112*scale,opacity*.9,moon);
          AuraEllipse(c,ox+300*scale,oy+108*scale,240*scale,240*scale,0,opacity*.28,a,2);
          foreach(var crater in new[]{new[]{390,118,13},new[]{352,196,20},new[]{300,142,10},new[]{415,196,9},new[]{330,96,7}})AuraDisc(c,ox+crater[0]*scale,oy+crater[1]*scale,crater[2]*scale,opacity*.2,Brush("#120208"));
          AuraPath(c,"M300 66 C246 92 250 146 292 174 M430 120 C448 168 414 218 366 234 M334 40 L318 96 L344 132 M368 262 L390 300 M250 262 L222 300",ox,oy,scale,opacity*.55,a,2);
          AuraPath(c,"M116 286 Q180 250 244 272 Q300 290 356 262 Q430 228 500 258",ox,oy,scale,opacity*.35,a,1.5);
          AuraPath(c,"M120 96 Q160 120 150 170 M492 74 Q462 116 480 156",ox,oy,scale,opacity*.3,s,1.5);
          foreach(var ember in new[]{new[]{470,120,6},new[]{486,170,4},new[]{150,80,4},new[]{190,300,5},new[]{470,286,3}})AuraDot(c,ox+ember[0]*scale,oy+ember[1]*scale,ember[2]*scale,opacity*.7,a);
          break;}
        case "dreamspiral":{
          AuraEllipse(c,ox+300*scale,oy+178*scale,340*scale,192*scale,-18,opacity*.18,a,1);
          for(int arm=0;arm<3;arm++){var poly=new Polyline{Stroke=Brush(arm==0?s:a),StrokeThickness=arm==0?2.2:1.2,Opacity=opacity*(arm==0?.9:.45),IsHitTestVisible=false};
            for(double t=0;t<=13;t+=.28){double r=6+t*6.8,angle=t+arm*2.1;poly.Points.Add(new Point(ox+(300+Math.Cos(angle)*r*1.18)*scale,oy+(178+Math.Sin(angle)*r*.68)*scale));}c.Children.Add(poly);}
          foreach(var spark in new[]{new[]{170,100,3},new[]{410,90,4},new[]{470,210,2},new[]{150,250,3},new[]{360,290,2},new[]{240,60,2},new[]{500,140,2},new[]{90,170,2}})AuraDot(c,ox+spark[0]*scale,oy+spark[1]*scale,spark[2]*scale,opacity*.8,s);
          AuraDot(c,ox+300*scale,oy+178*scale,13*scale,opacity*.35,s);AuraDot(c,ox+300*scale,oy+178*scale,4*scale,opacity*.9,s);
          break;}
        case "halo":{
          AuraEllipse(c,ox+300*scale,oy+104*scale,128*scale,128*scale,0,opacity*.9,s,3);
          AuraEllipse(c,ox+300*scale,oy+104*scale,160*scale,160*scale,0,opacity*.35,a,1);
          for(int i=0;i<14;i++){double angle=i*Math.PI/7-Math.PI/2;AuraLine(c,ox+(300+Math.Cos(angle)*92)*scale,oy+(104+Math.Sin(angle)*92)*scale,ox+(300+Math.Cos(angle)*124)*scale,oy+(104+Math.Sin(angle)*124)*scale,opacity*.45,s,1.6);}
          AuraPoly(c,new[]{new Point(300,136),new Point(310,166),new Point(342,176),new Point(310,186),new Point(300,220),new Point(290,186),new Point(258,176),new Point(290,166)},ox,oy,scale,opacity*.72,s);
          AuraPath(c,"M300 226 C242 170 176 164 116 210 C168 218 210 246 250 286 C266 256 282 236 300 226 Z",ox,oy,scale,opacity*.14,a,1.2,true);
          AuraPath(c,"M300 226 C358 170 424 164 484 210 C432 218 390 246 350 286 C334 256 318 236 300 226 Z",ox,oy,scale,opacity*.14,a,1.2,true);
          AuraDot(c,ox+452*scale,oy+110*scale,5*scale,opacity*.4,s);AuraDot(c,ox+148*scale,oy+294*scale,4*scale,opacity*.35,s);
          break;}
        case "inferno":{
          AuraEllipse(c,ox+300*scale,oy+306*scale,420*scale,52*scale,0,opacity*.12,a,1);
          foreach(var p in new[]{new Point(188,206),new Point(206,120),new Point(262,84),new Point(300,72),new Point(338,84),new Point(394,120),new Point(412,206),new Point(382,172),new Point(344,150),new Point(300,146),new Point(256,150),new Point(218,172)})AuraDot(c,ox+p.X*scale,oy+p.Y*scale,2*scale,opacity*.35,a);
          for(int i=0;i<5;i++){double x=170+i*66;AuraPoly(c,new[]{new Point(x,302),new Point(x-26,262),new Point(x+16,228),new Point(x-4,182),new Point(x+34,216),new Point(x+48,256),new Point(x+30,302)},ox,oy,scale,opacity*.55,s);}
          AuraPath(c,"M236 132 C238 96 266 72 282 58 C286 92 272 114 258 134 M364 132 C362 96 334 72 318 58 C314 92 328 114 342 134",ox,oy,scale,opacity*.85,s,3);
          foreach(var ember in new[]{new[]{140,120,4},new[]{460,100,3},new[]{500,220,5},new[]{110,240,3},new[]{380,60,2},new[]{250,60,2}})AuraDot(c,ox+ember[0]*scale,oy+ember[1]*scale,ember[2]*scale,opacity*.75,s);
          break;}
        case "throne":{
          for(int i=0;i<7;i++)AuraLine(c,ox+300*scale,oy+170*scale,ox+(150+i*50)*scale,oy+40*scale,opacity*.22,s,1);
          AuraPoly(c,new[]{new Point(300,78),new Point(266,126),new Point(246,82),new Point(244,154),new Point(356,154),new Point(354,82),new Point(334,126)},ox,oy,scale,opacity*.8,s);
          AuraPoly(c,new[]{new Point(254,154),new Point(346,154),new Point(360,296),new Point(240,296)},ox,oy,scale,opacity*.18,a,1.5);
          AuraLine(c,ox+232*scale,oy+296*scale,ox+368*scale,oy+296*scale,opacity*.8,s,3);
          AuraLine(c,ox+246*scale,oy+296*scale,ox+238*scale,oy+320*scale,opacity*.8,s,3);AuraLine(c,ox+354*scale,oy+296*scale,ox+362*scale,oy+320*scale,opacity*.8,s,3);
          AuraPoly(c,new[]{new Point(300,166),new Point(312,186),new Point(334,192),new Point(312,198),new Point(300,220),new Point(288,198),new Point(266,192),new Point(288,186)},ox,oy,scale,opacity*.65,s);
          AuraDot(c,ox+276*scale,oy+132*scale,4*scale,opacity,s);AuraDot(c,ox+324*scale,oy+132*scale,4*scale,opacity,s);AuraDot(c,ox+300*scale,oy+110*scale,5*scale,opacity,s);
          break;}
        case "blackhole":{
          AuraEllipse(c,ox+300*scale,oy+182*scale,392*scale,108*scale,-14,opacity*.5,a,2);
          AuraEllipse(c,ox+300*scale,oy+182*scale,332*scale,76*scale,-14,opacity*.45,s,1.2);
          AuraDisc(c,ox+300*scale,oy+180*scale,70*scale,opacity,Brush("#000000"),Brush(a),2.2);
          AuraEllipse(c,ox+300*scale,oy+180*scale,152*scale,152*scale,0,opacity*.55,s,1);
          AuraPath(c,"M208 180 Q300 120 392 180 M204 188 Q300 244 396 188",ox,oy,scale,opacity*.38,s,1.5);
          foreach(var line in new[]{new[]{90,100,180,70},new[]{470,280,560,320},new[]{120,300,70,330},new[]{520,70,560,30},new[]{180,60,130,30}})AuraLine(c,ox+line[0]*scale,oy+line[1]*scale,ox+line[2]*scale,oy+line[3]*scale,opacity*.35,s,1.2);
          break;}
        case "cage":{
          for(int i=0;i<9;i++)AuraLine(c,ox+(150+i*38)*scale,oy+72*scale,ox+(150+i*38)*scale,oy+300*scale,opacity*.35,m,3);
          AuraLine(c,ox+132*scale,oy+72*scale,ox+468*scale,oy+72*scale,opacity*.28,a,2);AuraLine(c,ox+128*scale,oy+300*scale,ox+472*scale,oy+300*scale,opacity*.28,a,2);
          AuraLine(c,ox+120*scale,oy+54*scale,ox+480*scale,oy+318*scale,opacity*.28,a,2);AuraLine(c,ox+480*scale,oy+54*scale,ox+120*scale,oy+318*scale,opacity*.28,a,2);
          for(int i=0;i<6;i++)AuraEllipse(c,ox+(188+i*46)*scale,oy+(96+(i%2)*14)*scale,18*scale,18*scale,0,opacity*.35,a,2);
          AuraPath(c,"M250 120 L282 166 L266 214 L300 252 M350 120 L318 166 L334 214 L300 252",ox,oy,scale,opacity*.55,s,1.5);
          AuraDot(c,ox+300*scale,oy+252*scale,5*scale,opacity*.8,s);
          break;}
        case "horizon":{
          AuraEllipse(c,ox+300*scale,oy+260*scale,296*scale,296*scale,0,opacity*.2,a,1);
          for(int i=0;i<12;i++){double angle=Math.PI+Math.PI*i/11;AuraLine(c,ox+(300+Math.Cos(angle)*150)*scale,oy+(260+Math.Sin(angle)*150)*scale,ox+(300+Math.Cos(angle)*164)*scale,oy+(260+Math.Sin(angle)*164)*scale,opacity*.45,s,1.5);}
          AuraPath(c,"M152 260 A148 148 0 0 1 448 260",ox,oy,scale,opacity*.75,s,2.5);
          AuraPath(c,"M208 260 A92 92 0 0 1 392 260 Z",ox,oy,scale,opacity*.32,s,1,true);
          AuraLine(c,ox+110*scale,oy+260*scale,ox+490*scale,oy+260*scale,opacity*.7,a,1.8);
          AuraLine(c,ox+132*scale,oy+288*scale,ox+468*scale,oy+288*scale,opacity*.25,a,1);AuraLine(c,ox+164*scale,oy+314*scale,ox+436*scale,oy+314*scale,opacity*.2,a,1);
          for(int i=0;i<9;i++)AuraLine(c,ox+(170+i*32)*scale,oy+260*scale,ox+(188+i*32)*scale,oy+330*scale,opacity*.16,a,.8);
          AuraDot(c,ox+300*scale,oy+260*scale,10*scale,opacity*.75,s);
          break;}
        default:for(int i=0;i<3;i++)AuraEllipse(c,ox+210*scale,oy+140*scale,(240-i*56)*scale,(88-i*20)*scale,-25,opacity*(.85-i*.15),a,1.5);
          AuraDot(c,ox+210*scale,oy+140*scale,5*scale,opacity,a);AuraEllipse(c,ox+210*scale,oy+140*scale,48*scale,48*scale,0,opacity*.85,m,2);
          for(int y=0;y<3;y++)for(int x=0;x<5;x++){var sq=new Rectangle{Width=26*scale,Height=14*scale,Fill=Brush((x+y)%2==0?a:m),Opacity=opacity*(.35+((x+y)%2)*.25),IsHitTestVisible=false};Canvas.SetLeft(sq,ox+(120+x*34)*scale);Canvas.SetTop(sq,oy+(200+y*16)*scale);c.Children.Add(sq);}
          break;
      }
    }
    void DrawAura() {
      DrawWallpaper();var canvas=Get<Canvas>("AuraCanvas");canvas.Children.Clear();
      string theme=CurrentThemeId();string caption;AuraCaptions.TryGetValue(theme,out caption);if(caption==null)caption=AuraCaptions["blue"];
      var stamp=Window.FindName("AuraStamp") as TextBlock;if(stamp!=null)stamp.Text=caption;
      if(Setting("aura","true")=="false")return;
      DrawAuraMotif(canvas,MotifFor(theme),10,10,theme=="blue"?0.95:0.52,0.9);
      AuraCaption(canvas,caption,18,232,10,0.95);
    }
    void DrawWallpaper() {
      var canvas=Get<Canvas>("ChatWallpaper");canvas.Children.Clear();if(Setting("aura","true")=="false")return;
      double width=canvas.ActualWidth,height=canvas.ActualHeight;
      string theme=CurrentThemeId();string caption;AuraCaptions.TryGetValue(theme,out caption);if(caption==null)caption=AuraCaptions["blue"];
      DrawAuraMotif(canvas,MotifFor(theme),width-330,height-300,theme=="blue"?1.35:0.82,0.5);
      AuraCaption(canvas,caption,width-300,height-38,10,0.55);
    }
    Window Dialog(string title,StackPanel panel) {
      var dialog=new Window {Title=T(title),Width=470,SizeToContent=SizeToContent.Height,MaxHeight=Window.ActualHeight-30,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=Window,Background=(Brush)Window.Resources["PanelBackground"],Foreground=Brushes.White,FontFamily=new FontFamily("Segoe UI"),ResizeMode=ResizeMode.NoResize};
      dialog.Resources=Window.Resources;panel.Margin=new Thickness(26);dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};return dialog;
    }
    TextBlock Label(string text,int size=13) {return LabelRaw(T(text),size);}
    TextBlock LabelRaw(string text,int size=13) {return new TextBlock {Text=text,FontSize=size,Foreground=Brush("#A8B9D2"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)};}
    void ShowSafety() {
      if(peer==null||group!=null)return;
      var panel=new StackPanel();panel.Children.Add(Label("Compare these fingerprints with your contact using another channel. They identify the encryption keys stored on your devices.",14));
      foreach(var user in new[]{me,peer}) {panel.Children.Add(Label("@"+user["username"],16));string fp=Crypto.Fingerprint((string)user["publicKey"]);panel.Children.Add(Label(string.Join(" ",Enumerable.Range(0,8).Select(i=>fp.Substring(i*8,8))),13));}
      string peerPrint=Crypto.Fingerprint((string)peer["publicKey"]);
      if(IsVerified(peer))panel.Children.Add(Label(T("Verified contact"),14));
      else{var markVerified=new Button{Content=T("Mark as verified"),Margin=new Thickness(0,10,0,8)};markVerified.Click+=(s,e)=>Guard(async()=>{File.WriteAllText(VerifiedPath((string)peer["id"]),peerPrint);markVerified.Content=T("Verified contact");markVerified.IsEnabled=false;Get<TextBlock>("ChatSubtitle").Text=ChatSubtitleText(peer);});panel.Children.Add(markVerified);}
      panel.Children.Add(Label("The server sees account names, message times, and who talks to whom. Message bodies are encrypted. First contact still trusts the server's key directory; compare fingerprints for stronger assurance. This prototype has not had an independent security audit."));Dialog("Verify your connection",panel).ShowDialog();
    }
    void ShowSettings() {
      var panel=new StackPanel();panel.Children.Add(Label("Make it yours.",25));panel.Children.Add(Label("ILLUSIONARY / APPEARANCE",11));
      var language=new ComboBox{ItemsSource=GetLanguageChoices(),DisplayMemberPath="Name",SelectedValuePath="Code",SelectedValue=currentLanguage,Margin=new Thickness(0,0,0,14)};panel.Children.Add(Label(T("Language")));panel.Children.Add(language);
      var themeIds=new[]{"blue","astraios","crimson-moon","dreammetric","eden","hellborn","monarch","oblivion","oppression","tomorrow"};var themeLabels=new[]{"Illusionary","Astraios","Calamity of the Crimson Moon","Dreammetric","Eden","Hellborn","Monarch","Oblivion","Oppression","Tomorrow"};var themes=new ComboBox{ItemsSource=Array.ConvertAll(themeLabels,label=>T(label)),SelectedIndex=Math.Max(0,Array.IndexOf(themeIds,CurrentThemeId())),Margin=new Thickness(0,0,0,12)};
      var themeStrip=new WrapPanel{Margin=new Thickness(0,0,0,14)};var themeButtons=new List<Button>();
      Action markThemes=()=>{for(int i=0;i<themeButtons.Count;i++){themeButtons[i].BorderThickness=new Thickness(i==themes.SelectedIndex?2:1);themeButtons[i].BorderBrush=i==themes.SelectedIndex?Brush("#FFFFFF"):(Brush)Window.Resources["PanelBorder"];}};
      for(int i=0;i<themeIds.Length;i++){int index=i;var chip=new Button{Content=T(themeLabels[i]),Background=Brush(ThemeColors[themeIds[i]][2]),Foreground=Brush(ThemeColors[themeIds[i]][7]),Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,0,6,6),FontSize=11,ToolTip=T(themeLabels[i])};chip.Click+=(s,e)=>themes.SelectedIndex=index;themeButtons.Add(chip);themeStrip.Children.Add(chip);}
      themes.SelectionChanged+=(s,e)=>markThemes();markThemes();panel.Children.Add(themeStrip);
      panel.Children.Add(Label("Interface style"));var ui=new ComboBox{ItemsSource=new[]{T("Detailed"),T("Minimalistic")},SelectedIndex=Setting("ui","detailed")=="minimalistic"?1:0,Margin=new Thickness(0,0,0,16)};panel.Children.Add(ui);
      panel.Children.Add(Label("Message text size"));var font=new Slider {Minimum=12,Maximum=20,TickFrequency=1,IsSnapToTickEnabled=true,Value=int.Parse(Setting("font","14")),Margin=new Thickness(0,0,0,16)};panel.Children.Add(font);
      panel.Children.Add(Label("Chat font"));var family=new ComboBox{ItemsSource=ProfileFonts,SelectedItem=Setting("chatFont","Segoe UI"),Margin=new Thickness(0,0,0,12)};panel.Children.Add(family);
      var lookRow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,14)};
      var exportLook=new Button{Content=T("Export look"),Margin=new Thickness(0,0,8,0)};var importLook=new Button{Content=T("Import look")};
      lookRow.Children.Add(exportLook);lookRow.Children.Add(importLook);panel.Children.Add(lookRow);
      var textColor=new ColorStudio("Message text color",Setting("textColor","#F1F5FF"));panel.Children.Add(new Expander{Header=T("Message text color · RGB studio"),Foreground=Brushes.White,Content=textColor});
      var bubbleColor=new ColorStudio("Your message bubble",Setting("bubbleColor",Setting("accent","#467AFF")));panel.Children.Add(new Expander{Header=T("Bubble color · RGB studio"),Foreground=Brushes.White,Content=bubbleColor});
      var compact=new CheckBox {Content=T("Compact message spacing"),IsChecked=Setting("compact","false")=="true",Foreground=Brushes.White,Margin=new Thickness(0,0,0,14)};panel.Children.Add(compact);
      var aura=new CheckBox {Content=T("Show checkerboard and orbital artwork"),IsChecked=Setting("aura","true")=="true",Foreground=Brushes.White,Margin=new Thickness(0,0,0,20)};panel.Children.Add(aura);
      var motion=new CheckBox {Content=T("Gentle message animations"),IsChecked=Setting("motion","true")=="true",Foreground=Brushes.White,Margin=new Thickness(0,0,0,20)};panel.Children.Add(motion);
      var receipts=new CheckBox {Content=T("Share read receipts"),IsChecked=Setting("receipts","true")=="true",Foreground=Brushes.White,Margin=new Thickness(0,0,0,20),ToolTip="Allow contacts to see when you read their messages. You can turn this off for future messages."};panel.Children.Add(receipts);
      themes.SelectionChanged+=(s,e)=>bubbleColor.Hex=ThemeColors[themeIds[themes.SelectedIndex]][4];
      exportLook.Click+=async(s,e)=>await Guard(async()=>{
        var pick=new SaveFileDialog{FileName="illusion-theme.json",DefaultExt=".json",Filter="Illusion theme (*.json)|*.json"};
        if(pick.ShowDialog()!=true)return;
        var look=new Dictionary<string,object>{{"version",1},{"theme",CurrentThemeId()},{"font",Setting("chatFont","Segoe UI")},{"textColor",Setting("textColor","#F1F5FF")},{"accent",Setting("accent","#467AFF")}};
        File.WriteAllText(pick.FileName,json.Serialize(look));
      });
      importLook.Click+=async(s,e)=>await Guard(async()=>{
        var pick=new OpenFileDialog{Filter="Illusion theme (*.json)|*.json"};
        if(pick.ShowDialog()!=true)return;
        if(new FileInfo(pick.FileName).Length>20000)throw new Exception(T("That theme file isn't valid."));
        var data=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(pick.FileName));
        string importedTheme=data!=null&&data.ContainsKey("theme")?Convert.ToString(data["theme"]):"";
        string importedFont=data!=null&&data.ContainsKey("font")?Convert.ToString(data["font"]):"";
        string importedText=data!=null&&data.ContainsKey("textColor")?Convert.ToString(data["textColor"]):"";
        string importedAccent=data!=null&&data.ContainsKey("accent")?Convert.ToString(data["accent"]):"";
        if(data==null||Convert.ToInt32(data.ContainsKey("version")?data["version"]:0)!=1||Array.IndexOf(themeIds,importedTheme)<0||Array.IndexOf(ProfileFonts,importedFont)<0||!IsLookHex(importedText)||!IsLookHex(importedAccent))throw new Exception(T("That theme file isn't valid."));
        settings["theme"]=importedTheme;settings["chatFont"]=importedFont;settings["textColor"]=importedText;settings["accent"]=importedAccent;SaveSettings();
        themes.SelectedIndex=Array.IndexOf(themeIds,importedTheme);family.SelectedItem=importedFont;textColor.Hex=importedText;
        ApplyTheme();DrawAura();RelocalizeEverything();
      });
      var apply=new Button {Content=T("Save appearance"),Background=(Brush)Window.Resources["Accent"]};panel.Children.Add(apply);
      var privacyButton=new Button{Content=T("Privacy, policies and data deletion"),Margin=new Thickness(0,10,0,0)};panel.Children.Add(privacyButton);privacyButton.Click+=(s,e)=>ShowPrivacyAndData();
      var backup=new Button {Content=T("Back up my encryption key"),IsEnabled=me!=null,Margin=new Thickness(0,12,0,0)};panel.Children.Add(backup);
      var recovery=new Button {Content=T("Connect Gmail for account recovery"),IsEnabled=me!=null,Margin=new Thickness(0,8,0,0)};panel.Children.Add(recovery);recovery.Click+=(s,e)=>ShowEmailRecovery();
      panel.Children.Add(Label("Your key is protected by Windows on this profile. Export a password-protected backup before changing PCs. Without it, old messages cannot be recovered.",12));
      var devices=new Button{Content=T("Signed-in devices"),IsEnabled=me!=null,Margin=new Thickness(0,10,0,0)};panel.Children.Add(devices);devices.Click+=(s,e)=>Guard(ShowDevices);
      var logout=new Button {Content=T("Sign out"),IsEnabled=me!=null};panel.Children.Add(logout);
      panel.Children.Add(Label("Illusion "+BuildInfo.Version+" · "+T("Private. Customizable. Free.")+"\n"+T("A connection beyond the noise.")+"\n"+T("Your space, uninterrupted."),11));
      var dialog=Dialog("Customize Illusion",panel);
      apply.Click+=(s,e)=>{settings["language"]=Convert.ToString(language.SelectedValue??"en");currentLanguage=settings["language"];settings["theme"]=themeIds[themes.SelectedIndex];settings["ui"]=ui.SelectedIndex==1?"minimalistic":"detailed";settings["chatFont"]=Convert.ToString(family.SelectedItem);settings["textColor"]=textColor.Hex;if(bubbleColor.Hex=="")settings.Remove("bubbleColor");else settings["bubbleColor"]=bubbleColor.Hex;settings["accent"]=ThemeColors[themeIds[themes.SelectedIndex]][4];settings["font"]=((int)font.Value).ToString();settings["compact"]=compact.IsChecked==true?"true":"false";settings["aura"]=aura.IsChecked==true?"true":"false";settings["motion"]=motion.IsChecked==true?"true":"false";settings["receipts"]=receipts.IsChecked==true?"true":"false";lastReadSent=0;SaveSettings();ApplyTheme();DrawAura();RelocalizeEverything();dialog.Close();};
      backup.Click+=async(s,e)=>await Guard(ExportKey);
      logout.Click+=async(s,e)=>{if(sending)return;dialog.Close();await Guard(async()=>{try{await Api("POST","/v1/logout",new{});}finally{SignOutLocal();}});};dialog.ShowDialog();
    }
    void ShowAccountRecovery() {
      var panel=new StackPanel();panel.Children.Add(Label("Use the Gmail address already verified on your account. A password reset does not restore encryption keys; use the device or .illusionkey backup that contains your key."));
      var username=new TextBox{Text=Get<TextBox>("UsernameInput").Text.Trim(),MaxLength=24};var email=new TextBox{MaxLength=254};var code=new TextBox{MaxLength=6};var password=new PasswordBox{MaxLength=128};
      panel.Children.Add(Label("Username"));panel.Children.Add(username);panel.Children.Add(Label("Gmail address"));panel.Children.Add(email);
      var send=new Button{Content=T("Send recovery code"),Margin=new Thickness(0,10,0,6)};panel.Children.Add(send);panel.Children.Add(Label("Six-letter code"));panel.Children.Add(code);panel.Children.Add(Label("New password · at least 10 characters"));panel.Children.Add(password);
      var reset=new Button{Content=T("Reset password"),Background=(Brush)Window.Resources["Accent"],Margin=new Thickness(0,12,0,0)};panel.Children.Add(reset);var dialog=Dialog("Recover your account",panel);
      send.Click+=async(s,e)=>await Guard(async()=>{SetServer();await Api("POST","/v1/recovery/start",new{username=username.Text,email=email.Text},false);Get<TextBlock>("AuthError").Text="If these details match a verified account, a code will arrive shortly.";});
      reset.Click+=async(s,e)=>await Guard(async()=>{if(password.Password.Length<10||password.Password.Length>128)throw new Exception("Use a password of 10–128 characters.");SetServer();await Api("POST","/v1/recovery/finish",new{username=username.Text,email=email.Text,code=code.Text,password=password.Password},false);password.Clear();dialog.Close();Get<TextBox>("UsernameInput").Text=username.Text;Get<PasswordBox>("PasswordInput").Clear();Get<TextBlock>("AuthError").Text="Password reset. Sign in with the new password and restore your key if needed.";});
      var keyReset=new Button{Content=T("Reset using this device key"),Margin=new Thickness(0,8,0,0)};panel.Children.Add(keyReset);keyReset.Click+=async(s,e)=>await Guard(async()=>{if(password.Password.Length<10||password.Password.Length>128)throw new Exception("Use a password of 10–128 characters.");SetServer();string account=username.Text.Trim().ToLowerInvariant(),path=KeyPath(account);if(!File.Exists(path))throw new Exception("No saved encryption key for this username. Restore its .illusionkey backup first.");string identity=Crypto.Unprotect(File.ReadAllText(path));var challenge=await Api("POST","/v1/recovery/key/challenge",new{username=account},false);string id=(string)challenge["challengeId"],nonce=(string)challenge["challenge"],signature=Crypto.SignRecovery(id,account,nonce,Convert.ToInt64(challenge["expiresAt"]),identity);await Api("POST","/v1/recovery/key/finish",new{username=account,challengeId=id,signature=signature,password=password.Password},false);password.Clear();dialog.Close();Get<TextBox>("UsernameInput").Text=account;Get<PasswordBox>("PasswordInput").Clear();Get<TextBlock>("AuthError").Text="Password reset using your device key. Sign in with the new password.";});
      dialog.ShowDialog();
    }
    async void ShowEmailRecovery() {
      if(me==null)return;Dictionary<string,object> status;try{status=await Api("GET","/v1/email/status");}catch(Exception error){Get<TextBlock>("StatusLabel").Text=error.Message;return;}var panel=new StackPanel();bool verified=status.ContainsKey("verified")&&Convert.ToBoolean(status["verified"]);panel.Children.Add(Label(verified?"Recovery Gmail is already verified: "+Convert.ToString(status["email"]):"Verify a Gmail address to enable password recovery. It is used only for account security codes."));
      if(verified){Dialog("Account recovery",panel).ShowDialog();return;}if(!status.ContainsKey("available")||!Convert.ToBoolean(status["available"])){panel.Children.Add(Label("Gmail recovery is not configured on this Illusion server yet. You can reset your password with the device key saved on this device or restore a .illusionkey backup."));Dialog("Account recovery",panel).ShowDialog();return;}
      var email=new TextBox{MaxLength=254};var code=new TextBox{MaxLength=6};panel.Children.Add(Label("Gmail address"));panel.Children.Add(email);var send=new Button{Content=T("Send verification code"),Margin=new Thickness(0,10,0,6)};panel.Children.Add(send);panel.Children.Add(Label("Six-letter code"));panel.Children.Add(code);var verify=new Button{Content=T("Verify Gmail"),Background=(Brush)Window.Resources["Accent"],Margin=new Thickness(0,12,0,0)};panel.Children.Add(verify);var dialog=Dialog("Connect Gmail for account recovery",panel);
      send.Click+=async(s,e)=>await Guard(async()=>{await Api("POST","/v1/email/start",new{email=email.Text});Get<TextBlock>("StatusLabel").Text="Verification code sent if the address is eligible.";});
      verify.Click+=async(s,e)=>await Guard(async()=>{var result=await Api("POST","/v1/email/verify",new{email=email.Text,code=code.Text});dialog.Close();Get<TextBlock>("StatusLabel").Text=T("Recovery email verified: ")+Convert.ToString(result["email"]);});dialog.ShowDialog();
    }
    void SignOutLocal() {
      StopExperience();generation++;timer.Stop();me=null;peer=null;group=null;token="";privateKey="";messages.Clear();ResetExperience();ResetGroups();Get<Border>("GroupEventsPanel").Visibility=Visibility.Collapsed;settings.Remove("token");SaveSettings();
      Get<ListBox>("ChatList").Items.Clear();Get<Border>("AuthOverlay").Visibility=Visibility.Visible;Get<StackPanel>("EmptyState").Visibility=Visibility.Visible;Get<ScrollViewer>("MessageScroll").Visibility=Visibility.Collapsed;Get<Border>("ComposerPanel").Visibility=Visibility.Collapsed;Get<Button>("VerifyButton").Visibility=Visibility.Collapsed;
    }
    string BackupPassword(string title) {
      var panel=new StackPanel();panel.Children.Add(Label("Use a strong backup password (at least 12 characters). Keep it separately from your key file."));
      var pass=new PasswordBox();panel.Children.Add(pass);var button=new Button {Content=T("Continue"),Margin=new Thickness(0,15,0,0)};panel.Children.Add(button);var dialog=Dialog(title,panel);string result=null;
      button.Click+=(s,e)=>{if(pass.Password.Length<12){MessageBox.Show(dialog,"Use at least 12 characters.");return;}result=pass.Password;dialog.Close();};dialog.ShowDialog();return result;
    }
    async Task ExportKey() {
      if(me==null)return;string password=BackupPassword("Protect your key backup");if(password==null)return;
      var save=new SaveFileDialog {Filter="Illusion key backup|*.illusionkey",FileName="Illusion-"+me["username"]+".illusionkey"};if(save.ShowDialog(Window)!=true)return;
      string key=privateKey,username=(string)me["username"],address=server;
      var backup=await Task.Run(()=>Crypto.Backup(key,password));backup["username"]=username;backup["server"]=address;
      File.WriteAllText(save.FileName,json.Serialize(backup));Get<TextBlock>("StatusLabel").Text="Encryption key backup saved. Keep the file and password safe.";
    }
    async Task ImportKey() {
      SetServer();string username=Get<TextBox>("UsernameInput").Text.Trim().ToLowerInvariant();if(username=="")throw new Exception("Enter your server address and username before restoring a key.");
      var open=new OpenFileDialog {Filter="Illusion key backup|*.illusionkey"};if(open.ShowDialog(Window)!=true)return;
      if(new FileInfo(open.FileName).Length>20000)throw new Exception("Invalid key file.");
      var backup=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(open.FileName));
      if((string)backup["username"]!=username)throw new Exception("This backup belongs to a different username.");
      if((string)backup["server"]!=server && MessageBox.Show(Window,T("Restore this key backup for this address? Sign-in will check that it matches the account."),T("Restore for a different address"),MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
      string password=BackupPassword("Restore your encryption key");if(password==null)return;
      string key=await Task.Run(()=>Crypto.Restore(backup,password));File.WriteAllText(KeyPath(username),Crypto.Protect(key));Get<TextBlock>("AuthError").Text=T("Key restored. You can sign in.");
    }
    public void Capture(string path) {Window.UpdateLayout();var bmp=new RenderTargetBitmap((int)Window.ActualWidth,(int)Window.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(Window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var file=File.Create(path))encoder.Save(file);}
  }
  public static class Program {
    [STAThread] public static void Main(string[] args) {
      ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
      var app=new Application();
      app.DispatcherUnhandledException+=(s,e)=>{MessageBox.Show("Illusion couldn't complete that action. "+e.Exception.Message,"Illusion");e.Handled=true;};
      var messenger=new Messenger(Environment.GetEnvironmentVariable("ILLUSION_PROFILE_DIR"));app.Run(messenger.Window);
    }
  }
}
