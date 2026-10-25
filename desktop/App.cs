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
        if(!updatingChats && item!=null && item.Tag!=null) await Guard(()=>SelectPeer((Dictionary<string,object>)item.Tag));
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
      InitializePrivacy();InitializeConsent();
      Window.Closed+=(s,e)=>{StopExperience();closing=true;generation++;timer.Stop();http.Dispose();};
      Window.ContentRendered+=async(s,e)=>await Guard(Resume);
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
      if(root==null)return;var element=root as FrameworkElement;if(element!=null){bool skip=excluded || new[]{"ChatList","MessageList","ChatTitle","ChatSubtitle","IdentityLabel","ConnectionLabel","StatusLabel","AuthError","AuthHeading","AuthButton","SwitchAuthButton","TermsLink","PrivacyLink","ComposerHint","CharacterCount","AttachmentLabel","ReplyText","NoticeText","SearchCount","NetworkDescription","Avatar","Composer","UsernameInput","PasswordInput","InviteInput","ServerInput","SearchInput","ChatSearchInput"}.Contains(element.Name);if(!skip){var text=element as TextBlock;if(text!=null && !string.IsNullOrEmpty(text.Text)){string original=text.Text;BindLanguage(original,value=>text.Text=value);}var button=element as Button;if(button!=null && button.Content is string){string original=(string)button.Content;BindLanguage(original,value=>button.Content=value);}var check=element as CheckBox;if(check!=null && check.Content is string){string original=(string)check.Content;BindLanguage(original,value=>check.Content=value);}var expander=element as Expander;if(expander!=null && expander.Header is string){string original=(string)expander.Header;BindLanguage(original,value=>expander.Header=value);}if(element.ToolTip is string){string original=(string)element.ToolTip;BindLanguage(original,value=>element.ToolTip=value);}}
      }
      foreach(object child in LogicalTreeHelper.GetChildren(root))if(child is DependencyObject)CaptureLocalizedElements((DependencyObject)child,excluded || (element!=null && (element.Name=="ChatList" || element.Name=="MessageList")));
    }
    void ApplyWindowLanguage(){foreach(var apply in languageBindings)apply();Get<Button>("OlderButton").Content=T("Load earlier messages");Get<Button>("FindButton").Content="+  "+T("Start a conversation");}
    sealed class LanguageChoice{public string Code{get;set;}public string Name{get;set;}}
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
      Get<TextBlock>("StatusLabel").Text="Connected · Message text encrypted on your device · Media uploads are unencrypted";
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
    Task LoadOlder() { return FetchOlder(); }
    public Task Send() { return QueueMessage(); }
    static SolidColorBrush Brush(string color) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
    void RenderMessages(bool bottom) { UpdateMessageViews(bottom,false); }
    void ApplyTheme() {ApplyPalette();Window.Resources["Accent"]=Brush(Setting("accent","#467AFF"));Window.FontFamily=new FontFamily(Setting("chatFont","Segoe UI"));}
    void DrawAura() {
      DrawWallpaper();var canvas=Get<Canvas>("AuraCanvas");canvas.Children.Clear();if(Setting("aura","true")=="false")return;
      for(int y=0;y<5;y++)for(int x=0;x<8;x++){var square=new Rectangle {Width=22,Height=11,Fill=Brush((x+y)%2==0?"#234EAD":"#A7C3FF"),Opacity=0.18};Canvas.SetLeft(square,55+x*24);Canvas.SetTop(square,157+y*12);canvas.Children.Add(square);}
      foreach(int size in new[]{175,140,105}){var orbit=new Ellipse {Width=size,Height=size/2.7,Stroke=Brush(Setting("accent","#467AFF")),StrokeThickness=1.5,RenderTransform=new RotateTransform(-25,size/2,size/5.4)};Canvas.SetLeft(orbit,(300-size)/2);Canvas.SetTop(orbit,105-size/5.4);canvas.Children.Add(orbit);}
      var diamond=new Rectangle {Width=40,Height=40,Stroke=Brushes.White,StrokeThickness=2,RenderTransform=new RotateTransform(45,20,20)};Canvas.SetLeft(diamond,130);Canvas.SetTop(diamond,83);canvas.Children.Add(diamond);
      foreach(var p in new[]{new Point(52,72),new Point(244,125),new Point(207,36)}){var star=new TextBlock {Text="✦",FontSize=22,Foreground=Brush("#8CB3FF")};Canvas.SetLeft(star,p.X);Canvas.SetTop(star,p.Y);canvas.Children.Add(star);}
      var caption=new TextBlock {Text="[ illusion / 001 ]",FontFamily=new FontFamily("Consolas"),FontSize=10,Foreground=Brush("#718CB6")};Canvas.SetLeft(caption,95);Canvas.SetTop(caption,222);canvas.Children.Add(caption);
    }
    void DrawWallpaper() {
      var canvas=Get<Canvas>("ChatWallpaper");canvas.Children.Clear();if(Setting("aura","true")=="false")return;
      double width=canvas.ActualWidth,height=canvas.ActualHeight;
      for(int y=0;y<8;y++)for(int x=0;x<12;x++){var square=new Rectangle{Width=25,Height=14,Fill=Brush((x+y)%2==0?"#467AFF":"#B5C7E8"),Opacity=0.018+(y/8.0)*0.022};Canvas.SetLeft(square,width-320+x*27);Canvas.SetTop(square,height-190+y*16);canvas.Children.Add(square);}
      foreach(int size in new[]{185,225,265}){var ring=new Ellipse{Width=size,Height=size/2.5,Stroke=Brush(Setting("accent","#467AFF")),StrokeThickness=1,Opacity=0.09,RenderTransform=new RotateTransform(-27,size/2,size/5.0)};Canvas.SetLeft(ring,width-180-size/2);Canvas.SetTop(ring,height-250-size/5.0);canvas.Children.Add(ring);}
      var stamp=new TextBlock{Text="ドリームステート / DREAM STATE",FontFamily=new FontFamily("Consolas"),FontSize=9,Foreground=Brush("#293B58")};Canvas.SetLeft(stamp,width-250);Canvas.SetTop(stamp,height-35);canvas.Children.Add(stamp);
    }
    Window Dialog(string title,StackPanel panel) {
      var dialog=new Window {Title=T(title),Width=470,SizeToContent=SizeToContent.Height,MaxHeight=Window.ActualHeight-30,WindowStartupLocation=WindowStartupLocation.CenterOwner,Owner=Window,Background=(Brush)Window.Resources["PanelBackground"],Foreground=Brushes.White,FontFamily=new FontFamily("Segoe UI"),ResizeMode=ResizeMode.NoResize};
      dialog.Resources=Window.Resources;panel.Margin=new Thickness(26);dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};return dialog;
    }
    TextBlock Label(string text,int size=13) {return LabelRaw(T(text),size);}
    TextBlock LabelRaw(string text,int size=13) {return new TextBlock {Text=text,FontSize=size,Foreground=Brush("#A8B9D2"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)};}
    void ShowSafety() {
      if(peer==null)return;
      var panel=new StackPanel();panel.Children.Add(Label("Compare these fingerprints with your contact using another channel. They identify the encryption keys stored on your devices.",14));
      foreach(var user in new[]{me,peer}) {panel.Children.Add(Label("@"+user["username"],16));string fp=Crypto.Fingerprint((string)user["publicKey"]);panel.Children.Add(Label(string.Join(" ",Enumerable.Range(0,8).Select(i=>fp.Substring(i*8,8))),13));}
      panel.Children.Add(Label("The server sees account names, message times, and who talks to whom. Message bodies are encrypted. First contact still trusts the server's key directory; compare fingerprints for stronger assurance. This prototype has not had an independent security audit."));Dialog("Verify your connection",panel).ShowDialog();
    }
    void ShowSettings() {
      var panel=new StackPanel();panel.Children.Add(Label("Make it yours.",25));panel.Children.Add(Label("ILLUSIONARY / APPEARANCE",11));
      var language=new ComboBox{ItemsSource=GetLanguageChoices(),DisplayMemberPath="Name",SelectedValuePath="Code",SelectedValue=currentLanguage,Margin=new Thickness(0,0,0,14)};panel.Children.Add(Label(T("Language")));panel.Children.Add(language);
      var themeIds=new[]{"blue","ice","silver","midnight","violet","rose","crimson","ember","forest","ocean","sand","aurora","plum","sunset","mono"};var themeLabels=new[]{"Illusionary · blue","Dream state · ice","Monochrome · silver","Midnight · indigo","Violet dusk","Rose quartz","Crimson ember","Ember glow","Forest moss","Deep ocean","Desert sand","Aurora veil","Plum haze","Sunset blush","Pure contrast"};var themes=new ComboBox{ItemsSource=Array.ConvertAll(themeLabels,label=>T(label)),SelectedIndex=Math.Max(0,Array.IndexOf(themeIds,CurrentThemeId())),Margin=new Thickness(0,0,0,16)};panel.Children.Add(themes);
      panel.Children.Add(Label("Message text size"));var font=new Slider {Minimum=12,Maximum=20,TickFrequency=1,IsSnapToTickEnabled=true,Value=int.Parse(Setting("font","14")),Margin=new Thickness(0,0,0,16)};panel.Children.Add(font);
      panel.Children.Add(Label("Chat font"));var family=new ComboBox{ItemsSource=ProfileFonts,SelectedItem=Setting("chatFont","Segoe UI"),Margin=new Thickness(0,0,0,12)};panel.Children.Add(family);
      var textColor=new ColorStudio("Message text color",Setting("textColor","#F1F5FF"));panel.Children.Add(new Expander{Header=T("Message text color · RGB studio"),Foreground=Brushes.White,Content=textColor});
      var bubbleColor=new ColorStudio("Your message bubble",Setting("bubbleColor",Setting("accent","#467AFF")));panel.Children.Add(new Expander{Header=T("Bubble color · RGB studio"),Foreground=Brushes.White,Content=bubbleColor});
      var compact=new CheckBox {Content=T("Compact message spacing"),IsChecked=Setting("compact","false")=="true",Foreground=Brushes.White,Margin=new Thickness(0,0,0,14)};panel.Children.Add(compact);
      var aura=new CheckBox {Content=T("Show checkerboard and orbital artwork"),IsChecked=Setting("aura","true")=="true",Foreground=Brushes.White,Margin=new Thickness(0,0,0,20)};panel.Children.Add(aura);
      var motion=new CheckBox {Content=T("Gentle message animations"),IsChecked=Setting("motion","true")=="true",Foreground=Brushes.White,Margin=new Thickness(0,0,0,20)};panel.Children.Add(motion);
      var receipts=new CheckBox {Content=T("Share read receipts"),IsChecked=Setting("receipts","true")=="true",Foreground=Brushes.White,Margin=new Thickness(0,0,0,20),ToolTip="Allow contacts to see when you read their messages. You can turn this off for future messages."};panel.Children.Add(receipts);
      themes.SelectionChanged+=(s,e)=>bubbleColor.Hex=ThemeColors[themeIds[themes.SelectedIndex]][4];
      var apply=new Button {Content=T("Save appearance"),Background=(Brush)Window.Resources["Accent"]};panel.Children.Add(apply);
      var privacyButton=new Button{Content=T("Privacy, policies and data deletion"),Margin=new Thickness(0,10,0,0)};panel.Children.Add(privacyButton);privacyButton.Click+=(s,e)=>ShowPrivacyAndData();
      var backup=new Button {Content=T("Back up my encryption key"),IsEnabled=me!=null,Margin=new Thickness(0,12,0,0)};panel.Children.Add(backup);
      var recovery=new Button {Content=T("Connect Gmail for account recovery"),IsEnabled=me!=null,Margin=new Thickness(0,8,0,0)};panel.Children.Add(recovery);recovery.Click+=(s,e)=>ShowEmailRecovery();
      panel.Children.Add(Label("Your key is protected by Windows on this profile. Export a password-protected backup before changing PCs. Without it, old messages cannot be recovered.",12));
      var logout=new Button {Content=T("Sign out"),IsEnabled=me!=null};panel.Children.Add(logout);
      panel.Children.Add(Label("Illusion "+BuildInfo.Version+" · "+T("Private. Customizable. Free.")+"\n"+T("A connection beyond the noise.")+"\n"+T("Your space, uninterrupted."),11));
      var dialog=Dialog("Customize Illusion",panel);
      apply.Click+=(s,e)=>{settings["language"]=Convert.ToString(language.SelectedValue??"en");currentLanguage=settings["language"];settings["theme"]=themeIds[themes.SelectedIndex];settings["chatFont"]=Convert.ToString(family.SelectedItem);settings["textColor"]=textColor.Hex;if(bubbleColor.Hex=="")settings.Remove("bubbleColor");else settings["bubbleColor"]=bubbleColor.Hex;settings["accent"]=ThemeColors[themeIds[themes.SelectedIndex]][4];settings["font"]=((int)font.Value).ToString();settings["compact"]=compact.IsChecked==true?"true":"false";settings["aura"]=aura.IsChecked==true?"true":"false";settings["motion"]=motion.IsChecked==true?"true":"false";settings["receipts"]=receipts.IsChecked==true?"true":"false";lastReadSent=0;SaveSettings();ApplyTheme();ApplyWindowLanguage();DrawAura();if(peer!=null)UpdateMessageViews(false,true);dialog.Close();};
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
      StopExperience();generation++;timer.Stop();me=null;peer=null;token="";privateKey="";messages.Clear();ResetExperience();settings.Remove("token");SaveSettings();
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
