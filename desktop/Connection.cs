using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Illusion {
  public partial class Messenger {
    ConnectionProfile defaultConnection;
    void InitializeConnection() {
      defaultConnection=ConnectionProfile.Embedded();
      string saved=Setting("server","");
      Get<TextBox>("ServerInput").Text=saved!=""?saved:defaultConnection.Relay!=""?defaultConnection.Relay:"http://127.0.0.1:3789";
      Get<TextBlock>("NetworkName").Text=Setting("networkName",defaultConnection.Name);
      bool ready=saved!="" || defaultConnection.Relay!="";
      Get<TextBlock>("NetworkDescription").Text=ready?"Your connection is already set. All your conversations use it automatically.":"Import your host's connection file. Local testing is available under Advanced.";
      Get<Expander>("ConnectionAdvanced").IsExpanded=false;
      Get<Button>("ImportConnectionButton").Click+=async(s,e)=>await Guard(ImportConnection);
    }
    Task ImportConnection() {
      if(me!=null)throw new Exception("Sign out before changing your Illusion network.");
      var dialog=new OpenFileDialog{Filter="Illusion connection|*.illusion;*.illusion.json",Title=T("Choose your Illusion connection file")};
      if(dialog.ShowDialog(Window)!=true)return Task.FromResult(0);
      if(new FileInfo(dialog.FileName).Length>6000)throw new Exception("The connection file is too large.");
      var profile=ConnectionProfile.Parse(File.ReadAllText(dialog.FileName));
      if(MessageBox.Show(Window,"Connect to "+profile.Name+"?\n\n"+profile.Relay+"\n\nUse connection files from a host you trust. Account credentials will be sent to this address at sign-in.","Choose your Illusion network",MessageBoxButton.YesNo,MessageBoxImage.Information)!=MessageBoxResult.Yes)return Task.FromResult(0);
      ApplyConnection(profile);return Task.FromResult(0);
    }
    void ApplyConnection(ConnectionProfile profile) {
      if(me!=null)throw new Exception("Sign out before changing your Illusion network.");
      string address=ConnectionProfile.NormalizeRelay(profile.Relay),old=Setting("server","");
      if(old!="" && !string.Equals(address,old,StringComparison.OrdinalIgnoreCase)) {
        // Accounts and remembered credentials belong to their original network.
        settings.Remove("token");settings.Remove("account");settings.Remove("username");
        Get<TextBox>("UsernameInput").Clear();Get<PasswordBox>("PasswordInput").Clear();
        token="";privateKey="";
      }
      settings["server"]=address;settings["networkName"]=profile.Name;settings["lan"]="false";
      Get<CheckBox>("LanConsent").IsChecked=false;Get<TextBox>("ServerInput").Text=address;
      Get<TextBlock>("NetworkName").Text=profile.Name;Get<TextBlock>("NetworkDescription").Text="Connection ready. Sign in or create an account; every chat uses this connection.";
      Get<Expander>("ConnectionAdvanced").IsExpanded=false;Get<TextBlock>("AuthError").Text="Connection saved. No address needs to be typed.";SaveSettings();
    }
  }
}
