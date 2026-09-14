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
  readonly Border shell=new Border(); readonly Forms.NotifyIcon tray=new Forms.NotifyIcon();
  readonly DispatcherTimer timer=new DispatcherTimer();
  RpcClient rpc; Usage usage; string problem="Connecting",theme="system",authFingerprint; bool busy,closing,exitRequested,smoke;
  int failures; DateTime nextRead=DateTime.MinValue; DateTime? desktopGone; bool desktopSeen;
  public Widget(bool smokeTest) {
   smoke=smokeTest; Title="Codex Usage Monitor"; Width=280; Height=52; ResizeMode=ResizeMode.NoResize;
   WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
   Topmost=true; ShowInTaskbar=true; ShowActivated=false;
   WindowStartupLocation=WindowStartupLocation.CenterScreen;
   shell.CornerRadius=new CornerRadius(14); shell.BorderThickness=new Thickness(1); shell.Padding=new Thickness(15,0,12,0);
   var grid=new Grid(); foreach(var width in new[]{68.0,68.0,1.0,90.0})grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(width)});
   five.FontSize=20; five.FontWeight=FontWeights.SemiBold; week.FontSize=20; week.FontWeight=FontWeights.SemiBold;
   five.Text="—";week.Text="—";status.Text="Status —";status.FontSize=12;
   foreach(var text in new[]{five,week,status})text.VerticalAlignment=VerticalAlignment.Center;
   Grid.SetColumn(week,1);Grid.SetColumn(status,3);status.Margin=new Thickness(12,0,0,0);
   var divider=new Border{Width=1,Height=20,Background=new SolidColorBrush(Color.FromArgb(65,128,128,128))};Grid.SetColumn(divider,2);
   grid.Children.Add(five);grid.Children.Add(week);grid.Children.Add(divider);grid.Children.Add(status);shell.Child=grid;Content=shell;
   MouseLeftButtonDown+=(s,e)=>{if(e.ClickCount==2){Hide();return;}try{DragMove();Save();}catch{}};
   MouseRightButtonUp+=(s,e)=>{var menu=new ContextMenu();
    AddMenu(menu,"Refresh usage",()=>{nextRead=DateTime.MinValue;Refresh();});
    AddMenu(menu,"Always on top: "+(Topmost?"on":"off"),()=>{Topmost=!Topmost;Save();});
    AddMenu(menu,"Theme: System",()=>SetTheme("system"));AddMenu(menu,"Theme: Dark",()=>SetTheme("dark"));AddMenu(menu,"Theme: Light",()=>SetTheme("light"));
    AddMenu(menu,"Center widget",Center);AddMenu(menu,"Minimize to tray",Hide);AddMenu(menu,"Exit",Quit);menu.IsOpen=true;};
   tray.Icon=System.Drawing.SystemIcons.Information;tray.Text="Codex Usage Monitor";
   var context=new Forms.ContextMenuStrip();context.Items.Add("Show widget",null,(s,e)=>Dispatcher.Invoke(new Action(()=>Show())));
   context.Items.Add("Refresh usage",null,(s,e)=>Dispatcher.Invoke(new Action(()=>{nextRead=DateTime.MinValue;Refresh();})));
   context.Items.Add("Exit",null,(s,e)=>Dispatcher.Invoke(new Action(Quit)));tray.ContextMenuStrip=context;
   tray.DoubleClick+=(s,e)=>Dispatcher.Invoke(new Action(()=>Show()));tray.Visible=!smoke;
   LoadSettings();SetTheme(theme);
   Loaded+=(s,e)=>{desktopSeen=DesktopPresent();Refresh();};
   timer.Interval=TimeSpan.FromSeconds(2);timer.Tick+=(s,e)=>Tick();timer.Start();
   Closing+=(s,e)=>{if(!exitRequested){e.Cancel=true;Hide();return;}closing=true;timer.Stop();Save();tray.Dispose();if(rpc!=null)rpc.Dispose();};
  }
  void AddMenu(ContextMenu menu,string name,Action action){var item=new MenuItem{Header=name};item.Click+=(s,e)=>action();menu.Items.Add(item);}
  void Center(){Left=SystemParameters.WorkArea.Left+(SystemParameters.WorkArea.Width-Width)/2;Top=SystemParameters.WorkArea.Top+(SystemParameters.WorkArea.Height-Height)/2;Save();}
  void SetTheme(string value){theme=value;bool dark=value=="dark";
   if(value=="system")try{dark=Convert.ToInt32(Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1))==0;}catch{}
   shell.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#202226":"#FAFAFB"));
   shell.BorderBrush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#44474D":"#D3D5DA"));
   five.Foreground=week.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#F4F5F7":"#202226"));
   status.Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#A3A8B3":"#646B77"));
   Save();
  }
  void Save(){try{Directory.CreateDirectory(store);string path=Path.Combine(store,"settings.json");
   var data=Json.Write(new {theme=theme,topmost=Topmost,left=Left,top=Top});File.WriteAllText(path+".tmp",data);
   if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);
  }catch{}}
  void LoadSettings(){try{var data=Json.Parse(File.ReadAllText(Path.Combine(store,"settings.json")));theme=Json.Text(data,"theme");if(theme!="light"&&theme!="dark")theme="system";
   if(Json.Get(data,"topmost") is bool)Topmost=(bool)data["topmost"];
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
   }catch(Exception e){problem=e is TimeoutException?e.Message:"Usage unavailable — check Codex sign-in or connection.";
    // Keep a same-session snapshot visibly stale; never infer a new quota.
    failures++;nextRead=DateTime.UtcNow.AddSeconds(Math.Min(300,30*Math.Pow(2,Math.Min(failures-1,4))));if(rpc!=null){rpc.Dispose();rpc=null;}
   }finally{busy=false;}
   if(!closing){UpdateText();if(smoke){await Task.Delay(200);CaptureSmoke();Quit();}}
  }
  void UpdateText(){five.Text=Usage.Percent(usage==null?null:usage.Five);week.Text=Usage.Percent(usage==null?null:usage.Week);
   bool stale=usage!=null&&(problem!=null||DateTime.UtcNow-usage.Updated>TimeSpan.FromSeconds(120));
   status.Text=problem!=null?(usage==null?"Offline":"Stale"):"Status —";
   if(stale)status.Text="Stale";
   five.ToolTip="5-hour usage remaining";week.ToolTip="Weekly usage remaining";
   string info=usage==null?"No successful usage read yet.":"5-hour remaining: "+five.Text+"\nWeekly remaining: "+week.Text+"\nUpdated: "+usage.Updated.ToLocalTime().ToString("T")+"\n"+ResetText("5-hour",usage.FiveReset)+ResetText("Weekly",usage.WeekReset);
   shell.ToolTip=info+(problem!=null?"\n"+problem:"")+"\nTask status is not connected in this phase.\nRight-click for options; double-click to hide.";
   tray.Text=("Codex: "+five.Text+" / "+week.Text+(stale?" (stale)":"")).Substring(0,Math.Min(63,("Codex: "+five.Text+" / "+week.Text+(stale?" (stale)":"")).Length));
  }
  string ResetText(string label,long? seconds){if(!seconds.HasValue)return "";try{return label+" reset: "+new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(seconds.Value).ToLocalTime().ToString("g")+"\n";}catch{return "";}}
  void CaptureSmoke(){try{string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","probe","results");Directory.CreateDirectory(folder);
   var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(folder,"widget.png")))encoder.Save(file);
   File.WriteAllText(Path.Combine(folder,"widget-smoke.json"),Json.Write(new {success=usage!=null&&problem==null,five=usage==null?null:usage.Five,weekly=usage==null?null:usage.Week,accountFingerprint=usage==null?null:Json.Hash(usage.Account),problem=problem,workingSet=Process.GetCurrentProcess().WorkingSet64}));
  }catch{}}
  void Quit(){exitRequested=true;Close();}
 }
 public static class Program {
  [STAThread] public static void Main(string[] args){bool created;using(var mutex=new Mutex(true,"Local\\CodexUsageMonitor.Widget",out created)){
   if(!created)return;
   try{var app=new Application();app.Run(new Widget(args.Contains("--smoke")));}
   catch(Exception e){Forms.MessageBox.Show("Codex Usage Monitor could not start: "+e.Message,"Codex Usage Monitor");}
  }}
 }
}
