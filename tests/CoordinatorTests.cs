using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Diagnostics;
using System.Threading;
using System.Security.Cryptography;
using MyDownload;
[assembly:System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]
class CoordinatorTests {
 static DownloadCoordinator manager;static string root,url;static Settings settings;
 static void Assert(bool ok,string why){if(!ok)throw new Exception(why);Console.WriteLine("PASS "+why);}
 static void Until(Func<bool> condition,int seconds=15){var watch=Stopwatch.StartNew();while(!condition()){manager.Tick();Thread.Sleep(30);if(watch.Elapsed.TotalSeconds>seconds)throw new Exception("Timed out: "+String.Join(", ",manager.Items.Select(x=>x.Phase+" "+x.Detail)));}manager.Tick();}
 static string Hash(string path){using(var h=SHA256.Create())using(var f=File.OpenRead(path))return BitConverter.ToString(h.ComputeHash(f));}
 static string Refused(Func<object> action){try{action();return null;}catch(AggregateException e){return e.GetBaseException().Message;}catch(DownloadError e){return e.Message;}}
 static int Main(string[] args){
  root=args[0];url=args[1];ServicePointManager.DefaultConnectionLimit=64;ServicePointManager.SecurityProtocol=SecurityProtocolType.SystemDefault;
  try{
   string data=Path.Combine(root,"data"),history=Path.Combine(root,"history");
   settings=new Settings{MaxActive=4,Connections=4};
   manager=new DownloadCoordinator(history,settings);
   var a=manager.Add(url+"/slow-a",data);var b=manager.Add(url+"/slow-b",data);var stalled=manager.Add(url+"/stall",data);
   Until(()=>a.Bytes>65536&&b.Bytes>65536&&stalled.Phase=="Downloading");
   Assert(a.Busy&&b.Busy,"two files transfer concurrently");
   Assert(a.Job.Starts!=null&&File.Exists(a.Job.DataPath),"new downloads write straight into one data.part");
   manager.Pause(a);Until(()=>!a.Busy);long saved=a.Bytes;
   Assert(saved>0&&saved<a.Job.Total,"partial file preserved on manual pause");
   var watch=Stopwatch.StartNew();manager.Pause(stalled);Until(()=>!stalled.Busy,6);
   Assert(watch.Elapsed.TotalSeconds<4,"cancel stalled body read in under four seconds");
   Until(()=>b.Phase=="Completed"&&!b.Busy,30);Assert(b.Job.Filename!=a.Job.Filename,"simultaneous identical names are reserved separately");
   Assert(!Directory.Exists(b.Job.WorkFolder),"work folder of a finished download is cleaned up");
   var d=manager.Add(url+"/slow-d",data);Until(()=>d.Bytes>65536);
   string aId=a.Job.Id,bId=b.Job.Id,dId=d.Job.Id,stalledId=stalled.Job.Id;
   string originalHash=Hash(b.Job.Output);File.Move(b.Job.Output,b.Job.Output+".saved");
   manager.Dispose();
   manager=new DownloadCoordinator(history,settings);manager.RestoreDownloads();
   a=manager.Items.Single(i=>i.Job.Id==aId);b=manager.Items.Single(i=>i.Job.Id==bId);d=manager.Items.Single(i=>i.Job.Id==dId);
   Assert(!a.Busy&&a.Job.UserPaused,"manual pause remains paused after reopen");
   Assert(b.Phase=="Missing"&&!b.Busy,"deleted final file waits for explicit redownload");
   Assert(d.Busy,"interrupted download resumes automatically on reopen");
   manager.Start(b);Until(()=>b.Phase=="Completed"&&d.Phase=="Completed",30);
   Assert(Hash(b.Job.Output)==originalHash,"redownload after deletion produces exact bytes");
   // A new link for a paused download is accepted only when it serves the same bytes.
   string diff=Refused(()=>manager.CheckReplacement(a,url+"/diff").Result);
   manager.CancelReplacement(a);
   Assert(diff!=null&&diff.Contains("diferente"),"link with different content is refused: "+diff);
   string smaller=Refused(()=>manager.CheckReplacement(a,url+"/short").Result);
   manager.CancelReplacement(a);
   Assert(smaller!=null&&smaller.Contains("tamanho"),"link with another size is refused: "+smaller);
   var replacement=manager.CheckReplacement(a,url+"/alt-a").Result;
   Assert(replacement.Samples>0&&replacement.ETag=="\"alt\"","matching link verified by comparing saved samples");
   manager.ApplyReplacement(a,replacement);Until(()=>a.Phase=="Completed",30);
   Assert(a.Job.Url.EndsWith("/alt-a")&&Hash(a.Job.Output)==originalHash,"resume through the new link produces exact SHA-256");
   var doc=Store.Read<HistoryDocument>(manager.HistoryPath);Assert(doc.Version==3&&doc.Downloads.Count==4,"JSON history stores all downloads");
   manager.SetExpectedHash(a,BitConverter.ToString(SHA256.Create().ComputeHash(File.ReadAllBytes(a.Job.Output))).Replace("-",""));
   Until(()=>a.Job.HashCheck!=null,15);Assert(a.Job.HashCheck=="match","pasted SHA-256 is compared with the file");
   manager.SaveHistory();manager.Dispose();manager=new DownloadCoordinator(history,settings);manager.RestoreDownloads();
   Assert(manager.Items.Count(i=>i.Phase=="Completed")==3&&manager.ActiveCount==0,"completed existing files are recognized without downloading");
   var q=Enumerable.Range(0,7).Select(i=>manager.Add(url+"/queue-"+i,data)).ToArray();
   Assert(manager.ActiveCount==4&&q.Count(i=>i.Phase=="Queued")==3,"four active files and bounded automatic queue");
   using(var release=new WebClient())release.DownloadString(url+"/release");
   Until(()=>q.All(i=>i.Phase=="Completed"),50);
   Assert(q.Select(i=>i.Job.Output).Distinct().Count()==7,"all queued colliding names finish independently");
   var kept=manager.Items.Single(i=>i.Job.Id==bId);string keptFile=kept.Job.Output;
   manager.Remove(kept,false);Until(()=>!manager.Items.Contains(kept));
   Assert(File.Exists(keptFile),"removing a finished download from the list keeps the file");
   stalled=manager.Items.Single(i=>i.Job.Id==stalledId);string stalledWork=stalled.Job.WorkFolder;
   manager.Remove(stalled,false);Until(()=>!manager.Items.Contains(stalled));
   Assert(!Directory.Exists(stalledWork),"removing an unfinished download deletes its partial data");
   manager.Dispose();
   manager=new DownloadCoordinator(history,settings);
   Assert(manager.Items.Count==9&&manager.Items.Any(i=>i.Job.Id==q[0].Job.Id&&i.Phase=="Completed"),"history alone restores finished downloads after cleanup");
   manager.Dispose();
   Console.WriteLine("ALL COORDINATOR TESTS PASSED");return 0;
  }catch(Exception e){Console.WriteLine(e);if(manager!=null)manager.Dispose();return 1;}
 }
}
