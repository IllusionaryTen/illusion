using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace Illusion {
  public partial class Messenger {
    const string PolicyVersion="2026-10-19.1";
    Dictionary<string,object> EmbeddedLegal(){using(var stream=typeof(Messenger).Assembly.GetManifestResourceStream("legal.json"))using(var reader=new StreamReader(stream))return json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());}
    void ShowPolicy(string id){var docs=(Dictionary<string,object>)EmbeddedLegal()["documents"];var panel=new StackPanel();panel.Children.Add(Label(Convert.ToString(docs[id]),14));Dialog("Illusion · "+id,panel).ShowDialog();}
    void InitializeConsent(){
      foreach(string id in new[]{"privacy","terms"}){string captured=id;Get<Button>(id=="privacy"?"PrivacyLink":"TermsLink").Click+=(s,e)=>ShowPolicy(captured);}
      var named=new Dictionary<string,string>{{"UsernameInput","Username"},{"PasswordInput","Password"},{"DisplayNameInput","Display name"},{"InviteInput","Invitation code"},{"Composer","Message"},{"SearchInput","Find username"},{"ServerInput","Relay address"},{"AgeBand","Age range"},{"AcceptTerms","Agree to Terms of service"},{"AcknowledgePrivacy","Acknowledge reading Privacy policy"},{"SettingsButton","Appearance and privacy settings"},{"SendButton","Send encrypted message"},{"SavedButton","Saved messages"},{"SearchChatButton","Search conversation"},{"EmojiButton","Insert emoji"},{"AttachButton","Attach photo or video"}};
      foreach(var p in named){var control=Window.FindName(p.Key) as DependencyObject;if(control!=null)AutomationProperties.SetName(control,p.Value);}
      foreach(var p in new Dictionary<string,string>{{"CancelReplyButton","Cancel reply"},{"CloseSearchButton","Close conversation search"},{"NextMatchButton","Next search match"},{"PreviousMatchButton","Previous search match"},{"ClearAttachmentButton","Remove attachment"},{"NewMessagesButton","Jump to new messages"},{"ChatOptionsButton","Profile, timer, blocking and reporting"},{"VerifyButton","Verify encryption keys"},{"OlderButton","Load earlier messages"},{"ChatsButton","Focus conversation finder"},{"ImportKeyButton","Restore encryption key"},{"ImportConnectionButton","Import connection invitation"}}){var control=Window.FindName(p.Key) as DependencyObject;if(control!=null)AutomationProperties.SetName(control,p.Value);}
    }
    void ShowPrivacyAndData(){
      var panel=new StackPanel();panel.Children.Add(Label("Privacy, policies and your data",23));panel.Children.Add(Label("No payments, analytics or marketing emails. Chat encryption is unaudited; reports share only selected evidence. Operator details are shown in each policy."));
      foreach(string id in new[]{"privacy","terms","refunds","cookies","licenses","accessibility"}){string captured=id;var b=new Button{Content=id=="cookies"?"Cookies and device storage":id,Margin=new Thickness(0,0,0,7)};b.Click+=(s,e)=>ShowPolicy(captured);panel.Children.Add(b);}
      var request=new Button{Content=T("Request account deletion"),IsEnabled=me!=null,Margin=new Thickness(0,15,0,0)};panel.Children.Add(request);panel.Children.Add(Label("Deletion requests go to this network's host for manual fulfillment. Other people's copies, your key backups and independent host backups cannot be remotely recalled. You can continue reading policies without accepting marketing or optional data collection."));
      var dialog=Dialog("Privacy and data",panel);request.Click+=(s,e)=>{dialog.Close();RequestDeletionDialog();};dialog.ShowDialog();
    }
    void RequestDeletionDialog(){
      if(me==null)return;int gen=generation;string username=(string)me["username"];var panel=new StackPanel();panel.Children.Add(Label("Request deletion of @"+username,22));panel.Children.Add(Label("This submits a request to remove your account, profiles and relay-held messages after host review. It does not immediately delete the account or erase another user's saved copies. Type your username and password to confirm."));
      var name=new TextBox();var password=new PasswordBox();panel.Children.Add(Label("Username"));panel.Children.Add(name);panel.Children.Add(Label("Password"));panel.Children.Add(password);AutomationProperties.SetName(name,"Confirm username for deletion request");AutomationProperties.SetName(password,"Current password for deletion request");var status=Label("");panel.Children.Add(status);var button=new Button{Content=T("Submit deletion request")};panel.Children.Add(button);var dialog=Dialog("Request account deletion",panel);
      button.Click+=async(s,e)=>{button.IsEnabled=false;try{if(gen!=generation){dialog.Close();return;}var result=await Api("POST","/v1/deletion-request",new{confirmUsername=name.Text.Trim().ToLowerInvariant(),password=password.Password});password.Clear();status.Text="Request #"+result["requestId"]+" is pending host review. Keep this reference.";button.Content=T("Request submitted");}catch(Exception error){status.Text=error.Message;button.IsEnabled=true;}};dialog.ShowDialog();password.Clear();
    }
  }
}
