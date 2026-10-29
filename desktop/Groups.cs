using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Illusion {
  public partial class Messenger {
    readonly List<Dictionary<string,object>> groups=new List<Dictionary<string,object>>();
    readonly List<Dictionary<string,object>> groupEvents=new List<Dictionary<string,object>>();
    readonly Dictionary<string,long> notifiedGroupUnread=new Dictionary<string,long>();
    Dictionary<string,object> group;
    string lastGroup="";
    bool groupsPrimed;

    static string GroupId(Dictionary<string,object> g){return Str(g,"id");}
    static string GroupName(Dictionary<string,object> g){return Str(g,"name","Group");}
    static int GroupTtl(Dictionary<string,object> g){return g!=null && g.ContainsKey("ttlSeconds")?Convert.ToInt32(g["ttlSeconds"]):0;}
    static long GroupUnread(Dictionary<string,object> g){return g!=null && g.ContainsKey("unread")?Convert.ToInt64(g["unread"]):0;}
    static List<Dictionary<string,object>> GroupMembers(Dictionary<string,object> g){
      var result=new List<Dictionary<string,object>>();
      if(g!=null && g.ContainsKey("members") && g["members"] is IEnumerable)foreach(var item in (IEnumerable)g["members"]){var member=item as Dictionary<string,object>;if(member!=null)result.Add(member);}
      return result;
    }
    static string GroupMemberLabel(Dictionary<string,object> user){string name=Str(user,"displayName","");string username=Str(user,"username");return name==""?"@"+username:name;}
    void PinGroupMembers(Dictionary<string,object> summary){if(me==null||summary==null)return;foreach(var member in GroupMembers(summary))if(Str(member,"id")!=(string)me["id"])Pin(member);}
    Border GroupAvatar(int size){
      var border=new Border{Width=size,Height=size,CornerRadius=new CornerRadius(size/2.0),Background=Brush("#233859"),BorderBrush=Brush("#8CB3FF"),BorderThickness=new Thickness(2),ClipToBounds=true};
      border.Child=new TextBlock{Text="◈",Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.Bold,FontSize=size/2.2};return border;
    }
    string GroupSubtitle(Dictionary<string,object> g){return GroupMembers(g).Count+" "+T("members")+"  ·  "+T("Group");}
    Dictionary<string,object> GroupSender(Dictionary<string,object> message){
      if(message.ContainsKey("sender") && message["sender"] is Dictionary<string,object>)return (Dictionary<string,object>)message["sender"];
      string id=Str(message,"senderId");
      if(me!=null && id==Str(me,"id"))return me;
      if(group!=null)foreach(var member in GroupMembers(group))if(Str(member,"id")==id)return member;
      return null;
    }
    string CurrentConversationId(){if(group!=null)return GroupId(group);if(peer!=null)return Str(peer,"id");return "";}

    void InitializeGroups(){
      Get<Button>("NewGroupButton").Click+=async(s,e)=>await Guard(NewGroupDialog);
    }
    void ResetGroups(){
      groups.Clear();groupEvents.Clear();notifiedGroupUnread.Clear();groupsPrimed=false;group=null;lastGroup="";
    }
    void UpdateGroupEventsPanel(){
      var panel=Get<Border>("GroupEventsPanel");var text=Get<TextBlock>("GroupEventsText");
      if(panel==null||text==null)return;
      if(group==null||groupEvents.Count==0){panel.Visibility=Visibility.Collapsed;text.Text="";return;}
      var lines=new List<string>();int start=Math.Max(0,groupEvents.Count-4);for(int i=start;i<groupEvents.Count;i++)lines.Add(GroupEventText(groupEvents[i]));
      text.Text=string.Join("\n",lines.ToArray());panel.Visibility=Visibility.Visible;
    }
    string GroupEventText(Dictionary<string,object> item){
      string type=Str(item,"type"),actor=Str(item,"actor","Illusion").Replace('\n',' '),target=Str(item,"target",actor).Replace('\n',' ');
      if(type=="join")return T("{actor} added {target}").Replace("{actor}",actor).Replace("{target}",target);
      if(type=="leave")return T("{actor} left the group").Replace("{actor}",actor);
      if(type=="rename")return T("{actor} renamed the group").Replace("{actor}",actor);
      if(type=="create")return T("{actor} created the group").Replace("{actor}",actor);
      return type;
    }
    void ReplaceGroupSummary(Dictionary<string,object> fresh){
      if(fresh==null)return;string id=GroupId(fresh);bool found=false;
      for(int i=0;i<groups.Count;i++)if(GroupId(groups[i])==id){groups[i]=fresh;found=true;break;}
      if(!found)groups.Add(fresh);
    }
    void ApplyGroupSummary(Dictionary<string,object> fresh,bool refreshChats){
      if(fresh==null)return;ReplaceGroupSummary(fresh);
      if(group!=null && GroupId(group)==GroupId(fresh)){
        group=fresh;
        Get<TextBlock>("ChatTitle").Text=GroupName(fresh);
        Get<TextBlock>("ChatSubtitle").Text=GroupSubtitle(fresh);
        Get<Button>("ChatOptionsButton").Content=T("Group settings");
        UpdateTimerHint();
      }
      if(refreshChats)UpdateChats(latestChats);
    }
    async Task FetchGroupsOnly(){
      if(me==null)return;int gen=generation;var response=await Api("GET","/v1/groups");if(gen!=generation||me==null)return;
      var next=((IEnumerable)response["groups"]).Cast<Dictionary<string,object>>().ToList();
      if(groupsPrimed){
        foreach(var item in next){
          string id=GroupId(item);long count=GroupUnread(item),old;notifiedGroupUnread.TryGetValue(id,out old);
          if(count>old && (group==null || GroupId(group)!=id || !Window.IsActive) && !mutedChats.Contains(id)){
            var notice=new Dictionary<string,object>(item);notice["displayName"]=GroupName(item);NotifyIncoming(notice,count-old);
          }
        }
      }
      var previous=groups.ToList();groups.Clear();groups.AddRange(next);notifiedGroupUnread.Clear();foreach(var item in next)notifiedGroupUnread[GroupId(item)]=GroupUnread(item);groupsPrimed=true;
      foreach(var old in previous){string oldId=GroupId(old);if(!next.Any(g=>GroupId(g)==oldId) && (group==null || GroupId(group)!=oldId))RemoveGroupArtifacts(oldId);}
      if(group!=null){
        var fresh=groups.FirstOrDefault(g=>GroupId(g)==GroupId(group));
        if(fresh==null)CloseGroupConversation(false);else ApplyGroupSummary(fresh,false);
      }
    }
    async Task RefreshGroups(){await FetchGroupsOnly();UpdateChats(latestChats);}

    public async Task SelectGroup(Dictionary<string,object> summary){
      if(me==null||summary==null)return;
      if(group!=null && GroupId(group)==GroupId(summary)){await FetchGroupConversation(false);return;}
      PinGroupMembers(summary);
      CaptureDraft();SaveLocalState();
      peer=null;group=summary;groupEvents.Clear();lastGroup=GroupId(summary);lastPeer="";conversationVersion++;lastReadSent=0;readThrough=0;
      pendingMedia=null;UpdateAttachment();Get<CheckBox>("ViewOnceCheck").IsChecked=false;Get<CheckBox>("ViewOnceCheck").IsEnabled=false;Get<Button>("AttachButton").IsEnabled=false;
      ClearContactWallpaper();
      messages.Clear();views.Clear();emptyMessage=null;
      var list=Get<StackPanel>("MessageList");while(list.Children.Count>1)list.Children.RemoveAt(1);
      string id=GroupId(summary);
      restoringDraft=true;Get<TextBox>("Composer").Text=drafts.ContainsKey(id)?drafts[id]:"";restoringDraft=false;reply=replyDrafts.ContainsKey(id)?replyDrafts[id]:null;UpdateReply();
      Get<TextBlock>("ChatTitle").Text=GroupName(summary);Get<TextBlock>("ChatSubtitle").Text=GroupSubtitle(summary);
      Get<Button>("ChatOptionsButton").Content=T("Group settings");Get<Button>("ChatOptionsButton").Visibility=Visibility.Visible;
      Get<Button>("VerifyButton").Visibility=Visibility.Collapsed;
      Get<TextBlock>("AttachmentNotice").Visibility=Visibility.Collapsed;Get<StackPanel>("AttachmentControls").Visibility=Visibility.Collapsed;Get<Border>("AttachmentBar").Visibility=Visibility.Collapsed;
      Get<StackPanel>("EmptyState").Visibility=Visibility.Collapsed;Get<ScrollViewer>("MessageScroll").Visibility=Visibility.Visible;Get<Border>("ComposerPanel").Visibility=Visibility.Visible;Get<Button>("NewMessagesButton").Visibility=Visibility.Collapsed;CloseSearch();
      UpdateGroupEventsPanel();UpdateTimerHint();
      if(history.ContainsKey(id)){foreach(var row in history[id])messages[Convert.ToInt64(row["id"])]=row;await DecodeGroupMessages(history[id]);}
      UpdateMessageViews(true,false);SaveLocalState();UpdateChats(latestChats);
      try{await FetchGroupConversation(messages.Count==0);}
      catch(ApiException error){if(error.Status==403||error.Status==404){CloseGroupConversation(false);ShowNotice(error.Message,true);return;}Connection(false);ShowNotice("Showing messages saved on this device. Reconnecting automatically.",false);}
      catch{Connection(false);ShowNotice("Showing messages saved on this device. Reconnecting automatically.",false);}
      if(group!=null){group["unread"]=0;UpdateChats(latestChats);}
      Get<TextBox>("Composer").Focus();
    }
    async Task DecodeGroupMessages(IEnumerable<Dictionary<string,object>> source){
      if(me==null)return;var rows=source.Where(m=>!decoded.ContainsKey(ContentKey(m))).ToArray();if(rows.Length==0)return;
      string myId=(string)me["id"],key=privateKey,own=(string)me["publicKey"];int gen=generation;
      var keys=new Dictionary<string,string>();keys[myId]=own;
      if(group!=null)foreach(var member in GroupMembers(group)){string id=Str(member,"id");string pub=Str(member,"publicKey");if(id!=""&&pub!="")keys[id]=pub;}
      foreach(var row in rows)if(row.ContainsKey("sender") && row["sender"] is Dictionary<string,object>){var sender=(Dictionary<string,object>)row["sender"];string id=Str(sender,"id"),pub=Str(sender,"publicKey");if(id!=""&&pub!="")keys[id]=pub;}
      string unavailable=T("Unable to verify or decrypt this message. Restore the correct key or verify with your contact.");
      var results=await Task.Run(()=>rows.Select(m=>{
        string senderId=Str(m,"senderId"),pub;Content content;
        if(!keys.TryGetValue(senderId,out pub))content=new Content{Text=unavailable,Valid=false};
        else content=Decode(m,myId,key,pub);
        return new KeyValuePair<string,Content>(ContentKey(m),content);
      }).ToArray());
      if(gen!=generation)return;foreach(var item in results)decoded[item.Key]=item.Value;
    }
    async Task FetchGroupConversation(bool initial){
      if(group==null||me==null)return;var active=group;string id=GroupId(active);int gen=generation,version=conversationVersion;
      long after=initial||messages.Count==0?0:messages.Keys.Last();
      var response=await Api("GET","/v1/groups/"+Uri.EscapeDataString(id)+"/messages"+(after>0?"?after="+after:""));
      if(gen!=generation||version!=conversationVersion||group==null)return;
      if(response.ContainsKey("group"))ApplyGroupSummary((Dictionary<string,object>)response["group"],false);
      groupEvents.Clear();if(response.ContainsKey("events"))foreach(var item in (IEnumerable)response["events"])groupEvents.Add((Dictionary<string,object>)item);
      var rows=((IEnumerable)response["messages"]).Cast<Dictionary<string,object>>().Where(m=>!Removed(m)).ToArray();await DecodeGroupMessages(rows);
      if(gen!=generation||version!=conversationVersion||group==null)return;
      bool bottom=AtBottom();
      foreach(var row in rows){messages[Convert.ToInt64(row["id"])]=row;var pending=outbox.FirstOrDefault(m=>m.ContainsKey("groupId")&&Str(m,"groupId")==id&&Str(m,"clientId")==Str(row,"clientId")&&Str(m,"senderId")==(string)me["id"]);if(pending!=null)outbox.Remove(pending);}
      readThrough=response.ContainsKey("readThrough")?Convert.ToInt64(response["readThrough"]):0;
      if(initial||after==0)Get<Button>("OlderButton").Visibility=(bool)response["hasMore"]?Visibility.Visible:Visibility.Collapsed;
      TrimConversation(false);UpdateGroupEventsPanel();UpdateTimerHint();UpdateMessageViews(initial||bottom,false);
      if(rows.Any(r=>Str(r,"senderId")!=(string)me["id"])&&!bottom&&!initial)Get<Button>("NewMessagesButton").Visibility=Visibility.Visible;
      history[id]=messages.Values.ToList();dirtyLocal=true;await MarkVisibleRead();
      if(group!=null && Window.IsActive && AtBottom())group["unread"]=0;UpdateChats(latestChats);
      if(after>0 && (bool)response["hasMore"] && rows.Length>0)await FetchGroupConversation(false);
    }
    async Task FetchGroupOlder(){
      if(group==null||me==null||messages.Count==0)return;string id=GroupId(group);int gen=generation,version=conversationVersion;
      Get<Button>("OlderButton").IsEnabled=false;
      try{
        var response=await Api("GET","/v1/groups/"+Uri.EscapeDataString(id)+"/messages?before="+messages.Keys.First());
        if(gen!=generation||version!=conversationVersion||group==null)return;
        if(response.ContainsKey("group"))ApplyGroupSummary((Dictionary<string,object>)response["group"],false);
        groupEvents.Clear();if(response.ContainsKey("events"))foreach(var item in (IEnumerable)response["events"])groupEvents.Add((Dictionary<string,object>)item);
        var rows=((IEnumerable)response["messages"]).Cast<Dictionary<string,object>>().Where(m=>!Removed(m)).ToArray();await DecodeGroupMessages(rows);
        if(gen!=generation||version!=conversationVersion||group==null)return;
        var scroll=Get<ScrollViewer>("MessageScroll");double offset=scroll.VerticalOffset,extent=scroll.ExtentHeight;loadingConversation=true;
        foreach(var row in rows)messages[Convert.ToInt64(row["id"])]=row;
        Get<Button>("OlderButton").Visibility=(bool)response["hasMore"]?Visibility.Visible:Visibility.Collapsed;TrimConversation(true);UpdateGroupEventsPanel();UpdateMessageViews(false,false,false);scroll.UpdateLayout();scroll.ScrollToVerticalOffset(offset+scroll.ExtentHeight-extent);history[id]=messages.Values.ToList();dirtyLocal=true;
      } finally {loadingConversation=false;Get<Button>("OlderButton").IsEnabled=true;}
    }
    async Task QueueGroupMessage(){
      if(group==null||me==null||sending)return;string raw=Get<TextBox>("Composer").Text,text=raw.Trim();if(text=="")return;
      if(text.Length>2000)throw new Exception("Keep each message under 2,000 characters.");
      if(outbox.Count>=100)throw new Exception("Your outbox has 100 messages. Reconnect to deliver them before adding more.");
      PinGroupMembers(group);
      var active=group;string gid=GroupId(active),sender=(string)me["id"],key=privateKey,clientId=Guid.NewGuid().ToString();var quoted=reply;int ttl=GroupTtl(active);
      var members=GroupMembers(active);
      sending=true;Get<Button>("SendButton").IsEnabled=false;int gen=generation;
      try{
        string content=json.Serialize(new Dictionary<string,object>{{"format","illusion.message.v2"},{"text",text},{"reply",quoted},{"media",null}});
        var policy=new Dictionary<string,object>{{"viewOnce",false},{"ttlSeconds",ttl}};
        var envelopes=await Task.Run(()=>{
          var list=new List<Dictionary<string,object>>();
          foreach(var member in members){string recipient=Str(member,"id"),pub=Str(member,"publicKey");if(recipient==""||pub=="")throw new Exception("A group member key is unavailable. Refresh the group.");list.Add(new Dictionary<string,object>{{"recipientId",recipient},{"envelope",Crypto.Encrypt(content,sender,recipient,clientId,key,pub,policy)}});}
          return list;
        });
        if(gen!=generation||me==null)return;
        var entry=new Dictionary<string,object>{{"senderId",sender},{"groupId",gid},{"clientId",clientId},{"envelopes",envelopes},{"viewOnce",false},{"createdAt",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}};
        if(outbox.Sum(m=>json.Serialize(m).Length)+json.Serialize(entry).Length>8000000)throw new Exception("Your encrypted outbox is full (8 MB). Reconnect before sending more.");
        outbox.Add(entry);
        try{SaveLocalState();}catch{outbox.Remove(entry);throw;}
        decoded[sender+"|"+clientId]=new Content{Text=text,Reply=quoted};
        if(drafts.ContainsKey(gid)&&drafts[gid]==raw)drafts.Remove(gid);replyDrafts.Remove(gid);
        if(group!=null && GroupId(group)==gid && Get<TextBox>("Composer").Text==raw){Get<TextBox>("Composer").Clear();reply=null;UpdateReply();UpdateMessageViews(true,false);}
        SaveLocalState();UpdateChats(latestChats);UpdateTimerHint();
      } finally {sending=false;Get<Button>("SendButton").IsEnabled=true;}
      await FlushOutbox();
    }
    async Task<bool> TryFlushGroupEntry(Dictionary<string,object> entry,int gen){
      string gid=Str(entry,"groupId");
      try{
        var response=await Api("POST","/v1/groups/"+Uri.EscapeDataString(gid)+"/messages",new{clientId=entry["clientId"],envelopes=entry["envelopes"]});
        if(gen!=generation||me==null)return false;
        var saved=(Dictionary<string,object>)response["message"];outbox.Remove(entry);
        if(decoded.ContainsKey(ContentKey(saved)))decoded[ContentKey(saved)].Deadline=saved.ContainsKey("expiresAt")?Convert.ToInt64(saved["expiresAt"]):0;
        if(!history.ContainsKey(gid))history[gid]=new List<Dictionary<string,object>>();if(!history[gid].Any(m=>Convert.ToInt64(m["id"])==Convert.ToInt64(saved["id"])))history[gid].Add(saved);
        if(group!=null && GroupId(group)==gid){
          if(response.ContainsKey("group"))ApplyGroupSummary((Dictionary<string,object>)response["group"],false);
          messages[Convert.ToInt64(saved["id"])]=saved;await DecodeGroupMessages(new[]{saved});UpdateMessageViews(AtBottom(),false);UpdateGroupEventsPanel();
        }
        SaveLocalState();UpdateChats(latestChats);UpdateTimerHint();return true;
      }catch(ApiException e){
        if(e.Status==401)throw;
        if(e.Status>=500||e.Status==429){Connection(false);ShowNotice("Message remains in your outbox: "+e.Message,false);return false;}
        outbox.Remove(entry);SaveLocalState();UpdateMessageViews(false,false);UpdateChats(latestChats);ShowNotice("Group message was not sent: "+e.Message,true);return true;
      }catch(OperationCanceledException){if(gen!=generation||closing||sessionStop==null)return false;Connection(false);return false;}
      catch{if(gen==generation&&!closing){Connection(false);ShowNotice("Your group message is queued safely. It will retry after the connection returns.",false);}return false;}
    }
    void RemoveGroupArtifacts(string id){
      if(id=="")return;ListBoxItem stale;if(chatItems.TryGetValue(id,out stale)){Get<ListBox>("ChatList").Items.Remove(stale);chatItems.Remove(id);}
      if(history.ContainsKey(id)){foreach(var row in history[id])decoded.Remove(ContentKey(row));history.Remove(id);}
      drafts.Remove(id);replyDrafts.Remove(id);for(int i=outbox.Count-1;i>=0;i--)if(Str(outbox[i],"groupId")==id)outbox.RemoveAt(i);
    }
    void CloseGroupConversation(bool localLeave){
      string id=group==null?"":GroupId(group);
      if(group!=null){groups.RemoveAll(g=>GroupId(g)==id);}
      RemoveGroupArtifacts(id);
      group=null;groupEvents.Clear();lastGroup="";lastPeer="";conversationVersion++;lastReadSent=0;readThrough=0;messages.Clear();views.Clear();decoded.Clear();
      UpdateGroupEventsPanel();Get<Border>("GroupEventsPanel").Visibility=Visibility.Collapsed;
      Get<StackPanel>("MessageList").Children.Clear();Get<StackPanel>("MessageList").Children.Add(Get<Button>("OlderButton"));emptyMessage=null;
      Get<ScrollViewer>("MessageScroll").Visibility=Visibility.Collapsed;Get<Border>("ComposerPanel").Visibility=Visibility.Collapsed;Get<StackPanel>("EmptyState").Visibility=Visibility.Visible;
      Get<Button>("VerifyButton").Visibility=Visibility.Collapsed;Get<TextBlock>("ChatTitle").Text=T("Make a connection.");Get<TextBlock>("ChatSubtitle").Text="";Get<TextBlock>("ComposerHint").Text="";Get<Button>("ChatOptionsButton").Content=T("Profile / Timer");
      Get<CheckBox>("ViewOnceCheck").IsEnabled=true;Get<Button>("AttachButton").IsEnabled=true;Get<TextBlock>("AttachmentNotice").Visibility=Visibility.Visible;Get<StackPanel>("AttachmentControls").Visibility=Visibility.Visible;
      if(localLeave)ShowNotice("You left the group. Its local history was removed from this device.",false);
      SaveLocalState();UpdateChats(latestChats);
    }
    async Task NewGroupDialog(){
      if(me==null)return;await Task.Yield();var panel=new StackPanel();panel.Children.Add(Label(T("New group"),24));
      panel.Children.Add(Label(T("Group name")));var name=new TextBox{MaxLength=60};panel.Children.Add(name);
      panel.Children.Add(Label(T("Member usernames")));var users=new TextBox{AcceptsReturn=true,MaxLength=500,MinHeight=70,TextWrapping=TextWrapping.Wrap};panel.Children.Add(users);
      panel.Children.Add(Label(T("Members can read messages sent after they join. Groups are limited to 20 people; attachments, view-once and reactions are not available in groups yet."),12));
      var create=new Button{Content=T("Create group"),Background=(Brush)Window.Resources["Accent"],Margin=new Thickness(0,10,0,0)};panel.Children.Add(create);var status=Label("");panel.Children.Add(status);
      var dialog=Dialog("New group",panel);int gen=generation;
      create.Click+=async(s,e)=>{create.IsEnabled=false;try{
        if(gen!=generation||me==null){dialog.Close();return;}
        var ids=new List<string>();foreach(string raw in users.Text.Split(new[]{',',';',' ','\n','\r','\t'},StringSplitOptions.RemoveEmptyEntries)){
          string username=raw.Trim().TrimStart('@');if(username=="")continue;var result=await Api("GET","/v1/users?username="+Uri.EscapeDataString(username));var found=(Dictionary<string,object>)result["user"];string id=(string)found["id"];if(id!=(string)me["id"]&&!ids.Contains(id))ids.Add(id);
        }
        if(ids.Count==0)throw new Exception("Add at least one other member by exact username.");
        if(ids.Count>19)throw new Exception("Groups can include up to 19 other members.");
        var created=await Api("POST","/v1/groups",new{name=name.Text.Trim(),memberIds=ids});dialog.Close();await RefreshGroups();await SelectGroup((Dictionary<string,object>)created["group"]);
      }catch(Exception error){status.Text=error.Message;create.IsEnabled=true;}};
      dialog.ShowDialog();
    }
    void ShowGroupSettings(){
      if(group==null||me==null)return;var current=group;string id=GroupId(current);int gen=generation;
      var panel=new StackPanel();panel.Children.Add(Label(T("Group settings"),22));
      panel.Children.Add(Label(T("Group name")));var nameBox=new TextBox{Text=GroupName(current),MaxLength=60};panel.Children.Add(nameBox);
      panel.Children.Add(Label("Automatically delete new messages"));var timerChoice=new ComboBox{ItemsSource=Array.ConvertAll(TimerNames,n=>T(n)),SelectedIndex=Math.Max(0,Array.IndexOf(ChatTimers,GroupTtl(current))),Margin=new Thickness(0,0,0,10)};panel.Children.Add(timerChoice);
      panel.Children.Add(Label(T("Members")));var membersText=LabelRaw(string.Join("\n",GroupMembers(current).Select(m=>GroupMemberLabel(m)+"  @"+Str(m,"username")).ToArray()),13);panel.Children.Add(membersText);
      panel.Children.Add(Label(T("Add member by username")));var addName=new TextBox{MaxLength=24};panel.Children.Add(addName);var add=new Button{Content=T("Add member")};panel.Children.Add(add);
      panel.Children.Add(Label(T("Members can read messages sent after they join. Groups are limited to 20 people; attachments, view-once and reactions are not available in groups yet."),12));
      var mute=new Button{Content=T(mutedChats.Contains(id)?"Unmute conversation":"Mute conversation"),Margin=new Thickness(0,8,0,0)};panel.Children.Add(mute);
      var save=new Button{Content=T("Save group"),Background=(Brush)Window.Resources["Accent"],Margin=new Thickness(0,12,0,0)};panel.Children.Add(save);
      var leave=new Button{Content=T("Leave group"),Margin=new Thickness(0,8,0,0)};panel.Children.Add(leave);var status=Label("");panel.Children.Add(status);
      var dialog=Dialog("Group settings",panel);
      mute.Click+=(s,e)=>{if(!mutedChats.Add(id))mutedChats.Remove(id);SaveMutes();UpdateChats(latestChats);mute.Content=T(mutedChats.Contains(id)?"Unmute conversation":"Mute conversation");};
      add.Click+=async(s,e)=>{add.IsEnabled=false;try{
        if(gen!=generation){dialog.Close();return;}string username=addName.Text.Trim().TrimStart('@');if(username=="")throw new Exception("Enter an exact username.");
        var result=await Api("GET","/v1/users?username="+Uri.EscapeDataString(username));var found=(Dictionary<string,object>)result["user"];var updated=await Api("POST","/v1/groups/"+Uri.EscapeDataString(id)+"/members",new{userId=found["id"]});ApplyGroupSummary((Dictionary<string,object>)updated["group"],false);membersText.Text=string.Join("\n",GroupMembers(group).Select(m=>GroupMemberLabel(m)+"  @"+Str(m,"username")).ToArray());addName.Clear();await FetchGroupConversation(false);UpdateChats(latestChats);
      }catch(Exception error){status.Text=error.Message;}finally{add.IsEnabled=true;}};
      save.Click+=async(s,e)=>{save.IsEnabled=false;try{
        if(gen!=generation){dialog.Close();return;}var updated=await Api("PUT","/v1/groups/"+Uri.EscapeDataString(id),new{name=nameBox.Text.Trim(),ttlSeconds=ChatTimers[timerChoice.SelectedIndex]});ApplyGroupSummary((Dictionary<string,object>)updated["group"],false);UpdateChats(latestChats);dialog.Close();
      }catch(Exception error){status.Text=error.Message;save.IsEnabled=true;}};
      leave.Click+=async(s,e)=>{if(MessageBox.Show(dialog,T("Leave this group?"),T("Leave group"),MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;await Guard(async()=>{await Api("DELETE","/v1/groups/"+Uri.EscapeDataString(id)+"/members");dialog.Close();CloseGroupConversation(true);});};
      dialog.ShowDialog();
    }
  }
}
