using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Automation;

namespace Illusion {
  public sealed class ColorStudio : StackPanel {
    readonly Slider[] channels=new Slider[3];readonly TextBox[] values=new TextBox[3];readonly TextBox hex=new TextBox();
    readonly Slider hue=new Slider{Minimum=0,Maximum=360,Value=220};readonly Canvas plane=new Canvas{Height=120,Focusable=true,ClipToBounds=true};
    readonly Rectangle baseColor=new Rectangle(),white=new Rectangle(),black=new Rectangle();readonly Ellipse marker=new Ellipse{Width=12,Height=12,Stroke=Brushes.White,StrokeThickness=2,Fill=Brushes.Transparent};
    readonly Border sample=new Border{Height=28,CornerRadius=new CornerRadius(6),Margin=new Thickness(0,6,0,6)};
    bool updating;double saturation=.7,brightness=1;string selected="#467AFF";
    public event EventHandler ColorChanged;
    public string Hex {get{return selected;}set{Color c;try{c=(Color)ColorConverter.ConvertFromString(value);}catch{return;}Set(c);}}
    public ColorStudio(string title,string initial){
      Margin=new Thickness(0,8,0,14);Children.Add(new TextBlock{Text=title,Foreground=Brushes.White,Margin=new Thickness(0,0,0,8)});
      var rainbow=new LinearGradientBrush();rainbow.StartPoint=new Point(0,.5);rainbow.EndPoint=new Point(1,.5);for(int i=0;i<=6;i++)rainbow.GradientStops.Add(new GradientStop(Hsv(i*60,1,1),i/6.0));
      Children.Add(new Border{Height=9,CornerRadius=new CornerRadius(4),Background=rainbow});hue.Margin=new Thickness(0,2,0,8);Children.Add(hue);AutomationProperties.SetName(hue,title+" hue");
      white.Fill=new LinearGradientBrush(Colors.White,Colors.Transparent,0);black.Fill=new LinearGradientBrush(Colors.Transparent,Colors.Black,90);
      plane.Children.Add(baseColor);plane.Children.Add(white);plane.Children.Add(black);plane.Children.Add(marker);Children.Add(plane);AutomationProperties.SetName(plane,title+" saturation and brightness; arrow keys adjust");
      plane.SizeChanged+=(s,e)=>DrawPlane();plane.MouseLeftButtonDown+=(s,e)=>{plane.Focus();plane.CaptureMouse();Pick(e.GetPosition(plane));};plane.MouseMove+=(s,e)=>{if(plane.IsMouseCaptured)Pick(e.GetPosition(plane));};plane.MouseLeftButtonUp+=(s,e)=>plane.ReleaseMouseCapture();
      plane.KeyDown+=(s,e)=>{double step=(Keyboard.Modifiers&ModifierKeys.Shift)!=0?.1:.01;if(e.Key==Key.Left)saturation-=step;else if(e.Key==Key.Right)saturation+=step;else if(e.Key==Key.Up)brightness+=step;else if(e.Key==Key.Down)brightness-=step;else return;e.Handled=true;Set(Hsv(hue.Value,Clamp(saturation),Clamp(brightness)));};
      hue.ValueChanged+=(s,e)=>{if(!updating)Set(Hsv(hue.Value,saturation,brightness));};Children.Add(sample);
      for(int i=0;i<3;i++){int channel=i;var row=new Grid{Margin=new Thickness(0,3,0,3)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(30)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(62)});row.Children.Add(new TextBlock{Text=new[]{"R","G","B"}[i],Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center});
        channels[i]=new Slider{Minimum=0,Maximum=255,TickFrequency=1,IsSnapToTickEnabled=true,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(4,0,12,0)};values[i]=new TextBox{MaxLength=3,Padding=new Thickness(7,5,7,5)};Grid.SetColumn(channels[i],1);Grid.SetColumn(values[i],2);row.Children.Add(channels[i]);row.Children.Add(values[i]);Children.Add(row);AutomationProperties.SetName(channels[i],title+" "+new[]{"red","green","blue"}[i]);AutomationProperties.SetName(values[i],title+" "+new[]{"red","green","blue"}[i]+" 0 to 255");
        channels[i].ValueChanged+=(s,e)=>{if(!updating)Set(Color.FromRgb((byte)channels[0].Value,(byte)channels[1].Value,(byte)channels[2].Value));};values[i].TextChanged+=(s,e)=>{byte n;if(!updating && byte.TryParse(values[channel].Text,out n)){var c=(Color)ColorConverter.ConvertFromString(selected);Set(Color.FromRgb(channel==0?n:c.R,channel==1?n:c.G,channel==2?n:c.B));}};
      }
      hex.MaxLength=7;hex.Margin=new Thickness(0,6,0,0);Children.Add(hex);AutomationProperties.SetName(hex,title+" hexadecimal color");hex.TextChanged+=(s,e)=>{if(!updating && System.Text.RegularExpressions.Regex.IsMatch(hex.Text,@"\A#[0-9a-fA-F]{6}\z"))Hex=hex.Text;};Hex=initial;
    }
    static double Clamp(double n){return Math.Max(0,Math.Min(1,n));}
    void Pick(Point p){saturation=Clamp(p.X/Math.Max(1,plane.ActualWidth));brightness=1-Clamp(p.Y/Math.Max(1,plane.ActualHeight));Set(Hsv(hue.Value,saturation,brightness));}
    void DrawPlane(){foreach(var r in new[]{baseColor,white,black}){r.Width=plane.ActualWidth;r.Height=plane.Height;}baseColor.Fill=new SolidColorBrush(Hsv(hue.Value,1,1));Canvas.SetLeft(marker,saturation*plane.ActualWidth-6);Canvas.SetTop(marker,(1-brightness)*plane.Height-6);}
    void Set(Color c){updating=true;selected=string.Format("#{0:X2}{1:X2}{2:X2}",c.R,c.G,c.B);double r=c.R/255.0,g=c.G/255.0,b=c.B/255.0,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),d=max-min;brightness=max;saturation=max==0?0:d/max;if(d>0){double h=max==r?((g-b)/d)%6:max==g?(b-r)/d+2:(r-g)/d+4;hue.Value=(h*60+360)%360;}var bytes=new[]{c.R,c.G,c.B};for(int i=0;i<3;i++){channels[i].Value=bytes[i];values[i].Text=bytes[i].ToString();}hex.Text=selected;sample.Background=new SolidColorBrush(c);DrawPlane();updating=false;if(ColorChanged!=null)ColorChanged(this,EventArgs.Empty);}
    static Color Hsv(double h,double s,double v){h=(h%360+360)%360;double c=v*s,x=c*(1-Math.Abs(h/60%2-1)),m=v-c;double[] rgb=h<60?new[]{c,x,0}:h<120?new[]{x,c,0}:h<180?new[]{0,c,x}:h<240?new[]{0,x,c}:h<300?new[]{x,0,c}:new[]{c,0,x};return Color.FromRgb((byte)Math.Round((rgb[0]+m)*255),(byte)Math.Round((rgb[1]+m)*255),(byte)Math.Round((rgb[2]+m)*255));}
  }
  public partial class Messenger {
    static string[] LoadFonts(){using(var stream=typeof(Messenger).Assembly.GetManifestResourceStream("fonts.json"))using(var reader=new StreamReader(stream))return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<string[]>(reader.ReadToEnd());}
    static double Luminance(Color c){Func<byte,double> f=b=>b/255.0<=.04045?b/255.0/12.92:Math.Pow((b/255.0+.055)/1.055,2.4);return .2126*f(c.R)+.7152*f(c.G)+.0722*f(c.B);}
    static double Contrast(Color a,Color b){double x=Luminance(a),y=Luminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
    Brush Readable(string requested,string background){var a=(Color)ColorConverter.ConvertFromString(requested);var b=(Color)ColorConverter.ConvertFromString(background);if(Contrast(a,b)>=4.5)return new SolidColorBrush(a);return new SolidColorBrush(Contrast(Colors.White,b)>=Contrast(Colors.Black,b)?Colors.White:Colors.Black);}
    void ApplyPalette(){string theme=Setting("theme",Setting("accent","#467AFF")=="#297C91"?"ice":Setting("accent","")=="#56657C"?"silver":"blue");string[] p=theme=="ice"?new[]{"#091E25","#10323C","#17434B","#26717B","#92E9E8","#C0E4E8"}:theme=="silver"?new[]{"#1D1E23","#292C34","#363B45","#727E95","#D2DCED","#C7CDDA"}:new[]{"#090C13","#101622","#192334","#31466A","#8CB3FF","#A8B9D2"};foreach(var item in new[]{new KeyValuePair<string,string>("AppBackground",p[0]),new KeyValuePair<string,string>("PanelBackground",p[1]),new KeyValuePair<string,string>("ControlBackground",p[2]),new KeyValuePair<string,string>("PanelBorder",p[3]),new KeyValuePair<string,string>("ThemeHighlight",p[4]),new KeyValuePair<string,string>("MutedText",p[5])})Window.Resources[item.Key]=Brush(item.Value);}
    void ShowProfileEditor(Dictionary<string,object> user){
      int gen=generation;var p=new Dictionary<string,object>(UserProfile(user));var panel=new StackPanel{Margin=new Thickness(22)};var preview=new StackPanel{Margin=new Thickness(22)};
      panel.Children.Add(Label("Make your profile yours",25));panel.Children.Add(Label("Display name"));var name=new TextBox{Text=Str(user,"displayName"),MaxLength=40};panel.Children.Add(name);AutomationProperties.SetName(name,"Profile display name");
      panel.Children.Add(Label("Description"));var bio=new TextBox{Text=Str(p,"bio"),MaxLength=300,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=70};panel.Children.Add(bio);AutomationProperties.SetName(bio,"Profile description");
      var avatar=new Button{Content=T("Choose profile picture"),Margin=new Thickness(0,12,0,6)};var banner=new Button{Content=T("Choose banner")};var clear=new Button{Content=T("Remove picture and banner"),Margin=new Thickness(0,6,0,0)};panel.Children.Add(avatar);panel.Children.Add(banner);panel.Children.Add(clear);
      var accent=new ColorStudio("Profile accent",Str(p,"accent","#467AFF"));var color=new ColorStudio("Profile text",Str(p,"textColor","#F1F5FF"));
      panel.Children.Add(new Expander{Header=T("Accent color · RGB studio"),Content=accent,Foreground=Brushes.White,Margin=new Thickness(0,15,0,8)});panel.Children.Add(new Expander{Header=T("Text color · RGB studio"),Content=color,Foreground=Brushes.White,Margin=new Thickness(0,0,0,8)});
      panel.Children.Add(Label("Profile font · installed fonts may vary by device"));var font=new ComboBox{ItemsSource=ProfileFonts,SelectedItem=Str(p,"font","Segoe UI"),Margin=new Thickness(0,0,0,12)};panel.Children.Add(font);AutomationProperties.SetName(font,"Profile font");
      panel.Children.Add(Label("Profile effect"));var effect=new ComboBox{ItemsSource=ProfileEffects,SelectedItem=Str(p,"effect","Orbit")};panel.Children.Add(effect);AutomationProperties.SetName(effect,"Profile effect");
      var status=Label("Changes stay private until you publish. Text contrast is adjusted in the preview when needed.");panel.Children.Add(status);var save=new Button{Content=T("Publish profile"),Background=(Brush)Window.Resources["Accent"]};panel.Children.Add(save);
      var grid=new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition());grid.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});var previewScroll=new ScrollViewer{Content=preview,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(previewScroll,1);grid.Children.Add(previewScroll);
      var dialog=new Window{Title=T("Your profile"),Owner=Window,Width=880,Height=700,MinWidth=720,MinHeight=480,MaxHeight=Math.Max(480,SystemParameters.WorkArea.Height-30),WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=(Brush)Window.Resources["PanelBackground"],Foreground=Brushes.White,Content=grid,Resources=Window.Resources};
      Action render=()=>{p["bio"]=bio.Text;p["accent"]=accent.Hex;p["textColor"]=color.Hex;p["font"]=font.SelectedItem??"Segoe UI";p["effect"]=effect.SelectedItem??"None";var sample=new Dictionary<string,object>(user);sample["displayName"]=string.IsNullOrWhiteSpace(name.Text)?"Your name":name.Text;sample["profile"]=p;preview.Children.Clear();preview.Children.Add(Label("LIVE PREVIEW",11));DecorateProfile(preview,sample);preview.Children.Add(Label("This is how signed-in people see your public profile. No changes are uploaded until you choose Publish profile."));};
      name.TextChanged+=(s,e)=>render();bio.TextChanged+=(s,e)=>render();accent.ColorChanged+=(s,e)=>render();color.ColorChanged+=(s,e)=>render();font.SelectionChanged+=(s,e)=>render();effect.SelectionChanged+=(s,e)=>render();clear.Click+=(s,e)=>{p["avatar"]="";p["banner"]="";render();};
      avatar.Click+=async(s,e)=>{try{var picker=new Microsoft.Win32.OpenFileDialog{Filter="Picture|*.png;*.jpg;*.jpeg"};if(picker.ShowDialog(dialog)==true){p["avatar"]=await System.Threading.Tasks.Task.Run(()=>PrepareImage(picker.FileName,256,256,256000));render();}}catch(Exception error){status.Text=error.Message;}};
      banner.Click+=async(s,e)=>{try{var picker=new Microsoft.Win32.OpenFileDialog{Filter="Picture|*.png;*.jpg;*.jpeg"};if(picker.ShowDialog(dialog)==true){p["banner"]=await System.Threading.Tasks.Task.Run(()=>PrepareImage(picker.FileName,1000,400,256000));render();}}catch(Exception error){status.Text=error.Message;}};
      save.Click+=async(s,e)=>{save.IsEnabled=false;try{if(gen!=generation || me==null){dialog.Close();return;}render();var response=await Api("PUT","/v1/profile",new{displayName=name.Text,profile=p});if(gen!=generation)return;me=(Dictionary<string,object>)response["user"];UpdateSelfProfile();contacts[(string)me["id"]]=me;settings["account"]=Crypto.Protect(json.Serialize(me));SaveSettings();UpdateChats(latestChats);dialog.Close();}catch(Exception error){status.Text=error.Message;}finally{save.IsEnabled=true;}};render();dialog.Closed+=(s,e)=>preview.Children.Clear();dialog.ShowDialog();
    }
  }
}
