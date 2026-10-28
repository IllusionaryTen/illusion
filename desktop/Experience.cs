using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Illusion {
  public partial class Messenger {
    sealed class Content { public string Text; public Dictionary<string,object> Reply,Media; public bool Valid=true; public long Deadline; }
    sealed class MessageView { public StackPanel Root; public TextBlock Date,Time; public Border Bubble; public Content Content; public Dictionary<string,object> Message; }
    readonly SemaphoreSlim refreshGate=new SemaphoreSlim(1,1),outboxGate=new SemaphoreSlim(1,1);
    readonly Dictionary<string,string> drafts=new Dictionary<string,string>();
    readonly Dictionary<string,Dictionary<string,object>> replyDrafts=new Dictionary<string,Dictionary<string,object>>(),contacts=new Dictionary<string,Dictionary<string,object>>();
    readonly Dictionary<string,List<Dictionary<string,object>>> history=new Dictionary<string,List<Dictionary<string,object>>>();
    readonly List<Dictionary<string,object>> outbox=new List<Dictionary<string,object>>();
    readonly HashSet<string> pinned=new HashSet<string>();
    readonly HashSet<string> mutedChats=new HashSet<string>();
    readonly Dictionary<string,ListBoxItem> chatItems=new Dictionary<string,ListBoxItem>();
    readonly Dictionary<string,Content> decoded=new Dictionary<string,Content>();
    readonly Dictionary<string,MessageView> views=new Dictionary<string,MessageView>();
    readonly Dictionary<string,long> notifiedUnread=new Dictionary<string,long>();
    readonly List<string> searchMatches=new List<string>();
    readonly Dictionary<string,System.Windows.Media.ImageSource> videoThumbCache=new Dictionary<string,System.Windows.Media.ImageSource>();
    CancellationTokenSource sessionStop;
    Task syncTask;
    Dictionary<string,object>[] latestChats=new Dictionary<string,object>[0];
    Dictionary<string,object> reply;
    bool updatingChats,restoringDraft,dirtyLocal,sessionConnected,loadingConversation,localStateReady;
    string lastPeer="",localStatePath="",activeSend="";
    long readThrough,lastReadSent;
    int conversationVersion,searchIndex=-1,scrollIntent;
    DateTime changedDraftAt=DateTime.UtcNow;
    TextBlock emptyMessage;
    Window notificationToast;
    System.Windows.Threading.DispatcherTimer notificationToastTimer;
    MediaPlayer notificationPlayer;
    string notificationSoundPath="";
    bool notificationBaselineReady;
    static string ContentKey(Dictionary<string,object> message){return (string)message["senderId"]+"|"+(string)message["clientId"];}

    void InitializeExperience() {
      InitializeNotifications();
      Get<TextBox>("Composer").TextChanged+=(s,e)=>{
        int length=Get<TextBox>("Composer").Text.Length;
        Get<TextBlock>("CharacterCount").Text=length+" / 2000";
        Get<TextBlock>("CharacterCount").Foreground=Brush(length>2000?"#FF9DA7":"#637B9F");
        if(!restoringDraft && me!=null && peer!=null){CaptureDraft();dirtyLocal=true;changedDraftAt=DateTime.UtcNow;}
      };
      Get<TextBox>("SearchInput").TextChanged+=(s,e)=>FilterChats();
      DataObject.AddPastingHandler(Get<TextBox>("Composer"),(s,e)=>{
        try {
          var image=e.DataObject.GetData(DataFormats.Bitmap) as System.Windows.Media.Imaging.BitmapSource;
          if(image==null)return;
          e.Handled=true;e.CancelCommand();
          if(me==null||peer==null)return;
          var source=image;
          double factor=Math.Min(1,Math.Min(1600.0/image.PixelWidth,1600.0/image.PixelHeight));
          if(factor<1){var scaled=new System.Windows.Media.Imaging.TransformedBitmap(image,new System.Windows.Media.ScaleTransform(factor,factor));scaled.Freeze();source=scaled;}
          string encoded=EncodeImage(source,4*1024*1024);
          pendingMedia=new Dictionary<string,object>{{"kind","photo"},{"mime","image/jpeg"},{"data",encoded.Substring(encoded.IndexOf(',')+1)}};
          UpdateAttachment();
        }catch{ }
      });
      Get<Button>("SearchChatButton").Click+=(s,e)=>OpenSearch();
      Get<TextBox>("ChatSearchInput").TextChanged+=(s,e)=>{searchIndex=-1;SearchMessages(false);};
      Get<TextBox>("ChatSearchInput").KeyDown+=(s,e)=>{if(e.Key==Key.Enter){MoveSearch(Keyboard.Modifiers==ModifierKeys.Shift?-1:1);e.Handled=true;}};
      Get<Button>("NextMatchButton").Click+=(s,e)=>MoveSearch(1);
      Get<Button>("PreviousMatchButton").Click+=(s,e)=>MoveSearch(-1);
      Get<Button>("CloseSearchButton").Click+=(s,e)=>CloseSearch();
      Get<Button>("CancelReplyButton").Click+=(s,e)=>{reply=null;UpdateReply();CaptureDraft();dirtyLocal=true;};
      Get<Button>("EmojiButton").Click+=(s,e)=>ShowEmoji();
      Get<Button>("RetryButton").Click+=async(s,e)=>await Guard(async()=>{await Refresh();await FlushOutbox();});
      Get<Button>("NewMessagesButton").Click+=async(s,e)=>{Get<ScrollViewer>("MessageScroll").ScrollToEnd();Get<Button>("NewMessagesButton").Visibility=Visibility.Collapsed;await Guard(MarkVisibleRead);};
      Get<ScrollViewer>("MessageScroll").ScrollChanged+=async(s,e)=>{if(e.VerticalChange!=0 && e.ExtentHeightChange==0 && e.ViewportHeightChange==0)scrollIntent++;if(AtBottom()){Get<Button>("NewMessagesButton").Visibility=Visibility.Collapsed;if(!loadingConversation)await Guard(MarkVisibleRead);}};
      Get<ScrollViewer>("MessageScroll").PreviewMouseWheel+=(s,e)=>scrollIntent++;
      Window.Activated+=async(s,e)=>await Guard(MarkVisibleRead);
      Window.PreviewKeyDown+=(s,e)=>{
        if(e.Key==Key.F && Keyboard.Modifiers==ModifierKeys.Control){OpenSearch();e.Handled=true;}
        if(e.Key==Key.Escape){CloseSearch();reply=null;UpdateReply();CaptureDraft();dirtyLocal=true;}
      };
    }
    void InitializeNotifications(){
      notificationPlayer=new MediaPlayer();
      var sound=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("illusionsfx.mp3");
      if(sound!=null){notificationSoundPath=System.IO.Path.Combine(ProfileDir,"illusionsfx.mp3");if(!File.Exists(notificationSoundPath)){Directory.CreateDirectory(ProfileDir);using(var output=File.Create(notificationSoundPath))sound.CopyTo(output);}sound.Dispose();notificationPlayer.Open(new Uri(notificationSoundPath));}
      Window.Closed+=(s,e)=>{CloseNotificationToast();if(notificationPlayer!=null){notificationPlayer.Stop();notificationPlayer.Close();notificationPlayer=null;}};
    }
    void NotifyIncoming(Dictionary<string,object> chat,long count){
      if(mutedChats.Contains(Str(chat,"id","")))return;
      try{if(notificationPlayer!=null && notificationPlayer.Source!=null){notificationPlayer.Position=TimeSpan.Zero;notificationPlayer.Play();}}catch{}
      if(!Window.IsActive){string name=Str(chat,"displayName","Illusion"),body=count>1?count+" new messages":"New message";ShowNotificationToast(name,body);}
    }
    void ShowNotificationToast(string name,string body){
      CloseNotificationToast();
      var title=new TextBlock{Text=name,FontSize=14,FontWeight=FontWeights.SemiBold,Foreground=Brush("#F4F7FF"),TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=268};
      var message=new TextBlock{Text=body,FontSize=12,Foreground=Brush("#AFBED5"),Margin=new Thickness(0,4,0,0)};
      var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};text.Children.Add(title);text.Children.Add(message);
      var close=new Button{Content="×",Width=30,Height=30,Padding=new Thickness(0),Background=Brush("#17202F"),Foreground=Brush("#AFBED5"),BorderThickness=new Thickness(0),FontSize=19,VerticalAlignment=VerticalAlignment.Top,ToolTip="Dismiss notification"};
      var row=new DockPanel{LastChildFill=true};DockPanel.SetDock(close,Dock.Right);row.Children.Add(close);row.Children.Add(text);
      var card=new Border{Padding=new Thickness(16,14,12,14),CornerRadius=new CornerRadius(15),BorderThickness=new Thickness(1),BorderBrush=Brush("#34445F"),Background=Brush("#111925"),Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,Opacity=.38,BlurRadius=22,ShadowDepth=5}};card.Child=row;
      var toast=new Window{Title=T("Illusion notification"),Width=360,Height=92,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,Topmost=true,AllowsTransparency=true,Background=Brush("#00000000"),Opacity=0,Content=card};
      System.Windows.Automation.AutomationProperties.SetName(toast,"Illusion message notification");System.Windows.Automation.AutomationProperties.SetName(title,"Message from "+name);System.Windows.Automation.AutomationProperties.SetName(message,body);
      var area=SystemParameters.WorkArea;toast.Left=Math.Max(area.Left+12,area.Right-toast.Width-18);toast.Top=Math.Max(area.Top+12,area.Bottom-toast.Height-18);
      toast.MouseLeftButtonUp+=(s,e)=>{if(e.OriginalSource is Button)return;if(Window.WindowState==WindowState.Minimized)Window.WindowState=WindowState.Normal;Window.Activate();CloseNotificationToast();};
      close.Click+=(s,e)=>CloseNotificationToast();
      toast.Closed+=(s,e)=>{if(ReferenceEquals(notificationToast,toast)){notificationToast=null;if(notificationToastTimer!=null){notificationToastTimer.Stop();notificationToastTimer=null;}}};
      notificationToast=toast;toast.Show();toast.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));
      notificationToastTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(5)};notificationToastTimer.Tick+=(s,e)=>{notificationToastTimer.Stop();if(ReferenceEquals(notificationToast,toast)){toast.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(220)));var closeTimer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(240)};closeTimer.Tick+=(sender,args)=>{closeTimer.Stop();toast.Close();};closeTimer.Start();}};notificationToastTimer.Start();
    }
    void CloseNotificationToast(){
      if(notificationToastTimer!=null){notificationToastTimer.Stop();notificationToastTimer=null;}
      if(notificationToast!=null){var toast=notificationToast;notificationToast=null;toast.Close();}
    }
    void ShowNotice(string text,bool error) {
      Get<Border>("NoticePanel").Visibility=string.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;
      Get<TextBlock>("NoticeText").Text=text;
      Get<Border>("NoticePanel").Background=Brush(error?"#30212D":"#17253B");
    }
    void Connection(bool connected) {
      sessionConnected=connected;
      Get<TextBlock>("ConnectionLabel").Text=connected?"●  "+T("Live connection")+(cloudLiveActive?" · "+T("Cloud"):""):"○  "+T("Reconnecting…");
      Get<TextBlock>("ConnectionLabel").Foreground=Brush(connected?"#79DDB5":"#E8B67C");
      Get<TextBlock>("StatusLabel").Text=connected?T("Live connection · encrypted message bodies.")+(outbox.Count>0?" · "+outbox.Count+" "+T("queued"):" · "+T("Ctrl+K find someone / Ctrl+F search chat")):T("Offline · Your drafts and queued messages are safe on this device");
      if(connected)ShowNotice("",false);
    }
    static void AtomicWrite(string path,string text) {File.WriteAllText(path+".tmp",text);if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);}
    void CaptureDraft() {
      if(me==null || (peer==null && group==null) || restoringDraft)return;
      string id=CurrentConversationId(),text=Get<TextBox>("Composer").Text;
      if(id=="")return;
      if(text.Length>0)drafts[id]=text;else drafts.Remove(id);
      if(reply!=null)replyDrafts[id]=reply;else replyDrafts.Remove(id);
    }
    void SaveLocalState() {
      if(me==null || localStatePath=="" || !localStateReady)return;
      int cachedBytes=0;var caches=history.SelectMany(p=>p.Value.Where(m=>!Removed(m)).Select(m=>new {Peer=p.Key,Message=m})).OrderByDescending(p=>Convert.ToInt64(p.Message["id"])).Take(400).Where(p=>{int size=json.Serialize(p.Message).Length;if(cachedBytes+size>8000000)return false;cachedBytes+=size;return true;}).GroupBy(p=>p.Peer).ToDictionary(p=>p.Key,p=>(object)p.Select(m=>m.Message).OrderBy(m=>Convert.ToInt64(m["id"])).ToArray());
      var cachedContacts=contacts.Values.OrderByDescending(c=>Str(c,"id")==lastPeer).Take(100).Select(c=>{var copy=c.Where(p=>p.Key!="lastMessage").ToDictionary(p=>p.Key,p=>p.Value);copy["profile"]=UserProfile(c).Where(p=>p.Key!="avatar" && p.Key!="banner").ToDictionary(p=>p.Key,p=>p.Value);return copy;}).ToArray();
      var state=new Dictionary<string,object>{{"drafts",drafts},{"replies",replyDrafts},{"contacts",cachedContacts},{"pinned",pinned.ToArray()},{"outbox",outbox.ToArray()},{"lastPeer",lastPeer},{"lastGroup",lastGroup},{"history",caches}};
      AtomicWrite(localStatePath,Crypto.Protect(json.Serialize(state)));dirtyLocal=false;
    }
    void LoadMutes(){
      mutedChats.Clear();if(me==null)return;
      try{var list=json.Deserialize<string[]>(Setting("mutes:"+(string)me["id"],"[]"));if(list!=null)foreach(var id in list)if(!string.IsNullOrEmpty(id))mutedChats.Add(id);}catch{}
    }
    void SaveMutes(){
      if(me==null)return;settings["mutes:"+(string)me["id"]]=json.Serialize(mutedChats.ToArray());SaveSettings();
    }
    void ResetExperience() {
      ResetPrivacy();ResetGroups();
      drafts.Clear();replyDrafts.Clear();contacts.Clear();outbox.Clear();pinned.Clear();mutedChats.Clear();chatItems.Clear();history.Clear();decoded.Clear();views.Clear();notifiedUnread.Clear();notificationBaselineReady=false;latestChats=new Dictionary<string,object>[0];reply=null;lastPeer="";lastGroup="";localStatePath="";dirtyLocal=false;localStateReady=false;lastReadSent=0;readThrough=0;
      Get<StackPanel>("MessageList").Children.Clear();Get<StackPanel>("MessageList").Children.Add(Get<Button>("OlderButton"));emptyMessage=null;
      Get<Border>("ReplyBar").Visibility=Visibility.Collapsed;Get<Border>("NoticePanel").Visibility=Visibility.Collapsed;Get<Button>("NewMessagesButton").Visibility=Visibility.Collapsed;Get<Border>("GroupEventsPanel").Visibility=Visibility.Collapsed;
      Get<TextBlock>("ConnectionLabel").Text="○  Not connected";Window.Title="Illusion";
    }
    void LoadLocalState() {
      ResetExperience();
      localStatePath=System.IO.Path.Combine(ProfileDir,"workspace-"+Crypto.Fingerprint(server+"/"+me["id"])+".dat");
      if(File.Exists(localStatePath)) {
        try {
          var state=json.Deserialize<Dictionary<string,object>>(Crypto.Unprotect(File.ReadAllText(localStatePath)));
          foreach(var p in (Dictionary<string,object>)state["drafts"])drafts[p.Key]=(string)p.Value;
          foreach(var p in (Dictionary<string,object>)state["replies"])replyDrafts[p.Key]=(Dictionary<string,object>)p.Value;
          foreach(Dictionary<string,object> user in (IEnumerable)state["contacts"])contacts[(string)user["id"]]=user;
          foreach(string id in (IEnumerable)state["pinned"])pinned.Add(id);
          foreach(Dictionary<string,object> item in (IEnumerable)state["outbox"])outbox.Add(item);
          lastPeer=state.ContainsKey("lastPeer")?(string)state["lastPeer"]:"";
          lastGroup=state.ContainsKey("lastGroup")?(string)state["lastGroup"]:"";
          if(state.ContainsKey("history"))foreach(var p in (Dictionary<string,object>)state["history"])history[p.Key]=((IEnumerable)p.Value).Cast<Dictionary<string,object>>().ToList();
        } catch {ShowNotice("Your local workspace could not be read. Your server history and encryption key are unchanged. The original workspace file has been kept.",true);throw new Exception("Unable to read local drafts and outbox. Keep the workspace file for recovery before resetting it.");}
      }
      contacts[(string)me["id"]]=me;
      LoadMutes();
      localStateReady=true;
      ExpireLocal();
    }
    void StartExperience() {
      if(sessionStop!=null)sessionStop.Cancel();sessionStop=new CancellationTokenSource();
      syncTask=BeginExperience(sessionStop.Token,generation);
    }
    async Task BeginExperience(CancellationToken cancellation,int gen) {
      try{if(peer==null && lastPeer!="" && contacts.ContainsKey(lastPeer))await SelectPeer(contacts[lastPeer]);}
      catch(Exception e){if(gen==generation && !cancellation.IsCancellationRequested)ShowNotice(e.Message,true);}
      if(!cancellation.IsCancellationRequested && gen==generation)await SyncLoop(cancellation,gen);
    }
    void StopExperience() {
      CaptureDraft();if(me!=null)SaveLocalState();if(sessionStop!=null){sessionStop.Cancel();sessionStop=null;}
    }
    async Task SyncLoop(CancellationToken cancellation,int gen) {
      if(await CloudLive(cancellation,gen))return;
      string cursor="";int delay=1000;bool fallback=false;
      while(!cancellation.IsCancellationRequested && gen==generation && !closing) {
        try {
          if(fallback){await Task.Delay(2000,cancellation);await Refresh();}
          else {
            var events=await Api("GET","/v1/events?cursor="+Uri.EscapeDataString(cursor));
            if(gen!=generation || cancellation.IsCancellationRequested)return;
            cursor=(string)events["cursor"];
            if((bool)events["changed"] || !sessionConnected)await Refresh();
          }
          if(gen!=generation || cancellation.IsCancellationRequested)return;
          Connection(true);delay=1000;
          if(peer==null && group==null && lastPeer!="" && contacts.ContainsKey(lastPeer))await SelectPeer(contacts[lastPeer]);
          await FlushOutbox();
        } catch(OperationCanceledException){if(cancellation.IsCancellationRequested)return;Connection(false);}
        catch(ApiException e){if(gen!=generation||cancellation.IsCancellationRequested)return;if(e.Status==401){SignOutLocal();Get<TextBlock>("AuthError").Text="Your session expired. Sign in again.";return;}if(e.Status==404){fallback=true;continue;}Connection(false);}
        catch {if(cancellation.IsCancellationRequested || gen!=generation || closing)return;Connection(false);}
        if(!sessionConnected){ShowNotice("Connection interrupted. Retrying automatically; you can keep writing and queue messages.",false);try{await Task.Delay(delay,cancellation);}catch(OperationCanceledException){return;}delay=Math.Min(delay*2,15000);}
      }
    }
    async Task ExperienceTick() {
      if(me==null)return;
      ExpireLocal();
      if(dirtyLocal && DateTime.UtcNow-changedDraftAt>TimeSpan.FromMilliseconds(600)){SaveLocalState();UpdateChats(latestChats);}
      if(sessionConnected)await MarkVisibleRead();
    }
    public async Task RefreshExperience() {
      int gen=generation;await refreshGate.WaitAsync();
      try {
        if(me==null || closing || gen!=generation)return;
        var response=await Api("GET","/v1/chats");if(gen!=generation || me==null)return;
        var chats=((IEnumerable)response["chats"]).Cast<Dictionary<string,object>>().ToArray();
        foreach(var chat in chats){Pin(chat);contacts[(string)chat["id"]]=chat;}
        foreach(var chat in chats)if(chat.ContainsKey("lastMessage"))await DecodeMessages(new[]{(Dictionary<string,object>)chat["lastMessage"]},chat);
        if(gen!=generation || me==null)return;
        try{await FetchGroupsOnly();}
        catch(ApiException groupError){if(groupError.Status==401)throw;}
        catch{}
        if(gen!=generation || me==null)return;
        latestChats=chats;UpdateChats(chats);
        if(group!=null){
          var fresh=groups.FirstOrDefault(g=>GroupId(g)==GroupId(group));
          if(fresh==null){CloseGroupConversation(false);ShowNotice("You are no longer a member of this group.",true);}
          else{group=fresh;UpdateChats(chats);try{await FetchGroupConversation(false);}catch(ApiException groupError){if(groupError.Status==403||groupError.Status==404){CloseGroupConversation(false);ShowNotice(groupError.Message,true);}else throw;}}
        }else if(peer!=null){var fresh=chats.FirstOrDefault(c=>(string)c["id"]==(string)peer["id"]);if(fresh!=null)peer=fresh;await FetchConversation(false);}
        else if(lastGroup!=""){
          var target=groups.FirstOrDefault(g=>GroupId(g)==lastGroup);
          if(target!=null)await SelectGroup(target);else lastGroup="";
        }
        if(gen==generation)Connection(true);
      } finally {refreshGate.Release();}
    }
    void UpdateChats(Dictionary<string,object>[] chats) {
      var list=Get<ListBox>("ChatList");var scroll=FindScroll(list);double offset=scroll==null?0:scroll.VerticalOffset;
      var data=chats.ToDictionary(c=>(string)c["id"]);
      foreach(var c in contacts.Values)if(!data.ContainsKey((string)c["id"]) && ((string)c["id"]!=(string)me["id"] || drafts.ContainsKey((string)c["id"])))data[(string)c["id"]]=c;
      var entries=new List<Dictionary<string,object>>(data.Values);
      foreach(var g in groups)if(!data.ContainsKey(GroupId(g)))entries.Add(g);
      var sorted=entries.OrderByDescending(c=>pinned.Contains(Str(c,"id"))).ThenByDescending(c=>c.ContainsKey("updatedAt")?Convert.ToInt64(c["updatedAt"]):0).ToArray();
      var nextUnread=new Dictionary<string,long>();foreach(var chat in sorted){if(!chat.ContainsKey("username"))continue;string id=(string)chat["id"];long count=chat.ContainsKey("unread")?Convert.ToInt64(chat["unread"]):0;nextUnread[id]=count;if(notificationBaselineReady&&count>0){long old;notifiedUnread.TryGetValue(id,out old);if(count>old && (peer==null || (string)peer["id"]!=id))NotifyIncoming(chat,count-old);}}
      notifiedUnread.Clear();foreach(var pair in nextUnread)notifiedUnread[pair.Key]=pair.Value;notificationBaselineReady=true;
      foreach(var pair in chatItems.ToArray()){var tag=pair.Value.Tag as Dictionary<string,object>;if(tag!=null && !tag.ContainsKey("username") && !groups.Any(g=>GroupId(g)==pair.Key)){list.Items.Remove(pair.Value);chatItems.Remove(pair.Key);}}
      updatingChats=true;
      try {
        int index=0;long unreadTotal=0;
        foreach(var chat in sorted) {
          string id=Str(chat,"id");bool isGroup=!chat.ContainsKey("username");long unread=isGroup?GroupUnread(chat):(chat.ContainsKey("unread")?Convert.ToInt64(chat["unread"]):0);unreadTotal+=unread;
          ListBoxItem item;
          if(!chatItems.TryGetValue(id,out item)) {
            item=new ListBoxItem();chatItems[id]=item;
            var menu=new ContextMenu();var pin=new MenuItem{Header=T("Pin / unpin conversation")};pin.Click+=(s,e)=>{if(!pinned.Add(id))pinned.Remove(id);SaveLocalState();UpdateChats(latestChats);};menu.Items.Add(pin);item.ContextMenu=menu;
          }
          item.Tag=chat;
          var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(40)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
          var avatar=isGroup?GroupAvatar(34):ProfileAvatar(chat,34);avatar.VerticalAlignment=VerticalAlignment.Top;row.Children.Add(avatar);
          var text=new StackPanel();Grid.SetColumn(text,1);
          string title=isGroup?GroupName(chat):(string)chat["displayName"];
          text.Children.Add(new TextBlock{Text=(pinned.Contains(id)?"◇  ":"")+(mutedChats.Contains(id)?"🔕  ":"")+title+(unread>0?"   • "+unread:""),Foreground=Brushes.White,FontSize=14,FontWeight=FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis});
          string preview;string signature;
          if(isGroup){
            preview=GroupMembers(chat).Count+" "+T("members");
            signature=T("Group")+"|"+title+"|"+preview+"|"+unread+"|"+pinned.Contains(id)+"|"+mutedChats.Contains(id)+"|"+GroupMembers(chat).Count;
            item.ToolTip=T("Group")+" · "+GroupMembers(chat).Count+" "+T("members");
          }else{
            preview="@"+chat["username"];
            if(drafts.ContainsKey(id))preview=T("Draft: ")+drafts[id].Replace('\n',' ');
            else if(chat.ContainsKey("lastMessage")){var last=(Dictionary<string,object>)chat["lastMessage"];Content content;if(decoded.TryGetValue(ContentKey(last),out content))preview=((string)last["senderId"]==(string)me["id"]?T("You: "):"")+content.Text.Replace('\n',' ');}
            signature=(string)chat["displayName"]+"|"+preview+"|"+unread+"|"+pinned.Contains(id)+"|"+mutedChats.Contains(id)+"|"+Crypto.Fingerprint(json.Serialize(UserProfile(chat)));
            item.ToolTip="@"+chat["username"]+" · Right-click to pin";
          }
          text.Children.Add(new TextBlock{Text=preview,Foreground=Brush(drafts.ContainsKey(id)?"#91B7FF":"#7C8DAA"),FontSize=11,Margin=new Thickness(0,5,0,0),TextTrimming=TextTrimming.CharacterEllipsis});row.Children.Add(text);
          // Keep the item itself and its selection; content changes do not reset the list.
          if(item.Uid!=signature){item.Content=row;item.Uid=signature;}
          int old=list.Items.IndexOf(item);if(old!=index){if(old>=0)list.Items.RemoveAt(old);list.Items.Insert(index,item);}index++;
          if((peer!=null && (string)peer["id"]==id) || (group!=null && GroupId(group)==id))list.SelectedItem=item;
        }
        Window.Title=unreadTotal>0?"("+unreadTotal+") Illusion":"Illusion";FilterChats();
      } finally {updatingChats=false;if(scroll!=null)scroll.ScrollToVerticalOffset(offset);}
    }
    static ScrollViewer FindScroll(DependencyObject node) {if(node is ScrollViewer)return (ScrollViewer)node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var found=FindScroll(VisualTreeHelper.GetChild(node,i));if(found!=null)return found;}return null;}
    void FilterChats() {string filter=Get<TextBox>("SearchInput").Text.Trim().TrimStart('@');foreach(var pair in chatItems){var target=pair.Value.Tag as Dictionary<string,object>;if(target==null)continue;bool match;if(target.ContainsKey("username"))match=filter=="" || ((string)target["username"]).IndexOf(filter,StringComparison.OrdinalIgnoreCase)>=0 || ((string)target["displayName"]).IndexOf(filter,StringComparison.OrdinalIgnoreCase)>=0;else match=filter=="" || GroupName(target).IndexOf(filter,StringComparison.OrdinalIgnoreCase)>=0;pair.Value.Visibility=match?Visibility.Visible:Visibility.Collapsed;}}
    async Task SelectConversation(Dictionary<string,object> user) {
      if(me==null)return;if(peer!=null && (string)peer["id"]==(string)user["id"])return;
      CaptureDraft();SaveLocalState();group=null;groupEvents.Clear();lastGroup="";UpdateGroupEventsPanel();Get<Border>("GroupEventsPanel").Visibility=Visibility.Collapsed;
      Pin(user);peer=user;string id=(string)user["id"];contacts[id]=user;lastPeer=id;conversationVersion++;lastReadSent=0;readThrough=0;
      pendingMedia=null;UpdateAttachment();Get<CheckBox>("ViewOnceCheck").IsChecked=false;Get<CheckBox>("ViewOnceCheck").IsEnabled=true;Get<Button>("AttachButton").IsEnabled=true;
      Get<TextBlock>("AttachmentNotice").Visibility=Visibility.Visible;Get<StackPanel>("AttachmentControls").Visibility=Visibility.Visible;Get<Button>("ChatOptionsButton").Content=T("Profile / Timer");
      LoadContactWallpaper(id);
      messages.Clear();views.Clear();emptyMessage=null;
      var list=Get<StackPanel>("MessageList");while(list.Children.Count>1)list.Children.RemoveAt(1);
      restoringDraft=true;Get<TextBox>("Composer").Text=drafts.ContainsKey(id)?drafts[id]:"";restoringDraft=false;reply=replyDrafts.ContainsKey(id)?replyDrafts[id]:null;UpdateReply();
      Get<TextBlock>("ChatTitle").Text=id==(string)me["id"]?T("Saved messages"):(string)user["displayName"];
      Get<TextBlock>("ChatSubtitle").Text=ChatSubtitleText(user);
      Get<StackPanel>("EmptyState").Visibility=Visibility.Collapsed;Get<ScrollViewer>("MessageScroll").Visibility=Visibility.Visible;Get<Border>("ComposerPanel").Visibility=Visibility.Visible;Get<Button>("VerifyButton").Visibility=Visibility.Visible;Get<Button>("NewMessagesButton").Visibility=Visibility.Collapsed;CloseSearch();
      if(history.ContainsKey(id)) {foreach(var row in history[id])messages[Convert.ToInt64(row["id"])]=row;await DecodeMessages(history[id],user);}
      await DecodeMessages(outbox.Where(m=>(string)m["recipientId"]==id).ToArray(),user);UpdateMessageViews(true,false);SaveLocalState();UpdateChats(latestChats);
      try {await FetchConversation(messages.Count==0);}catch(Exception e){if(e is ApiException && ((ApiException)e).Status==401)throw;Connection(false);ShowNotice("Showing messages saved on this device. Reconnecting automatically.",false);}
      Get<TextBox>("Composer").Focus();
    }
    bool AtBottom() {var scroll=Get<ScrollViewer>("MessageScroll");return scroll.ScrollableHeight-scroll.VerticalOffset<65;}
    async Task FetchConversation(bool initial) {
      if(peer==null || me==null)return;string id=(string)peer["id"];var user=peer;int gen=generation,version=conversationVersion;
      long after=initial || messages.Count==0?0:messages.Keys.Last();
      var response=await Api("GET","/v1/messages?peer="+id+(after>0?"&after="+after:""));
      if(gen!=generation || version!=conversationVersion || peer==null)return;
      if(response.ContainsKey("ttlSeconds")){chatTimers[id]=Convert.ToInt32(response["ttlSeconds"]);UpdateTimerHint();}
      if(response.ContainsKey("removedIds"))RemoveFromCache(((IEnumerable)response["removedIds"]).Cast<object>().Select(Convert.ToInt64));
      var rows=((IEnumerable)response["messages"]).Cast<Dictionary<string,object>>().Where(m=>!Removed(m)).ToArray();await DecodeMessages(rows,user);
      if(gen!=generation || version!=conversationVersion || peer==null)return;
      bool bottom=AtBottom();foreach(var row in rows){messages[Convert.ToInt64(row["id"])]=row;var pending=outbox.FirstOrDefault(m=>(string)m["senderId"]==(string)row["senderId"] && (string)m["recipientId"]==(string)row["recipientId"] && (string)m["clientId"]==(string)row["clientId"] && (Once(row) || (string)((Dictionary<string,object>)m["envelope"])["signature"]==(string)((Dictionary<string,object>)row["envelope"])["signature"]));if(pending!=null){outbox.Remove(pending);if(Once(row))decoded[ContentKey(row)]=new Content{Text="View-once message"};}}
      readThrough=response.ContainsKey("readThrough")?Convert.ToInt64(response["readThrough"]):0;
      if(initial || after==0)Get<Button>("OlderButton").Visibility=(bool)response["hasMore"]?Visibility.Visible:Visibility.Collapsed;
      TrimConversation(false);UpdateMessageViews(initial||bottom,false);
      if(rows.Any(r=>(string)r["senderId"]!=(string)me["id"]) && !bottom && !initial)Get<Button>("NewMessagesButton").Visibility=Visibility.Visible;
      history[id]=messages.Values.ToList();dirtyLocal=true;await MarkVisibleRead();if(after>0 && (bool)response["hasMore"] && rows.Length>0)await FetchConversation(false);
    }
    async Task FetchOlder() {
      if(peer==null || messages.Count==0)return;string id=(string)peer["id"];var user=peer;int gen=generation,version=conversationVersion;
      Get<Button>("OlderButton").IsEnabled=false;
      try {
        var response=await Api("GET","/v1/messages?peer="+id+"&before="+messages.Keys.First());
        if(gen!=generation || version!=conversationVersion)return;
        var rows=((IEnumerable)response["messages"]).Cast<Dictionary<string,object>>().Where(m=>!Removed(m)).ToArray();await DecodeMessages(rows,user);if(gen!=generation || version!=conversationVersion)return;
        var scroll=Get<ScrollViewer>("MessageScroll");double offset=scroll.VerticalOffset,extent=scroll.ExtentHeight;loadingConversation=true;
        foreach(var row in rows)messages[Convert.ToInt64(row["id"])]=row;
        Get<Button>("OlderButton").Visibility=(bool)response["hasMore"]?Visibility.Visible:Visibility.Collapsed;TrimConversation(true);UpdateMessageViews(false,false,false);scroll.UpdateLayout();scroll.ScrollToVerticalOffset(offset+scroll.ExtentHeight-extent);history[id]=messages.Values.ToList();dirtyLocal=true;
      } finally {loadingConversation=false;Get<Button>("OlderButton").IsEnabled=true;}
    }
    void TrimConversation(bool older){
      long bytes=0;foreach(var row in messages.Values){var e=row.ContainsKey("envelope")?row["envelope"] as Dictionary<string,object>:null;bytes+=2048+(e!=null && e.ContainsKey("ciphertext")?Convert.ToString(e["ciphertext"]).Length:0);}
      while((messages.Count>400 || bytes>16000000) && messages.Count>1){long id=older?messages.Keys.Last():messages.Keys.First();var row=messages[id];var e=row.ContainsKey("envelope")?row["envelope"] as Dictionary<string,object>:null;bytes-=2048+(e!=null && e.ContainsKey("ciphertext")?Convert.ToString(e["ciphertext"]).Length:0);decoded.Remove(ContentKey(row));messages.Remove(id);Get<Button>("OlderButton").Visibility=Visibility.Visible;}
    }
    async Task MarkVisibleRead() {
      if(me==null || (peer==null && group==null) || !sessionConnected || !Window.IsActive || !AtBottom() || messages.Count==0)return;
      long last=messages.Keys.Last();if(last<=lastReadSent)return;int version=conversationVersion;
      lastReadSent=last;
      if(group!=null){
        string groupId=GroupId(group);
        try{await Api("POST","/v1/groups/"+Uri.EscapeDataString(groupId)+"/read",new{lastId=last});if(group!=null){group["unread"]=0;UpdateChats(latestChats);}}
        catch{if(version==conversationVersion)lastReadSent=0;throw;}
        return;
      }
      string id=(string)peer["id"];
      try {await Api("POST","/v1/read",new{peer=id,lastId=last,shareReceipts=Setting("receipts","true")=="true"});}catch {if(version==conversationVersion)lastReadSent=0;throw;}
    }
    Content Decode(Dictionary<string,object> message,string myId,string key,string senderKey,bool opened=false) {
      try {
        if(Removed(message))return new Content{Text="Message removed"};
        if(Once(message) && !opened)return new Content{Text="View-once message"};
        if(message.ContainsKey("attachment") && message["envelope"]==null)return new Content{Text="Encrypted attachment"};
        string text=Crypto.Decrypt(message,myId,key,senderKey);Dictionary<string,object> quote=null,media=null;
        Dictionary<string,object> parsed=null;
        if(text.StartsWith("{"))try{parsed=new JavaScriptSerializer{MaxJsonLength=8000000}.Deserialize<Dictionary<string,object>>(text);}catch{}
        if(parsed!=null && (Str(parsed,"format")=="illusion.message.v1" || Str(parsed,"format")=="illusion.message.v2")){
          if(!parsed.ContainsKey("text") || !(parsed["text"] is string) || ((string)parsed["text"]).Length>2000)throw new Exception("Invalid message text.");text=(string)parsed["text"];
          if(parsed.ContainsKey("reply") && parsed["reply"]!=null){quote=parsed["reply"] as Dictionary<string,object>;if(quote==null || !quote.ContainsKey("name") || !(quote["name"] is string) || ((string)quote["name"]).Length>80 || !quote.ContainsKey("text") || !(quote["text"] is string) || ((string)quote["text"]).Length>320)throw new Exception("Invalid reply.");}
          if(parsed.ContainsKey("media") && parsed["media"]!=null)media=SealedMediaMeta(parsed["media"] as Dictionary<string,object>);
        }else if(text.Length>12000)throw new Exception("Message text is too large.");
        if(message.ContainsKey("media") && message["media"]!=null)media=AttachMediaData(media,message["media"] as Dictionary<string,object>);
        return new Content{Text=text,Reply=quote,Media=media,Deadline=message.ContainsKey("expiresAt")?Convert.ToInt64(message["expiresAt"]):0};
      }catch{return new Content{Text="Unable to verify or decrypt this message. Restore the correct key or verify with your contact.",Valid=false};}
    }
    async Task DecodeMessages(IEnumerable<Dictionary<string,object>> source,Dictionary<string,object> other) {
      if(me==null)return;var rows=source.Where(m=>!decoded.ContainsKey(ContentKey(m)) || (m.ContainsKey("envelope") && m["envelope"]!=null && decoded[ContentKey(m)].Text=="Encrypted attachment")).ToArray();if(rows.Length==0)return;
      string myId=(string)me["id"],key=privateKey,own=(string)me["publicKey"],pub=(string)other["publicKey"];int gen=generation;
      var results=await Task.Run(()=>rows.Select(m=>new KeyValuePair<string,Content>(ContentKey(m),Decode(m,myId,key,(string)m["senderId"]==myId?own:pub))).ToArray());
      if(gen!=generation)return;foreach(var item in results)decoded[item.Key]=item.Value;
    }
    async Task QueueMessage() {
      if(peer==null || me==null || sending)return;string raw=Get<TextBox>("Composer").Text,text=raw.Trim();if(text=="" && pendingMedia==null)return;
      if(text.Length>2000)throw new Exception("Keep each message under 2,000 characters.");
      if(outbox.Count>=100)throw new Exception("Your outbox has 100 messages. Reconnect to deliver them before adding more.");
      sending=true;Get<Button>("SendButton").IsEnabled=false;int gen=generation;
      string sender=(string)me["id"],recipient=(string)peer["id"],pub=(string)peer["publicKey"],key=privateKey,clientId=Guid.NewGuid().ToString();var quoted=reply;var attached=pendingMedia;bool once=Get<CheckBox>("ViewOnceCheck").IsChecked==true;Dictionary<string,object> mediaMeta=null;if(attached!=null){byte[] plain=Convert.FromBase64String(Str(attached,"data"));string mk,miv,mmk,mmac;byte[] cipher=Crypto.SealMedia(plain,out mk,out miv,out mmk,out mmac);mediaMeta=new Dictionary<string,object>{{"kind","sealed"},{"mime",Str(attached,"mime")},{"key",mk},{"iv",miv},{"macKey",mmk},{"mac",mmac}};attached=new Dictionary<string,object>{{"kind","sealed"},{"mime",Str(attached,"mime")},{"data",Convert.ToBase64String(cipher)}};}
      if(once && sender==recipient){sending=false;Get<Button>("SendButton").IsEnabled=true;throw new Exception("View once is for messages to someone else.");}
      int ttl;chatTimers.TryGetValue(recipient,out ttl);var policy=new Dictionary<string,object>{{"viewOnce",once},{"ttlSeconds",ttl}};
      string content=json.Serialize(new Dictionary<string,object>{{"format","illusion.message.v2"},{"text",text},{"reply",quoted},{"media",mediaMeta}});
      try {
        var envelope=await Task.Run(()=>Crypto.Encrypt(content,sender,recipient,clientId,key,pub,policy));if(gen!=generation || me==null)return;
        var entry=new Dictionary<string,object>{{"senderId",sender},{"recipientId",recipient},{"clientId",clientId},{"envelope",envelope},{"media",attached},{"viewOnce",once},{"createdAt",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}};
        if(outbox.Sum(m=>json.Serialize(m).Length)+json.Serialize(entry).Length>8000000)throw new Exception("Your encrypted outbox is full (8 MB). Reconnect before adding another attachment.");
        outbox.Add(entry);
        try{SaveLocalState();}catch{outbox.Remove(entry);throw;}
        decoded[sender+"|"+clientId]=new Content{Text=once?"View-once message":text,Reply=once?null:quoted,Media=once?null:LocalMedia(mediaMeta,attached)};
        if(drafts.ContainsKey(recipient) && drafts[recipient]==raw)drafts.Remove(recipient);replyDrafts.Remove(recipient);
        if(peer!=null && (string)peer["id"]==recipient && Get<TextBox>("Composer").Text==raw){Get<TextBox>("Composer").Clear();reply=null;if(object.ReferenceEquals(pendingMedia,attached))pendingMedia=null;UpdateAttachment();Get<CheckBox>("ViewOnceCheck").IsChecked=false;UpdateReply();UpdateMessageViews(true,false);}
        SaveLocalState();Get<TextBlock>("ComposerHint").Text=sessionConnected?"◇  Sending securely…":"◇  Queued safely · sends when reconnected";
      } finally {sending=false;Get<Button>("SendButton").IsEnabled=true;}
      await FlushOutbox();
    }
    async Task FlushOutbox() {
      if(me==null || !await outboxGate.WaitAsync(0))return;int gen=generation;
      try {
        while(outbox.Count>0 && gen==generation && me!=null && !closing) {
          var entry=outbox[0];
          activeSend=(string)entry["clientId"];
          if(entry.ContainsKey("groupId")){bool keepGoing=await TryFlushGroupEntry(entry,gen);if(!keepGoing)return;continue;}
          try {
            var response=await Api("POST","/v1/messages",new{recipientId=entry["recipientId"],clientId=entry["clientId"],envelope=entry["envelope"],media=entry.ContainsKey("media")?entry["media"]:null});
            if(gen!=generation || me==null)return;
            var saved=(Dictionary<string,object>)response["message"];outbox.Remove(entry);
            if(Once(saved))decoded[ContentKey(saved)]=new Content{Text="View-once message"};
            else if(decoded.ContainsKey(ContentKey(saved)))decoded[ContentKey(saved)].Deadline=saved.ContainsKey("expiresAt")?Convert.ToInt64(saved["expiresAt"]):0;
            string id=(string)entry["recipientId"];
            if(!history.ContainsKey(id))history[id]=new List<Dictionary<string,object>>();if(!history[id].Any(m=>Convert.ToInt64(m["id"])==Convert.ToInt64(saved["id"])))history[id].Add(saved);
            if(peer!=null && (string)peer["id"]==id){messages[Convert.ToInt64(saved["id"])]=saved;UpdateMessageViews(AtBottom(),false);}
            Get<TextBlock>("ComposerHint").Text="◇  "+T("Encrypted attachment");
          } catch(ApiException e){if(e.Status==401)throw;if(e.Status>=500 || e.Status==429)Connection(false);ShowNotice("Message remains in your outbox: "+e.Message,false);return;}
          catch(OperationCanceledException){if(gen!=generation || closing || sessionStop==null)return;Connection(false);return;}
          catch {if(gen==generation && !closing){Connection(false);ShowNotice("Your message is queued safely. It will retry after the connection returns.",false);}return;}
        }
      }finally{activeSend="";outboxGate.Release();}
    }
    void BeginReply(Dictionary<string,object> message,Content content) {
      string who;
      if((string)message["senderId"]==(string)me["id"])who=(string)me["displayName"];
      else if(group!=null){var sender=GroupSender(message);who=sender==null?Str(message,"senderId"):GroupMemberLabel(sender);}
      else who=(string)peer["displayName"];
      reply=new Dictionary<string,object>{{"id",message.ContainsKey("id")?message["id"]:0},{"name",who},{"text",content.Text.Length>160?content.Text.Substring(0,160):content.Text}};UpdateReply();CaptureDraft();dirtyLocal=true;Get<TextBox>("Composer").Focus();
    }
    void UpdateReply() {Get<Border>("ReplyBar").Visibility=reply==null?Visibility.Collapsed:Visibility.Visible;Get<TextBlock>("ReplyText").Text=reply==null?"":"Replying to "+reply["name"]+": "+reply["text"];}
    void UpdateMessageViews(bool bottom,bool force,bool manageScroll=true) {
      if(me==null || (peer==null && group==null))return;var list=Get<StackPanel>("MessageList");string conversationId=CurrentConversationId();
      if(force){views.Clear();while(list.Children.Count>1)list.Children.RemoveAt(1);emptyMessage=null;}
      var scroll=Get<ScrollViewer>("MessageScroll");double previousOffset=scroll.VerticalOffset;int intent=++scrollIntent,version=conversationVersion;
      var pendingRows=outbox.Where(m=>{
        if(group!=null)return m.ContainsKey("groupId") && Str(m,"groupId")==conversationId && !messages.Values.Any(s=>Str(s,"clientId")==Str(m,"clientId") && Str(s,"senderId")==Str(m,"senderId"));
        return !m.ContainsKey("groupId") && Str(m,"recipientId")==conversationId && !messages.Values.Any(s=>Str(s,"clientId")==Str(m,"clientId") && Str(s,"senderId")==Str(m,"senderId"));
      });
      var rows=messages.Values.Concat(pendingRows).Where(m=>!Removed(m)).ToArray();
      if(emptyMessage!=null){list.Children.Remove(emptyMessage);emptyMessage=null;}
      var wanted=new HashSet<string>();DateTime? date=null;int index=1;
      foreach(var message in rows) {
        string clientId=(string)message["clientId"],id=message.ContainsKey("id")?Convert.ToString(message["id"]):"q:"+clientId;bool own=(string)message["senderId"]==(string)me["id"],queued=!message.ContainsKey("id");
        Content content;if(!decoded.TryGetValue(ContentKey(message),out content))continue;wanted.Add(id);MessageView view;
        if(!views.TryGetValue(id,out view) && !queued && views.TryGetValue("q:"+clientId,out view)){views.Remove("q:"+clientId);views[id]=view;}
        var when=DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(message["createdAt"])).LocalDateTime;
        if(view==null) {
          view=new MessageView{Root=new StackPanel(),Date=new TextBlock{HorizontalAlignment=HorizontalAlignment.Center,Foreground=Brush("#637898"),FontSize=11,Margin=new Thickness(0,12,0,18)},Content=content};
          view.Root.Children.Add(view.Date);var inner=new StackPanel();
          if(group!=null && !own){var sender=GroupSender(message);inner.Children.Add(new TextBlock{Text=sender!=null?GroupMemberLabel(sender):Str(message,"senderId"),Foreground=Brush("#8CB3FF"),FontSize=11,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,5),TextTrimming=TextTrimming.CharacterEllipsis});}
          if(content.Reply!=null)inner.Children.Add(new Border{BorderBrush=Brush("#9ABCF2"),BorderThickness=new Thickness(2,0,0,0),Padding=new Thickness(9,2,0,2),Margin=new Thickness(0,0,0,8),Child=new TextBlock{Text=Convert.ToString(content.Reply["name"])+"\n"+Convert.ToString(content.Reply["text"]),Foreground=Brush("#D0E0FC"),FontSize=11,TextWrapping=TextWrapping.Wrap,MaxHeight=58}});
          bool once=Once(message);var capturedMessage=message;var capturedUser=group!=null?GroupSender(message):peer;
          inner.Children.Add(new TextBlock{Text=content.Text,TextWrapping=TextWrapping.Wrap,FontFamily=new FontFamily(Setting("chatFont","Segoe UI")),FontSize=int.Parse(Setting("font","14")),Foreground=content.Valid?Readable(Setting("textColor","#F1F5FF"),own?Setting("bubbleColor",Setting("accent","#467AFF")):"#192334"):Brush("#FF9DA7")});
          if(once && !own && !queued){var open=new Button{Content=T("Open once"),Margin=new Thickness(0,8,0,0)};open.Click+=async(s,e)=>await Guard(()=>OpenOnce(capturedMessage));inner.Children.Add(open);}
          if(content.Media!=null && !once){inner.Children.Add(BuildMediaPreview(capturedMessage,content,capturedUser));}
          view.Time=new TextBlock{FontSize=10,Foreground=Readable("#B3C5E1",own?Setting("bubbleColor",Setting("accent","#467AFF")):"#192334"),HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,7,0,0)};inner.Children.Add(view.Time);
          view.Bubble=new Border{Background=own?Brush(Setting("bubbleColor",Setting("accent","#467AFF"))):Brush("#192334"),CornerRadius=new CornerRadius(14,14,own?4:14,own?14:4),Padding=new Thickness(15,11,15,9),Margin=new Thickness(0,0,0,Setting("compact","false")=="true"?6:13),MaxWidth=480,HorizontalAlignment=own?HorizontalAlignment.Right:HorizontalAlignment.Left,Child=inner,BorderThickness=new Thickness(1),BorderBrush=Brushes.Transparent};
          var menu=new ContextMenu();var copy=new MenuItem{Header=T("Copy message")};string copied=content.Text;copy.Click+=(s,e)=>Clipboard.SetText(copied);menu.Items.Add(copy);
          if(once)menu.Items.Clear();
          if(content.Valid && !once){var replyItem=new MenuItem{Header=T("Reply")};var capturedView=view;var capturedContent=content;replyItem.Click+=(s,e)=>BeginReply(capturedView.Message,capturedContent);menu.Items.Add(replyItem);}
          if(!queued && !own && group==null){var report=new MenuItem{Header=T("Report message")};report.Click+=(s,e)=>ReportDialog(capturedUser,capturedMessage,once?null:content);menu.Items.Add(report);}
          if(queued){var stop=new MenuItem{Header=T("Stop retrying this message"),ToolTip=T("Stops future attempts; it does not remove a message already delivered.")};var captured=message;stop.Click+=(s,e)=>{if(activeSend==clientId){ShowNotice("This message is currently sending. Wait for the current attempt before stopping retries.",false);return;}outbox.Remove(captured);SaveLocalState();UpdateMessageViews(false,false);};menu.Items.Add(stop);}
          view.Bubble.ContextMenu=menu;view.Root.Children.Add(view.Bubble);views[id]=view;
          if(!force && Setting("motion","true")=="true" && SystemParameters.ClientAreaAnimation)view.Bubble.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(150)));
        }
        view.Message=message;
        if(!queued)foreach(var item in view.Bubble.ContextMenu.Items.Cast<MenuItem>().Where(i=>Convert.ToString(i.Header)=="Stop retrying this message").ToArray())view.Bubble.ContextMenu.Items.Remove(item);
        view.Date.Text=when.Date==DateTime.Today?T("Today"):when.ToString("dddd, MMMM d");view.Date.Visibility=date!=when.Date?Visibility.Visible:Visibility.Collapsed;date=when.Date;
        view.Time.Text=when.ToString("HH:mm")+(queued?"  ◷ "+T("Queued"):!content.Valid?"  !":group!=null?"  ◇":own && (string)peer["id"]!=(string)me["id"]?(Convert.ToInt64(message["id"])<=readThrough?"  ✓✓ "+T("Read"):"  ✓ "+T("Sent")):"  ◇");
        int old=list.Children.IndexOf(view.Root);if(old!=index){if(old>=0)list.Children.RemoveAt(old);list.Children.Insert(index,view.Root);}index++;
      }
      foreach(string id in views.Keys.Where(k=>!wanted.Contains(k)).ToArray()){list.Children.Remove(views[id].Root);views.Remove(id);}
      if(rows.Length==0){emptyMessage=new TextBlock{Text="This is the beginning of your conversation. Say hello.",Foreground=Brush("#7C8DAA"),TextWrapping=TextWrapping.Wrap,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,35,0,0)};list.Children.Add(emptyMessage);}
      SearchMessages(false);if(manageScroll)Window.Dispatcher.BeginInvoke(new Action(()=>{if(!closing && version==conversationVersion && intent==scrollIntent){if(bottom)scroll.ScrollToEnd();else scroll.ScrollToVerticalOffset(previousOffset);}}),System.Windows.Threading.DispatcherPriority.Background);
    }
    System.Windows.Controls.Image ScalePreview(System.Windows.Media.Imaging.BitmapSource source){var image=new System.Windows.Controls.Image{Source=source,MaxWidth=240,MaxHeight=200,Stretch=Stretch.UniformToFill,Cursor=System.Windows.Input.Cursors.Hand,Margin=new Thickness(0,8,0,0)};System.Windows.Controls.ToolTipService.SetToolTip(image,T("Open photo"));return image;}
    Border BuildMediaPreview(Dictionary<string,object> message,Content content,Dictionary<string,object> user){
      var card=new Border{Margin=new Thickness(0,8,0,0),Cursor=System.Windows.Input.Cursors.Hand,CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),BorderBrush=Brush("#31466A"),ClipToBounds=true};
      var panel=new Grid();

      string mediaKind=MediaType(content.Media);

      if(mediaKind=="audio"||mediaKind=="file"){var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(new TextBlock{Text=mediaKind=="audio"?"♪":"📄",FontSize=20,Foreground=Brush("#8CB3FF"),Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center});row.Children.Add(new TextBlock{Text=mediaKind=="audio"?T("Play"):T("Attachment"),FontSize=13,Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center});card.Padding=new Thickness(12,10,12,10);card.Child=row;card.MouseLeftButtonUp+=(s,e)=>ShowMessageContent(message,content,user,false);return card;}
      System.Windows.Media.Imaging.BitmapSource photo=null;
      try{if(mediaKind=="photo")photo=ImageFromBytes(MediaPlain(content.Media));}catch{}
      if(photo!=null){panel.Children.Add(ScalePreview(photo));}
      else{
        var key=message.ContainsKey("id")?Convert.ToString(message["id"]):"q:"+Convert.ToString(message["clientId"]);
        System.Windows.Media.ImageSource cached;videoThumbCache.TryGetValue(key,out cached);
        if(cached!=null){panel.Children.Add(new System.Windows.Controls.Image{Source=cached,MaxWidth=240,MaxHeight=200,Stretch=Stretch.UniformToFill});}
        else{
          var pending=new TextBlock{Text="▶",FontSize=22,Foreground=Brush("#8CB3FF"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Width=120,Height=80};
          panel.Children.Add(pending);
          var bytes=MediaPlain(content.Media);
          var gen=generation;
          System.Threading.Tasks.Task.Run(async()=>{
            var thumb=await RenderVideoFirstFrame(bytes);
            if(thumb==null||closing||gen!=generation)return;
            await Window.Dispatcher.InvokeAsync(()=>{videoThumbCache[key]=thumb;UpdateMessageViews(false,false);});
          }).ContinueWith(_=>{},System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }
        var play=new TextBlock{Text="▶",FontSize=16,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
        var badge=new Border{Width=36,Height=36,CornerRadius=new CornerRadius(18),Background=Brush("#99000000"),Child=play,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
        panel.Children.Add(badge);
      }
      card.Child=panel;
      card.MouseLeftButtonUp+=(s,e)=>ShowMessageContent(message,content,user,false);
      return card;
    }
    async System.Threading.Tasks.Task<System.Windows.Media.ImageSource> RenderVideoFirstFrame(byte[] bytes){
      // Same short-lived decrypted file pattern as playback: written, read once for a
      // single scrubbed frame, then deleted immediately even on failure.
      string dir=System.IO.Path.Combine(ProfileDir,"playback");System.IO.Directory.CreateDirectory(dir);
      string file=System.IO.Path.Combine(dir,"thumb-"+Guid.NewGuid().ToString("N")+".mp4");
      var player=new System.Windows.Media.MediaPlayer{ScrubbingEnabled=true,Volume=0};
      try{
        System.IO.File.WriteAllBytes(file,bytes);
        var opened=new TaskCompletionSource<bool>();
        player.MediaOpened+=(s,e)=>opened.TrySetResult(true);
        player.MediaFailed+=(s,e)=>opened.TrySetResult(false);
        player.Open(new Uri(file));
        var done=await System.Threading.Tasks.Task.WhenAny(opened.Task,System.Threading.Tasks.Task.Delay(5000));
        if(done!=opened.Task||!opened.Task.Result)return null;
        player.Position=TimeSpan.FromMilliseconds(40);
        await System.Threading.Tasks.Task.Delay(120);
        var drawing=new System.Windows.Media.DrawingVisual();
        using(var context=drawing.RenderOpen())context.DrawVideo(player,new Rect(0,0,Math.Min(320,player.NaturalVideoWidth),Math.Max(1,Math.Min(320,player.NaturalVideoWidth)*(double)player.NaturalVideoHeight/Math.Max(1,player.NaturalVideoWidth))));
        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Min(320,player.NaturalVideoWidth==0?320:player.NaturalVideoWidth),(int)Math.Max(1,Math.Min(320,player.NaturalVideoWidth==0?180:player.NaturalVideoWidth)*(double)(player.NaturalVideoHeight==0?180:player.NaturalVideoHeight)/Math.Max(1,player.NaturalVideoWidth)),96,96,System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(drawing);bitmap.Freeze();return bitmap;
      }catch{return null;}
      finally{try{player.Close();}catch{}try{System.IO.File.Delete(file);}catch{}}
    }
    void OpenSearch(){if(peer==null&&group==null)return;Get<Border>("ChatSearchBar").Visibility=Visibility.Visible;Get<TextBox>("ChatSearchInput").Focus();SearchMessages(false);}
    void CloseSearch(){Get<Border>("ChatSearchBar").Visibility=Visibility.Collapsed;Get<TextBox>("ChatSearchInput").Clear();}
    void SearchMessages(bool jump) {
      string query=Get<TextBox>("ChatSearchInput").Text.Trim();searchMatches.Clear();
      foreach(var p in views){bool found=query!="" && p.Value.Content.Text.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0;p.Value.Bubble.BorderBrush=found?Brush("#D5BD73"):Brushes.Transparent;if(found)searchMatches.Add(p.Key);}
      if(searchIndex>=searchMatches.Count)searchIndex=searchMatches.Count-1;if(searchIndex<0 && searchMatches.Count>0)searchIndex=0;
      Get<TextBlock>("SearchCount").Text=query==""?"Loaded history":searchMatches.Count==0?"No matches":(searchIndex+1)+" / "+searchMatches.Count;
      if(jump && searchIndex>=0)views[searchMatches[searchIndex]].Root.BringIntoView();
    }
    void MoveSearch(int direction){if(searchMatches.Count==0)return;searchIndex=(searchIndex+direction+searchMatches.Count)%searchMatches.Count;SearchMessages(true);}
    void ShowEmoji() {
      var wrap=new WrapPanel{Width=240};var popup=new Popup{PlacementTarget=Get<Button>("EmojiButton"),Placement=PlacementMode.Top,StaysOpen=false,AllowsTransparency=true};
      popup.Child=new Border{Background=Brush("#172238"),BorderBrush=Brush("#31466A"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(9),Child=wrap};
      foreach(string emoji in new[]{"💙","✨","🌙","👋","😊","😂","😭","🫶","🔥","👍","👀","◇"}){var b=new Button{Content=emoji,FontSize=23,Width=56,Height=48,Background=Brushes.Transparent};string value=emoji;b.Click+=(s,e)=>{var composer=Get<TextBox>("Composer");int start=composer.SelectionStart;composer.SelectedText=value;composer.CaretIndex=start+value.Length;popup.IsOpen=false;composer.Focus();};wrap.Children.Add(b);}popup.IsOpen=true;
    }
  }
}
