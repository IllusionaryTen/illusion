using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace Illusion {
  public partial class Messenger {
    static readonly int[] ChatTimers={0,60,3600,86400,604800,2592000};
    static readonly string[] TimerNames={"Off (default)","1 minute","1 hour","1 day","1 week","30 days"};
    static readonly string[] ProfileFonts=LoadFonts();
    static readonly string[] ProfileEffects={"None","Orbit","Glow","Prism","Checkerboard"};
    static FontFamily ProfileFont(string requested){return new FontFamily(ProfileFonts.Contains(requested)?requested:"Segoe UI");}
    readonly Dictionary<string,int> chatTimers=new Dictionary<string,int>();
    readonly HashSet<string> mediaFiles=new HashSet<string>();
    readonly HashSet<Window> contentWindows=new HashSet<Window>();
    readonly Dictionary<Window,long> contentDeadlines=new Dictionary<Window,long>();
    Dictionary<string,object> pendingMedia;
    bool openingOnce;
    static string Str(Dictionary<string,object> d,string key,string fallback=""){return d!=null && d.ContainsKey(key) && d[key]!=null?Convert.ToString(d[key]):fallback;}
    static bool Once(Dictionary<string,object> m){return m.ContainsKey("viewOnce") && Convert.ToBoolean(m["viewOnce"]);}
    static bool Removed(Dictionary<string,object> m){return Str(m,"removed")!="" || (m.ContainsKey("id") && m.ContainsKey("expiresAt") && Convert.ToInt64(m["expiresAt"])>0 && Convert.ToInt64(m["expiresAt"])<=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());}
    Dictionary<string,object> UserProfile(Dictionary<string,object> user){return user!=null && user.ContainsKey("profile")?user["profile"] as Dictionary<string,object>:new Dictionary<string,object>();}
    void InitializePrivacy(){
      Get<Button>("AvatarButton").Click+=(s,e)=>{if(me!=null)ShowProfile(me,true);};
      Get<Button>("ChatOptionsButton").Click+=async(s,e)=>await Guard(ChatOptions);
      Get<Button>("AttachButton").Click+=async(s,e)=>await Guard(AttachMedia);
      Get<Button>("ClearAttachmentButton").Click+=(s,e)=>{pendingMedia=null;UpdateAttachment();};
      Window.Closed+=(s,e)=>ClosePrivateContent();
      // A previous crash may have left a short-lived decrypted video in this profile.
      string dir=System.IO.Path.Combine(ProfileDir,"playback");
      if(Directory.Exists(dir))foreach(string f in Directory.GetFiles(dir,"illusion-*.mp4"))try{File.Delete(f);}catch{}
    }
    void UpdateSelfProfile(){if(me==null)return;var text=Get<TextBlock>("Avatar");var surface=text.Parent as Border;if(surface==null)return;var profile=UserProfile(me);surface.Background=Brush(Str(profile,"accent","#22395D"));text.Visibility=Visibility.Visible;try{var image=ImageData(Str(profile,"avatar"));if(image!=null){surface.Background=new ImageBrush(image){Stretch=Stretch.UniformToFill};text.Visibility=Visibility.Collapsed;}}catch{}text.Text=Str(me,"displayName","I").Substring(0,1).ToUpperInvariant();}
    void ClosePrivateContent(){foreach(var w in contentWindows.ToArray())w.Close();foreach(var f in mediaFiles.ToArray())try{File.Delete(f);}catch{}mediaFiles.Clear();}
    void ResetPrivacy(){ClosePrivateContent();pendingMedia=null;chatTimers.Clear();videoThumbCache.Clear();Get<CheckBox>("ViewOnceCheck").IsChecked=false;UpdateAttachment();}
    void UpdateAttachment(){Get<Border>("AttachmentBar").Visibility=pendingMedia==null?Visibility.Collapsed:Visibility.Visible;Get<TextBlock>("AttachmentLabel").Text=pendingMedia==null?"":Str(pendingMedia,"kind")=="photo"?"Photo attached · sent without end-to-end encryption":"Video attached · sent without end-to-end encryption";}
    async Task AttachMedia(){
      if(me==null || peer==null)return;var open=new OpenFileDialog{Filter="Photo or short MP4 video|*.png;*.jpg;*.jpeg;*.mp4"};if(open.ShowDialog(Window)!=true)return;
      if(new FileInfo(open.FileName).Length>4*1024*1024)throw new Exception("Choose a photo or short MP4 video under 4 MB.");
      int version=conversationVersion;string file=open.FileName;
      var media=await Task.Run(()=>{
        bool video=System.IO.Path.GetExtension(file).Equals(".mp4",StringComparison.OrdinalIgnoreCase);byte[] bytes;
        if(video){bytes=File.ReadAllBytes(file);if(bytes.Length<12 || Encoding.ASCII.GetString(bytes,4,4)!="ftyp")throw new Exception("Choose a valid MP4 file.");}
        else {string data=PrepareImage(file,1600,1600,4*1024*1024);bytes=Convert.FromBase64String(data.Substring(data.IndexOf(',')+1));}
        return new Dictionary<string,object>{{"kind",video?"video":"photo"},{"mime",video?"video/mp4":"image/jpeg"},{"data",Convert.ToBase64String(bytes)}};
      });
      if(version!=conversationVersion || me==null)return;pendingMedia=media;UpdateAttachment();
    }
    static string PrepareImage(string file,int width,int height,int limit){
      if(new FileInfo(file).Length>10*1024*1024)throw new Exception("Choose an image under 10 MB.");
      byte[] bytes=File.ReadAllBytes(file);int sourceWidth,sourceHeight;ImageSafety.Dimensions(bytes,out sourceWidth,out sourceHeight);double factor=Math.Min(1,Math.Min((double)width/sourceWidth,(double)height/sourceHeight));
      var image=new BitmapImage();using(var input=new MemoryStream(bytes)){image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=Math.Max(1,(int)(sourceWidth*factor));image.DecodePixelHeight=Math.Max(1,(int)(sourceHeight*factor));image.StreamSource=input;image.EndInit();}image.Freeze();
      if(image.PixelHeight>height){var scale=new TransformedBitmap(image,new ScaleTransform((double)height/image.PixelHeight,(double)height/image.PixelHeight));scale.Freeze();return EncodeImage(scale,limit);}
      return EncodeImage(image,limit);
    }
    static string EncodeImage(BitmapSource source,int limit){using(var stream=new MemoryStream()){var encoder=new JpegBitmapEncoder{QualityLevel=85};encoder.Frames.Add(BitmapFrame.Create(source));encoder.Save(stream);if(stream.Length>limit)throw new Exception("This image is too large after resizing. Choose a smaller image.");return "data:image/jpeg;base64,"+Convert.ToBase64String(stream.ToArray());}}
    static BitmapSource ImageData(string data){
      if(string.IsNullOrEmpty(data))return null;byte[] bytes=Convert.FromBase64String(data.Substring(data.IndexOf(',')+1));if(bytes.Length>4*1024*1024)throw new Exception("Image is too large.");
      int width,height;ImageSafety.Dimensions(bytes,out width,out height);double scale=Math.Min(1,Math.Min(1600.0/width,1600.0/height));
      using(var stream=new MemoryStream(bytes)){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=Math.Max(1,(int)(width*scale));image.DecodePixelHeight=Math.Max(1,(int)(height*scale));image.StreamSource=stream;image.EndInit();image.Freeze();return image;}
    }
    Border ProfileAvatar(Dictionary<string,object> user,int size){
      var p=UserProfile(user);var border=new Border{Width=size,Height=size,CornerRadius=new CornerRadius(size/2.0),Background=Brush(Str(p,"accent","#233859")),BorderBrush=Brush(Str(p,"textColor","#BFD6FF")),BorderThickness=new Thickness(Str(p,"effect","Orbit")=="None"?0:2),ClipToBounds=true};
      System.Windows.Automation.AutomationProperties.SetName(border,"Profile picture for @"+user["username"]);
      try{var image=ImageData(Str(p,"avatar"));if(image!=null){border.Background=new ImageBrush(image){Stretch=Stretch.UniformToFill};return border;}}catch{}
      border.Child=new TextBlock{Text=Str(user,"displayName","I").Substring(0,1).ToUpperInvariant(),Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.Bold,FontSize=size/2.5};return border;
    }
    void DecorateProfile(StackPanel panel,Dictionary<string,object> user){
      var p=UserProfile(user);var scene=new Grid{Height=145,Background=Brush(Str(p,"accent","#467AFF")),ClipToBounds=true,Margin=new Thickness(0,0,0,12)};
      try{var image=ImageData(Str(p,"banner"));if(image!=null)scene.Background=new ImageBrush(image){Stretch=Stretch.UniformToFill};}catch{}
      string effect=Str(p,"effect","Orbit");var canvas=new Canvas{IsHitTestVisible=false};scene.Children.Add(canvas);
      if(effect=="Orbit" || effect=="Prism")for(int i=0;i<3;i++){var ring=new System.Windows.Shapes.Ellipse{Width=200+i*40,Height=60+i*20,Stroke=effect=="Prism"?Brush(new[]{"#B7DBFF","#F6BCFF","#B0FFDF"}[i]):Brushes.White,Opacity=effect=="Prism"?.7:.25,StrokeThickness=2,RenderTransform=new RotateTransform(-25+i*(effect=="Prism"?20:0))};Canvas.SetLeft(ring,90);Canvas.SetTop(ring,40);canvas.Children.Add(ring);}
      if(effect=="Checkerboard")for(int y=0;y<6;y++)for(int x=0;x<17;x++)if((x+y)%2==0){var tile=new System.Windows.Shapes.Rectangle{Width=25,Height=25,Fill=Brushes.White,Opacity=.15};Canvas.SetLeft(tile,x*25);Canvas.SetTop(tile,y*25);canvas.Children.Add(tile);}
      var avatar=ProfileAvatar(user,76);avatar.HorizontalAlignment=HorizontalAlignment.Left;avatar.VerticalAlignment=VerticalAlignment.Bottom;avatar.Margin=new Thickness(14);scene.Children.Add(avatar);
      if(effect=="Glow")avatar.Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.White,BlurRadius=24,ShadowDepth=0,Opacity=.8};
      if(effect!="None" && Setting("motion","true")=="true" && SystemParameters.ClientAreaAnimation)canvas.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.45,1,TimeSpan.FromSeconds(2)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
      panel.Children.Add(scene);panel.Children.Add(new TextBlock{Text=Str(user,"displayName"),Foreground=Readable(Str(p,"textColor","#F1F5FF"),((SolidColorBrush)Window.Resources["PanelBackground"]).Color.ToString()),FontFamily=ProfileFont(Str(p,"font","Segoe UI")),FontSize=25,TextWrapping=TextWrapping.Wrap});panel.Children.Add(Label("@"+user["username"]));panel.Children.Add(LabelRaw(Str(p,"bio","No description yet."),14));
    }
    void ShowProfile(Dictionary<string,object> user,bool edit){
      if(edit){ShowProfileEditor(user);return;}var panel=new StackPanel();DecorateProfile(panel,user);var report=new Button{Content=T("Report profile")};panel.Children.Add(report);var view=Dialog("Profile",panel);report.Click+=(s,e)=>ReportDialog(user,null,null);view.ShowDialog();
    }
    async Task ChatOptions(){
      if(peer==null || me==null)return;var user=peer;string id=(string)user["id"];int gen=generation;
      var response=await Api("GET","/v1/chat-settings?peer="+id);if(gen!=generation)return;chatTimers[id]=Convert.ToInt32(response["ttlSeconds"]);
      var panel=new StackPanel();panel.Children.Add(Label("Conversation with @"+user["username"],18));
      var profile=new Button{Content=T("View profile")};panel.Children.Add(profile);profile.Click+=(s,e)=>ShowProfile(user,false);
      panel.Children.Add(Label("Automatically delete new messages"));var timerChoice=new ComboBox{ItemsSource=TimerNames,SelectedIndex=Array.IndexOf(ChatTimers,chatTimers[id]),Margin=new Thickness(0,0,0,10)};panel.Children.Add(timerChoice);
      panel.Children.Add(Label("Either participant can change this shared timer. It applies to messages sent afterward, starting when the relay accepts them. Expired content is removed from the relay and this app's cache. Copies, screenshots and backups cannot be recalled."));
      var save=new Button{Content=T("Save chat timer")};panel.Children.Add(save);var status=Label("");panel.Children.Add(status);
      var block=new Button{Content=(bool)response["blocked"]?"Unblock this user":"Block this user",IsEnabled=id!=(string)me["id"],Margin=new Thickness(0,12,0,0)};panel.Children.Add(block);
      var report=new Button{Content=T("Report profile"),IsEnabled=id!=(string)me["id"],Margin=new Thickness(0,8,0,0)};panel.Children.Add(report);report.Click+=(s,e)=>ReportDialog(user,null,null);
      var dialog=Dialog("Chat settings",panel);
      save.Click+=async(s,e)=>{save.IsEnabled=false;try{if(gen!=generation){dialog.Close();return;}await Api("PUT","/v1/chat-settings",new{peer=id,ttlSeconds=ChatTimers[timerChoice.SelectedIndex]});chatTimers[id]=ChatTimers[timerChoice.SelectedIndex];UpdateTimerHint();dialog.Close();}catch(Exception error){status.Text=error.Message;}finally{save.IsEnabled=true;}};
      block.Click+=async(s,e)=>{block.IsEnabled=false;try{if(gen!=generation){dialog.Close();return;}await Api("PUT","/v1/block",new{peer=id,blocked=!(bool)response["blocked"]});dialog.Close();}catch(Exception error){status.Text=error.Message;block.IsEnabled=true;}};dialog.ShowDialog();
    }
    void UpdateTimerHint(){if(peer==null)return;int ttl;chatTimers.TryGetValue((string)peer["id"],out ttl);Get<TextBlock>("ComposerHint").Text="◇ Encrypted · Auto-delete: "+TimerNames[Array.IndexOf(ChatTimers,ttl)];}
    void RemoveFromCache(IEnumerable<long> ids){
      var gone=new HashSet<long>(ids);if(gone.Count==0)return;
      foreach(var bucket in history.Values)foreach(var m in bucket.Where(m=>gone.Contains(Convert.ToInt64(m["id"]))).ToArray()){decoded.Remove(ContentKey(m));bucket.Remove(m);}
      foreach(long id in messages.Keys.Where(gone.Contains).ToArray()){decoded.Remove(ContentKey(messages[id]));messages.Remove(id);}
      foreach(var c in contacts.Values.Concat(latestChats).Distinct())if(c.ContainsKey("lastMessage") && gone.Contains(Convert.ToInt64(((Dictionary<string,object>)c["lastMessage"])["id"])))c.Remove("lastMessage");
      foreach(string id in replyDrafts.Keys.ToArray())if(replyDrafts[id].ContainsKey("id") && gone.Contains(Convert.ToInt64(replyDrafts[id]["id"])))replyDrafts.Remove(id);
      if(reply!=null && gone.Contains(Convert.ToInt64(reply["id"]))){reply=null;UpdateReply();}
      dirtyLocal=true;if(peer!=null)UpdateMessageViews(false,false);UpdateChats(latestChats);
    }
    void ExpireLocal(){long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();foreach(var item in contentDeadlines.ToArray())if(item.Value>0 && item.Value<=now)item.Key.Close();RemoveFromCache(history.Values.SelectMany(x=>x).Concat(messages.Values).Where(Removed).Where(m=>m.ContainsKey("id")).Select(m=>Convert.ToInt64(m["id"])).Distinct().ToArray());foreach(var item in decoded.Where(p=>p.Value.Deadline>0 && p.Value.Deadline<=now).ToArray())decoded.Remove(item.Key);}
    async Task OpenOnce(Dictionary<string,object> stub){
      if(openingOnce || me==null || (string)stub["recipientId"]!=(string)me["id"])return;
      if(MessageBox.Show(Window,"Open this view-once message? It will disappear even if opening fails or this window is closed. Screenshots and recordings are still possible.","Open once",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
      openingOnce=true;int gen=generation;string key=privateKey,myId=(string)me["id"];var user=peer;
      try{var response=await Api("POST","/v1/open",new{id=stub["id"]});if(gen!=generation)return;var message=(Dictionary<string,object>)response["message"];RemoveFromCache(new[]{Convert.ToInt64(stub["id"])});SaveLocalState();var content=await Task.Run(()=>Decode(message,myId,key,(string)user["publicKey"],true));if(gen!=generation)return;ShowMessageContent(message,content,user,true);}finally{openingOnce=false;}
    }
    void ShowMessageContent(Dictionary<string,object> message,Content content,Dictionary<string,object> user,bool once){
      if(!content.Valid)throw new Exception(content.Text);var panel=new StackPanel();panel.Children.Add(Label(once?"VIEW ONCE · Closing this window removes this copy.":"Attachment",16));
      panel.Children.Add(new TextBlock{Text=content.Text,TextWrapping=TextWrapping.Wrap,FontSize=16,Foreground=Brushes.White,Margin=new Thickness(0,10,0,15)});
      string videoFile=null;MediaElement player=null;
      if(content.Media!=null){string kind=Str(content.Media,"kind"),data=Str(content.Media,"data");if(data.Length>5600000)throw new Exception("Attachment is too large.");
        if(kind=="photo")panel.Children.Add(new Image{Source=ImageData("data:image/jpeg;base64,"+data),MaxHeight=430,Stretch=Stretch.Uniform});
        else if(kind=="video"){
          byte[] bytes=Convert.FromBase64String(data);if(bytes.Length<12 || Encoding.ASCII.GetString(bytes,4,4)!="ftyp")throw new Exception("Invalid MP4 attachment.");
          string dir=System.IO.Path.Combine(ProfileDir,"playback");Directory.CreateDirectory(dir);videoFile=System.IO.Path.Combine(dir,"illusion-"+Guid.NewGuid().ToString("N")+".mp4");File.WriteAllBytes(videoFile,bytes);Array.Clear(bytes,0,bytes.Length);mediaFiles.Add(videoFile);
          player=new MediaElement{Source=new Uri(videoFile),LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Close,MaxHeight=400,Stretch=Stretch.Uniform};panel.Children.Add(player);var controls=new StackPanel{Orientation=Orientation.Horizontal};var play=new Button{Content=T("Play")};var pause=new Button{Content=T("Pause"),Margin=new Thickness(10,0,0,0)};controls.Children.Add(play);controls.Children.Add(pause);panel.Children.Add(controls);var captured=player;play.Click+=(s,e)=>captured.Play();pause.Click+=(s,e)=>captured.Pause();
          player.MediaFailed+=(s,e)=>MessageBox.Show("Windows could not play this video. Try an MP4 encoded with H.264/AAC.","Illusion");
        }else throw new Exception("Unsupported attachment type.");
      }
      var report=new Button{Content=T("Report this message"),Margin=new Thickness(0,15,0,0),IsEnabled=(string)message["senderId"]!=(string)me["id"]};panel.Children.Add(report);report.Click+=(s,e)=>ReportDialog(user,message,content);
      var dialog=Dialog(once?"View once":"Attachment",panel);contentWindows.Add(dialog);contentDeadlines[dialog]=message.ContainsKey("expiresAt")?Convert.ToInt64(message["expiresAt"]):0;
      dialog.Closed+=(s,e)=>{contentWindows.Remove(dialog);contentDeadlines.Remove(dialog);if(player!=null){player.Stop();player.Close();player.Source=null;}panel.Children.Clear();if(videoFile!=null)try{File.Delete(videoFile);mediaFiles.Remove(videoFile);}catch{};};dialog.ShowDialog();
    }
    void ReportDialog(Dictionary<string,object> target,Dictionary<string,object> message,Content content){
      if(me==null || (string)target["id"]==(string)me["id"])return;var panel=new StackPanel();panel.Children.Add(Label(message==null?"Report @"+target["username"]+"'s public profile":"Report this message from @"+target["username"],18));
      panel.Children.Add(Label(message==null?"This shares a snapshot of the public profile, your account ID, reason and note with this network's moderators.":"This shares your account ID, the message identifier, reason and note. Only the selected message text/caption is shared if you choose the evidence option. Other chats and private keys stay on your device."));
      var reason=new ComboBox{ItemsSource=new[]{"spam","harassment","threats","sexual-content","other"},SelectedIndex=0};panel.Children.Add(reason);
      panel.Children.Add(Label("Note (optional)"));var note=new TextBox{MaxLength=1000,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=65};panel.Children.Add(note);
      CheckBox share=null;if(message!=null){share=new CheckBox{Content=T("Share this message's text/caption as verified evidence"),IsChecked=false,IsEnabled=content!=null && content.Valid,Foreground=Brushes.White,Margin=new Thickness(0,14,0,8)};panel.Children.Add(share);if(content!=null)panel.Children.Add(LabelRaw(content.Text.Length>2000?content.Text.Substring(0,2000):content.Text));}
      var status=Label("");panel.Children.Add(status);var send=new Button{Content=T("Submit report")};panel.Children.Add(send);var dialog=Dialog("Report",panel);int gen=generation;
      send.Click+=async(s,e)=>{send.IsEnabled=false;try{if(gen!=generation || me==null){dialog.Close();return;}var b=new Dictionary<string,object>{{"targetId",target["id"]},{"reason",reason.SelectedItem},{"note",note.Text}};if(message!=null){b["messageId"]=message["id"];if(share.IsChecked==true)b["evidence"]=new{envelope=message["envelope"],material=Crypto.ReportMaterial(message,(string)me["id"],privateKey)};}var result=await Api("POST","/v1/reports",b);status.Text="Report #"+result["reportId"]+" submitted. The network's moderators can review it.";send.Content=T("Submitted");}catch(Exception error){status.Text=error.Message;send.IsEnabled=true;}};dialog.ShowDialog();
    }
  }
}
