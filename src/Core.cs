using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexMonitor {
 public static class Json {
  public static Dictionary<string,object> Parse(string s) { return new JavaScriptSerializer { MaxJsonLength=16*1024*1024 }.Deserialize<Dictionary<string,object>>(s); }
  public static string Write(object o) { return new JavaScriptSerializer().Serialize(o); }
  public static Dictionary<string,object> Obj(object o) { return o as Dictionary<string,object> ?? new Dictionary<string,object>(); }
  public static object Get(Dictionary<string,object> o,string k) { object v; return o.TryGetValue(k,out v)?v:null; }
  public static string Text(Dictionary<string,object> o,string k) { return Convert.ToString(Get(o,k)); }
  public static string Hash(string s) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-","").ToLowerInvariant().Substring(0,16); }
 }
 public sealed class Usage {
  public double? Five, Week; public long? FiveReset, WeekReset; public string Account; public DateTime Updated;
  public static Usage From(Dictionary<string,object> result) {
   var all=Json.Obj(Json.Get(result,"rateLimitsByLimitId"));
   var bucket=Json.Obj(Json.Get(all,"codex"));
   if(bucket.Count==0) { var legacy=Json.Obj(Json.Get(result,"rateLimits")); if(Json.Text(legacy,"limitId")=="codex" || Json.Text(legacy,"limitId")=="") bucket=legacy; }
   var u=new Usage { Updated=DateTime.UtcNow,Account=Json.Text(result,"accountId") };
   foreach(var key in new[]{"primary","secondary"}) {
    var w=Json.Obj(Json.Get(bucket,key)); double used; int duration; long reset;
    if(!double.TryParse(Convert.ToString(Json.Get(w,"usedPercent"),System.Globalization.CultureInfo.InvariantCulture),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out used) || double.IsNaN(used) || double.IsInfinity(used)) continue;
    if(!int.TryParse(Json.Text(w,"windowDurationMins"),out duration)) continue;
    double remaining=Math.Max(0,Math.Min(100,100-used));
    long? resets=long.TryParse(Json.Text(w,"resetsAt"),out reset)?(long?)reset:null;
    if(duration==300) {u.Five=remaining;u.FiveReset=resets;}
    if(duration==10080) {u.Week=remaining;u.WeekReset=resets;}
   }
   return u;
  }
  public static string Percent(double? n) { return n.HasValue?n.Value.ToString("0.#")+"%":"—"; }
 }
 public sealed class RpcClient : IDisposable {
  Process p; StreamWriter input; int next; bool disposed; readonly object gate=new object();
  readonly Dictionary<int,TaskCompletionSource<Dictionary<string,object>>> pending=new Dictionary<int,TaskCompletionSource<Dictionary<string,object>>>();
  public static string FindBinary() {
   var explicitPath=Environment.GetEnvironmentVariable("CODEX_MONITOR_BINARY");
   if(!String.IsNullOrEmpty(explicitPath)&&File.Exists(explicitPath)) return explicitPath;
   foreach(var proc in Process.GetProcessesByName("codex")) {
    using(proc) { try {var path=proc.MainModule.FileName; if(path.IndexOf(@"\OpenAI\Codex\bin\",StringComparison.OrdinalIgnoreCase)>=0) return path;} catch{} }
   }
   string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"OpenAI\Codex\bin");
   if(Directory.Exists(root)) {var match=Directory.GetDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc).Select(d=>Path.Combine(d,"codex.exe")).FirstOrDefault(File.Exists); if(match!=null)return match;}
   throw new IOException("Codex backend not found. Open Codex or set CODEX_MONITOR_BINARY.");
  }
  public async Task Start() {
   p=new Process { StartInfo=new ProcessStartInfo(FindBinary(),"app-server --listen stdio://") {
    UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
    StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 } };
   p.Start(); input=p.StandardInput; input.AutoFlush=true;
   Pump(); DrainErrors();
   await Call("initialize",new {clientInfo=new {name="codex_usage_monitor",version="0.1.0"}});
   Send(new {method="initialized"});
  }
  async void DrainErrors() {try {while(await p.StandardError.ReadLineAsync()!=null){} } catch{} }
  async void Pump() {
   try {
    string line;
    while((line=await p.StandardOutput.ReadLineAsync())!=null) {
     var m=Json.Parse(line); int id;
     // Observer never responds to server requests or controls a task.
     if(Json.Get(m,"method")!=null || !int.TryParse(Json.Text(m,"id"),out id)) continue;
     TaskCompletionSource<Dictionary<string,object>> waiter=null;
     lock(gate) {if(pending.TryGetValue(id,out waiter))pending.Remove(id);}
     if(waiter!=null) {
      if(Json.Get(m,"error")!=null) waiter.TrySetException(new IOException("Codex rejected the usage request. Check sign-in and backend compatibility."));
      else waiter.TrySetResult(Json.Obj(Json.Get(m,"result")));
     }
    }
   } catch{} finally {lock(gate){foreach(var t in pending.Values)t.TrySetException(new IOException("Codex connection closed."));pending.Clear();}}
  }
  void Send(object o) {lock(gate){if(disposed)throw new ObjectDisposedException("RpcClient");input.WriteLine(Json.Write(o));}}
  public async Task<Dictionary<string,object>> Call(string method,object parameters) {
   int id=Interlocked.Increment(ref next); var t=new TaskCompletionSource<Dictionary<string,object>>();
   lock(gate){pending[id]=t;}
   try {
    Send(new Dictionary<string,object>{{"id",id},{"method",method},{"params",parameters??new {}}});
    if(await Task.WhenAny(t.Task,Task.Delay(20000))!=t.Task)throw new TimeoutException("Codex did not respond within 20 seconds.");
    return await t.Task;
   } finally {lock(gate)pending.Remove(id);}
  }
  public void Dispose() {
   lock(gate){disposed=true;foreach(var t in pending.Values)t.TrySetCanceled();pending.Clear();}
   if(p!=null){try{if(!p.HasExited)p.Kill();}catch{} p.Dispose();p=null;}
  }
 }
}
