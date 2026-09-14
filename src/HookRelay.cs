using System;
using System.IO;
using System.Collections.Generic;
namespace CodexMonitor {
 public static class HookRelay {
  public static int Main(string[] args) {
   // Separate short-lived executable: only an allowlist of metadata reaches disk.
   // No approval decision, instruction injection, or prompt/tool content is returned.
   try {
    var input=Json.Parse(Console.In.ReadToEnd());
    string root=Path.GetFullPath(args.Length>0?args[0]:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageMonitor","hook-events"));
    Directory.CreateDirectory(root);
    var output=new Dictionary<string,object>();
    foreach(var k in new[]{"hook_event_name","session_id","turn_id","agent_id"})
      if(Json.Get(input,k)!=null)output[k]=Json.Get(input,k);
    output["receivedAt"]=DateTime.UtcNow.ToString("o");
    File.WriteAllText(Path.Combine(root,Guid.NewGuid().ToString("N")+".json"),Json.Write(output));
   } catch{} // A monitor outage must not block Codex.
   Console.Write("{}"); return 0;
  }
 }
}
