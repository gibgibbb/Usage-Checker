using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms=System.Windows.Forms;

namespace CodexMonitor {
 public sealed class Widget : Window {
  readonly string store=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageMonitor");
  readonly TextBlock five=new TextBlock(),week=new TextBlock(),status=new TextBlock();
  readonly TextBlock heading=new TextBlock(),clock=new TextBlock(),fiveReset=new TextBlock(),weekReset=new TextBlock();
  readonly Border fiveFill=new Border(),weekFill=new Border(),fiveTrack=new Border(),weekTrack=new Border();
  readonly List<TextBlock> muted=new List<TextBlock>();
  bool dockTop=true; string preview;
  readonly Border shell=new Border(); readonly Forms.NotifyIcon tray=new Forms.NotifyIcon();
  readonly DispatcherTimer timer=new DispatcherTimer();
  RpcClient rpc; Usage usage; string problem="Connecting",theme="system",authFingerprint; bool busy,closing,exitRequested,smoke;
  int failures; DateTime nextRead=DateTime.MinValue; DateTime? desktopGone; bool desktopSeen;
  public Widget(bool smokeTest,string previewTheme=null) {
   preview=previewTheme;
   smoke=smokeTest; Title="Codex Usage Monitor"; Width=264; Height=164; ResizeMode=ResizeMode.NoResize;
   WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
   Topmost=true; ShowInTaskbar=true; ShowActivated=false; UseLayoutRounding=true;
   FontFamily=new FontFamily("Segoe UI");
   WindowStartupLocation=WindowStartupLocation.Manual;
   shell.CornerRadius=new CornerRadius(18); shell.BorderThickness=new Thickness(1); shell.Padding=new Thickness(15,11,15,11);
   var stack=new StackPanel();
   var header=new DockPanel {Margin=new Thickness(0,0,0,9)};
   heading.Text="Codex";heading.FontSize=13;heading.FontWeight=FontWeights.SemiBold;
   clock.FontSize=11;clock.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(clock,Dock.Right);
   header.Children.Add(clock);header.Children.Add(heading);muted.Add(clock);stack.Children.Add(header);
   stack.Children.Add(UsageRow("5-hour",five,fiveReset,fiveTrack,fiveFill));
   stack.Children.Add(new Border{Height=9});
   stack.Children.Add(UsageRow("Weekly",week,weekReset,weekTrack,weekFill));
   shell.Child=stack;Content=shell;
   MouseLeftButtonDown+=(s,e)=>{if(e.ClickCount==2){Hide();return;}try{DragMove();dockTop=false;Save();}catch{}};
   MouseRightButtonUp+=(s,e)=>{var menu=new ContextMenu();
    AddMenu(menu,"Refresh usage",()=>{nextRead=DateTime.MinValue;Refresh();});
    AddMenu(menu,"Always on top: "+(Topmost?"on":"off"),()=>{Topmost=!Topmost;Save();});
    AddMenu(menu,"Theme: System",()=>SetTheme("system"));AddMenu(menu,"Theme: Dark",()=>SetTheme("dark"));AddMenu(menu,"Theme: Light",()=>SetTheme("light"));
    AddMenu(menu,"Move to top-center",Center);AddMenu(menu,"Minimize to tray",Hide);AddMenu(menu,"Exit",Quit);menu.IsOpen=true;};
   tray.Icon=System.Drawing.SystemIcons.Information;tray.Text="Codex Usage Monitor";
   var context=new Forms.ContextMenuStrip();context.Items.Add("Show widget",null,(s,e)=>Dispatcher.Invoke(new Action(()=>Show())));
   context.Items.Add("Refresh usage",null,(s,e)=>Dispatcher.Invoke(new Action(()=>{nextRead=DateTime.MinValue;Refresh();})));
   context.Items.Add("Exit",null,(s,e)=>Dispatcher.Invoke(new Action(Quit)));tray.ContextMenuStrip=context;
   tray.DoubleClick+=(s,e)=>Dispatcher.Invoke(new Action(()=>Show()));tray.Visible=!smoke;
   LoadSettings();SetTheme(preview??theme);
   Loaded+=async (s,e)=>{if(dockTop)Center();desktopSeen=DesktopPresent();
    if(preview!=null){usage=new Usage{Five=79,Week=97,Updated=DateTime.UtcNow,FiveReset=(long)(DateTime.UtcNow.AddMinutes(87)-new DateTime(1970,1,1)).TotalSeconds,WeekReset=(long)(DateTime.UtcNow.AddDays(4)-new DateTime(1970,1,1)).TotalSeconds};problem=null;UpdateText();await Task.Delay(200);CaptureSmoke();Quit();}
    else Refresh();};
   timer.Interval=TimeSpan.FromSeconds(2);timer.Tick+=(s,e)=>Tick();timer.Start();
   Closing+=(s,e)=>{if(!exitRequested){e.Cancel=true;Hide();return;}closing=true;timer.Stop();Save();tray.Dispose();if(rpc!=null)rpc.Dispose();};
  }
  void AddMenu(ContextMenu menu,string name,Action action){var item=new MenuItem{Header=name};item.Click+=(s,e)=>action();menu.Items.Add(item);}
  FrameworkElement UsageRow(string label,TextBlock value,TextBlock reset,Border track,Border fill){
   var stack=new StackPanel();var line=new DockPanel();
   value.FontSize=20;value.FontWeight=FontWeights.SemiBold;value.Text="—";value.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(value,Dock.Right);
   var title=new TextBlock{Text=label,FontSize=11,VerticalAlignment=VerticalAlignment.Center};muted.Add(title);
   line.Children.Add(value);line.Children.Add(title);stack.Children.Add(line);
   track.Height=4;track.CornerRadius=new CornerRadius(2);track.Margin=new Thickness(0,3,0,3);
   fill.Height=4;fill.CornerRadius=new CornerRadius(2);fill.HorizontalAlignment=HorizontalAlignment.Left;fill.Width=0;track.Child=fill;stack.Children.Add(track);
   reset.FontSize=10;reset.Text="Reset unavailable";muted.Add(reset);stack.Children.Add(reset);return stack;
  }
  void Center(){
   var handle=new System.Windows.Interop.WindowInteropHelper(this).Handle;
   var screen=IsLoaded?Forms.Screen.FromHandle(handle):Forms.Screen.FromPoint(Forms.Cursor.Position);
   var source=PresentationSource.FromVisual(this);var transform=source!=null?source.CompositionTarget.TransformFromDevice:Matrix.Identity;
   var origin=transform.Transform(new Point(screen.WorkingArea.Left,screen.WorkingArea.Top));
   var size=transform.Transform(new Vector(screen.WorkingArea.Width,screen.WorkingArea.Height));
   Left=origin.X+(size.X-Width)/2;Top=origin.Y+12;dockTop=true;Save();
  }
  void SetTheme(string value){theme=value;bool dark=value=="dark";
   if(value=="system")try{dark=Convert.ToInt32(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1))==0;}catch{}
   // Native equivalents of daisyUI base-100/base-200/base-content/success tokens.
   var base100=(Color)ColorConverter.ConvertFromString(dark?"#191B1E":"#FFFFFF");
   var base200=(Color)ColorConverter.ConvertFromString(dark?"#24272B":"#F3F5F4");
   var content=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#F3F5F4":"#19231F"));
   var subtle=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#A6ADA9":"#616D66"));
   var success=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#40D982":"#16834B"));
   shell.Background=new LinearGradientBrush(base200,base100,90);
   shell.BorderBrush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#484D49":"#CDD7D0"));
   heading.Foreground=content;five.Foreground=week.Foreground=success;fiveFill.Background=weekFill.Background=success;
   fiveTrack.Background=weekTrack.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#343A36":"#E0E7E2"));
   foreach(var item in muted)item.Foreground=subtle;
   Save();
  }
  void Save(){if(preview!=null)return;try{Directory.CreateDirectory(store);string path=Path.Combine(store,"settings.json");
   var data=Json.Write(new {theme=theme,topmost=Topmost,left=Left,top=Top,dockTop=dockTop,layoutVersion=2});File.WriteAllText(path+".tmp",data);
   if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);
  }catch{}}
  void LoadSettings(){try{var data=Json.Parse(File.ReadAllText(Path.Combine(store,"settings.json")));theme=Json.Text(data,"theme");if(theme!="light"&&theme!="dark")theme="system";
   if(Json.Get(data,"topmost") is bool)Topmost=(bool)data["topmost"];
   dockTop=Json.Get(data,"layoutVersion")==null||Json.Get(data,"dockTop")==null||Convert.ToBoolean(data["dockTop"]);
   double x=Convert.ToDouble(data["left"]),y=Convert.ToDouble(data["top"]);
   if(!Double.IsNaN(x)&&!Double.IsNaN(y)&&x>=SystemParameters.VirtualScreenLeft&&x+Width<=SystemParameters.VirtualScreenLeft+SystemParameters.VirtualScreenWidth&&y>=SystemParameters.VirtualScreenTop&&y+Height<=SystemParameters.VirtualScreenTop+SystemParameters.VirtualScreenHeight){WindowStartupLocation=WindowStartupLocation.Manual;Left=x;Top=y;}
  }catch{}}
  static bool DesktopPresent(){foreach(var name in new[]{"ChatGPT","Codex"})foreach(var p in Process.GetProcessesByName(name))using(p){
   try{string path=p.MainModule.FileName;if(path.IndexOf("OpenAI.Codex",StringComparison.OrdinalIgnoreCase)>=0||path.IndexOf(@"\OpenAI\ChatGPT\",StringComparison.OrdinalIgnoreCase)>=0)return true;}catch{}
  }return false;}
  void Tick(){if(closing)return;
   SetThemeIfSystem();
   if(!smoke){bool present=DesktopPresent();if(present){desktopSeen=true;desktopGone=null;}else if(desktopSeen){if(!desktopGone.HasValue)desktopGone=DateTime.UtcNow;if(DateTime.UtcNow-desktopGone.Value>TimeSpan.FromSeconds(8)){Quit();return;}}}
   UpdateText();if(DateTime.UtcNow>=nextRead)Refresh();
  }
  int themeTicks;void SetThemeIfSystem(){if(theme=="system"&&++themeTicks>=15){themeTicks=0;SetTheme(theme);}}
  async void Refresh(){if(busy||closing)return;busy=true;
   try{
    if(rpc==null){rpc=new RpcClient();await rpc.Start();}
    var auth=await rpc.Call("account/read",new {refreshToken=false});
    var account=Json.Obj(Json.Get(auth,"account"));
    string fingerprint=Json.Hash(Json.Text(account,"type")+":"+Json.Text(account,"email"));
    if(authFingerprint!=null&&authFingerprint!=fingerprint)usage=null;
    authFingerprint=fingerprint;
    if(Json.Text(account,"type")!="chatgpt"){usage=null;throw new IOException("Sign in to Codex with ChatGPT to read subscription limits.");}
    var result=await rpc.Call("account/rateLimits/read",new {});
    usage=Usage.From(result);problem=null;failures=0;nextRead=DateTime.UtcNow.AddSeconds(60);
   }catch(Exception e){problem=e is TimeoutException?e.Message:"Usage unavailable â€” check Codex sign-in or connection.";
    // Keep a same-session snapshot visibly stale; never infer a new quota.
    failures++;nextRead=DateTime.UtcNow.AddSeconds(Math.Min(300,30*Math.Pow(2,Math.Min(failures-1,4))));if(rpc!=null){rpc.Dispose();rpc=null;}
   }finally{busy=false;}
   if(!closing){UpdateText();if(smoke){await Task.Delay(200);CaptureSmoke();Quit();}}
  }
  void UpdateText(){five.Text=Usage.Percent(usage==null?null:usage.Five);week.Text=Usage.Percent(usage==null?null:usage.Week);
   bool stale=usage!=null&&(problem!=null||DateTime.UtcNow-usage.Updated>TimeSpan.FromSeconds(120));
   clock.Text=DateTime.Now.ToString("HH:mm");
   fiveFill.Width=232*(usage==null?0:usage.Five.GetValueOrDefault())/100;weekFill.Width=232*(usage==null?0:usage.Week.GetValueOrDefault())/100;
   fiveReset.Text=CompactReset(usage==null?null:usage.FiveReset,false);weekReset.Text=CompactReset(usage==null?null:usage.WeekReset,true);
   if(stale){fiveReset.Text="Stale · last known usage";weekReset.Text="Waiting for connection";}
   else if(usage==null){fiveReset.Text=busy?"Connecting…":"Usage unavailable";weekReset.Text="Waiting for connection";}
   fiveFill.Opacity=weekFill.Opacity=stale?0.4:1;
   status.Text=problem!=null?(usage==null?"Offline":"Stale"):"Status â€”";
   if(stale)status.Text="Stale";
   five.ToolTip="5-hour usage remaining";week.ToolTip="Weekly usage remaining";
   string info=usage==null?"No successful usage read yet.":"5-hour remaining: "+five.Text+"\nWeekly remaining: "+week.Text+"\nUpdated: "+usage.Updated.ToLocalTime().ToString("T")+"\n"+ResetText("5-hour",usage.FiveReset)+ResetText("Weekly",usage.WeekReset);
   shell.ToolTip=info+(problem!=null?"\n"+problem:"")+"\nTask status is not connected in this phase.\nRight-click for options; double-click to hide.";
   tray.Text=("Codex: "+five.Text+" / "+week.Text+(stale?" (stale)":"")).Substring(0,Math.Min(63,("Codex: "+five.Text+" / "+week.Text+(stale?" (stale)":"")).Length));
  }
  string CompactReset(long? seconds,bool weekly){if(!seconds.HasValue)return "Reset unavailable";try{
   var at=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(seconds.Value);var left=at-DateTime.UtcNow;
   if(left.TotalSeconds<=0)return "Reset pending refresh";
   if(weekly)return "Resets "+at.ToLocalTime().ToString("ddd, MMM d · HH:mm");
   return "Resets in "+(int)left.TotalHours+"h "+left.Minutes+"m";
  }catch{return "Reset unavailable";}}
  string ResetText(string label,long? seconds){if(!seconds.HasValue)return "";try{return label+" reset: "+new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(seconds.Value).ToLocalTime().ToString("g")+"\n";}catch{return "";}}
  void CaptureSmoke(){try{string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","probe","results");Directory.CreateDirectory(folder);
   var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(folder,"widget"+(preview==null?"":"-"+preview)+".png")))encoder.Save(file);
   if(preview!=null)return;
   File.WriteAllText(Path.Combine(folder,"widget-smoke.json"),Json.Write(new {success=usage!=null&&problem==null,five=usage==null?null:usage.Five,weekly=usage==null?null:usage.Week,accountFingerprint=usage==null?null:Json.Hash(usage.Account),problem=problem,workingSet=Process.GetCurrentProcess().WorkingSet64}));
  }catch{}}
  void Quit(){exitRequested=true;Close();}
 }
 public static class Program {
  [STAThread] public static void Main(string[] args){bool created;using(var mutex=new Mutex(true,"Local\\CodexUsageMonitor.Widget"+(args.Any(a=>a.StartsWith("--preview-"))?".Preview":""),out created)){
   if(!created)return;
   try{var app=new Application();app.Run(new Widget(args.Contains("--smoke")||args.Any(a=>a.StartsWith("--preview-")),args.Contains("--preview-dark")?"dark":args.Contains("--preview-light")?"light":null));}
   catch(Exception e){Forms.MessageBox.Show("Codex Usage Monitor could not start: "+e.Message,"Codex Usage Monitor");}
  }}
 }
}
