using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Illusion {
  public static class CloudDesktopTests {
    static int checks;
    static void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS: "+text);}
    static async Task Wait(Func<bool> value,string text){var until=DateTime.UtcNow.AddSeconds(25);while(!value()){if(DateTime.UtcNow>until)throw new Exception("Timed out: "+text);await Task.Delay(100);}Check(true,text);}
    static object Field(Messenger m,string name){return typeof(Messenger).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(m);}
    static void Click(Messenger m,string name){m.Get<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
    static async Task Register(Messenger m,string name,string server){m.Window.Show();m.Get<TextBox>("ServerInput").Text=server;m.Get<TextBox>("UsernameInput").Text=name;m.Get<PasswordBox>("PasswordInput").Password="a strong cloud desktop password";Click(m,"SwitchAuthButton");m.Get<ComboBox>("AgeBand").SelectedIndex=4;m.Get<CheckBox>("AcceptTerms").IsChecked=true;m.Get<CheckBox>("AcknowledgePrivacy").IsChecked=true;m.Get<TextBox>("DisplayNameInput").Text=name;Click(m,"AuthButton");await Wait(()=>m.Get<Border>("AuthOverlay").Visibility==Visibility.Collapsed,"Cloud account registers from Windows: "+name);await Wait(()=>(bool)Field(m,"cloudLiveActive"),"Windows client authenticates its cloud live socket: "+name);}
    static string Text(DependencyObject node){var block=node as TextBlock;string result=block==null?"":block.Text;for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++)result+="\n"+Text(System.Windows.Media.VisualTreeHelper.GetChild(node,i));return result;}
    static async Task Run(string server){
      string root=Path.Combine(Path.GetTempPath(),"illusion-cloud-native-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
      Messenger alice=null,bob=null;
      try{
        alice=new Messenger(Path.Combine(root,"alice"));bob=new Messenger(Path.Combine(root,"bob"));
        await Register(alice,"native_cloud_a",server);await Register(bob,"native_cloud_b",server);
        alice.Get<TextBox>("SearchInput").Text="native_cloud_b";Click(alice,"FindButton");await Wait(()=>alice.Get<TextBlock>("ChatSubtitle").Text.StartsWith("@native_cloud_b "),"Windows cloud lookup finds recipient");
        bob.Get<TextBox>("SearchInput").Text="native_cloud_a";Click(bob,"FindButton");await Wait(()=>bob.Get<TextBlock>("ChatSubtitle").Text.StartsWith("@native_cloud_a "),"Windows cloud lookup finds sender");
        alice.Get<TextBox>("Composer").Text="Windows through Cloudflare ✨";Click(alice,"SendButton");await Wait(()=>alice.Get<TextBox>("Composer").Text=="","Windows sends encrypted message to the hosted relay");
        await Wait(()=>Text(bob.Get<StackPanel>("MessageList")).Contains("Windows through Cloudflare ✨"),"Recipient renders and decrypts the cloud message after a live notification");
        Check((bool)Field(alice,"cloudLiveActive")&&(bool)Field(bob,"cloudLiveActive"),"Both Windows cloud sockets stay active after delivery");
        File.WriteAllText("artifacts/desktop-cloudflare-qa.txt",checks+" native Cloudflare integration checks passed against the local Workers runtime.\n");
      }finally{if(alice!=null)alice.Window.Close();if(bob!=null)bob.Window.Close();}
    }
    [STAThread] public static int Main(string[] args){if(args.Length!=1)return 2;var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int exit=0;app.Startup+=async(s,e)=>{try{await Run(args[0]);}catch(Exception error){Console.Error.WriteLine(error);exit=1;}finally{app.Shutdown();}};app.Run();return exit;}
  }
}
