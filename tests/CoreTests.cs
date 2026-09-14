using System;
using CodexMonitor;
class Tests {
 static void Check(bool b,string name){if(!b)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Main(){
  var u=Usage.From(Json.Parse("{\"rateLimitsByLimitId\":{\"codex\":{\"primary\":{\"usedPercent\":22,\"windowDurationMins\":10080},\"secondary\":{\"usedPercent\":30,\"windowDurationMins\":300}},\"base_model_inference\":{\"primary\":{\"usedPercent\":0,\"windowDurationMins\":300}}}}"));
  Check(u.Five==70&&u.Week==78,"duration mapping and bucket isolation");
  u=Usage.From(Json.Parse("{\"rateLimits\":{\"limitId\":\"codex\",\"primary\":null}}"));Check(u.Five==null&&u.Week==null,"missing data is unknown");
  u=Usage.From(Json.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":125,\"windowDurationMins\":300},\"secondary\":{\"usedPercent\":-5,\"windowDurationMins\":10080}}}"));Check(u.Five==0&&u.Week==100,"bounds clamp");
  u=Usage.From(Json.Parse("{\"rateLimits\":{\"limitId\":\"other\",\"primary\":{\"usedPercent\":50,\"windowDurationMins\":300}}}"));Check(u.Five==null,"unrelated legacy bucket rejected");
  u=Usage.From(Json.Parse("{\"rateLimits\":{\"primary\":{\"usedPercent\":0,\"windowDurationMins\":15}}}"));Check(u.Five==null,"unexpected duration rejected");
  Check(Usage.Percent(null)=="—","unknown display");
 }
}
