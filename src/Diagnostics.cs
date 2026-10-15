using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MyDownload {
    // Command-line hooks used by the automated tests (tests\test_downloads.py) and for
    // measuring real downloads without opening the window.
    static class Diagnostics {
        // MyDownload.exe --download-test <url> <folder> [ms | bytes:N] [sparse:off] [connections:N]
        //                [split:N] [limit:N] [joblimit:N] [name:file] [threshold:N] [profile:real]
        public static int RunDownloadTest(string[] args) {
            string root=Path.GetFullPath(args[2]);Directory.CreateDirectory(root);
            string work=Path.Combine(root,".mydownload"),meta=Path.Combine(work,"test","job.json");
            Job job=File.Exists(meta)?Store.Read<Job>(meta):new Job{Id="test",Url=args[1],Folder=root};
            long stopAt=-1;int stopMs=-1;bool real=false;
            using(var engine=new Engine(job,work)) {
                engine.TimeoutMs=3000;engine.RetryMs=50;engine.ParallelThreshold=1024*1024;engine.Connections=4;engine.CheckpointMs=200;
                foreach(string option in args.Skip(3)) {
                    string value=option.Contains(':')?option.Substring(option.IndexOf(':')+1):option;
                    if(option.StartsWith("bytes:"))stopAt=Int64.Parse(value);
                    else if(option=="sparse:off")engine.AllowSparse=false;
                    else if(option.StartsWith("connections:"))engine.Connections=Int32.Parse(value);
                    else if(option.StartsWith("split:"))engine.MinSplit=Int64.Parse(value);
                    else if(option.StartsWith("limit:"))RateLimiter.BytesPerSecond=Int64.Parse(value);
                    else if(option.StartsWith("joblimit:"))job.SpeedLimit=Int64.Parse(value);
                    else if(option.StartsWith("name:"))job.Name=value;
                    else if(option.StartsWith("threshold:"))engine.ParallelThreshold=Int64.Parse(value);
                    else if(option=="profile:real"){real=true;engine.TimeoutMs=20000;engine.RetryMs=2000;engine.ParallelThreshold=16*1024*1024;engine.CheckpointMs=2000;engine.MinSplit=2*1024*1024;}
                    else stopMs=Int32.Parse(value);
                }
                var clock=Stopwatch.StartNew();
                var task=Task.Factory.StartNew(()=>engine.Run(),TaskCreationOptions.LongRunning);
                using(var progress=new StreamWriter(Path.Combine(root,"progress.log"),true)) {
                    long last=engine.Downloaded;double lastTime=0;
                    while(!task.Wait(real?1000:20)) {
                        double now=clock.Elapsed.TotalSeconds;
                        if(real||now-lastTime>=1) {
                            long bytes=engine.Downloaded;
                            progress.WriteLine(String.Format(CultureInfo.InvariantCulture,"{0:0.0}s phase={1} bytes={2} rate={3:0.00}MB/s connections={4}",now,engine.Phase,bytes,(bytes-last)/Math.Max(.001,now-lastTime)/1e6,engine.ActiveConnections));
                            progress.Flush();last=bytes;lastTime=now;
                        }
                        if(stopAt>=0&&(engine.Downloaded>=stopAt||now>(real?600:60))){engine.Stop.Cancel();break;}
                        if(stopMs>=0&&clock.ElapsedMilliseconds>=stopMs){engine.Stop.Cancel();break;}
                    }
                    task.Wait();
                    progress.WriteLine(String.Format(CultureInfo.InvariantCulture,"{0:0.0}s end state={1} bytes={2} detail={3}",clock.Elapsed.TotalSeconds,job.State,engine.Downloaded,job.Detail));
                }
                Store.Save(Path.Combine(root,"result.json"),job.Snapshot());
                return job.State=="Completed"?0:job.State=="Paused"?2:1;
            }
        }
    }
}
