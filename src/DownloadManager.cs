using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualBasic.FileIO;

namespace MyDownload {
    [DataContract] public sealed class HistoryEntry {
        [DataMember] public string Checkpoint;
        [DataMember] public Job Download;
        [DataMember] public long Bytes;
    }
    [DataContract] public sealed class HistoryDocument {
        [DataMember] public int Version=3;
        [DataMember] public string Updated;
        [DataMember] public List<HistoryEntry> Downloads=new List<HistoryEntry>();
    }

    public static class AppPaths {
        public const string Name="MyDownload", WorkFolderName=".mydownload";
        static string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Name);
        public static string Root { get { return root; } set { root=Path.GetFullPath(value); } }
        public static bool Custom;
        public static string History { get { return Path.Combine(root,"history.json"); } }
        public static string SettingsFile { get { return Path.Combine(root,"settings.json"); } }
        public static string Downloads { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"); } }
    }

    [DataContract] public sealed class Settings {
        [DataMember] public string Folder;
        [DataMember] public bool AskFolder;     // ask where to save each new download
        [DataMember] public List<string> RecentFolders;
        [DataMember] public int MaxActive;
        [DataMember] public int Connections;
        [DataMember] public long SpeedLimit;
        [DataMember] public bool Organize;
        [DataMember] public bool WatchClipboard;
        [DataMember] public bool Notify;
        [DataMember] public bool CloseToTray;
        [DataMember] public bool HashOnComplete;
        [DataMember] public string Theme;
        [DataMember] public string Sort;
        [DataMember] public bool TrayHintShown;
        [DataMember] public int[] Window;
        [DataMember] public int[] Mini;
        public static readonly int[] ConnectionChoices={1,2,4,8,16};
        public Settings(){Defaults();}
        [OnDeserializing] void OnDeserializing(StreamingContext context){Defaults();}
        void Defaults() {
            Folder=AppPaths.Downloads;AskFolder=true;RecentFolders=new List<string>();MaxActive=3;Connections=8;SpeedLimit=0;
            Organize=false;WatchClipboard=true;Notify=true;CloseToTray=true;HashOnComplete=false;Theme="dark";Sort="recent";
        }
        void Clamp() {
            if(String.IsNullOrWhiteSpace(Folder)||!Path.IsPathRooted(Folder))Folder=AppPaths.Downloads;
            if(RecentFolders==null)RecentFolders=new List<string>();
            MaxActive=Math.Max(1,Math.Min(8,MaxActive));
            if(Array.IndexOf(ConnectionChoices,Connections)<0)Connections=8;
            SpeedLimit=Math.Max(0,SpeedLimit);
            if(Theme!="light"&&Theme!="dark"&&Theme!="system")Theme="dark";
            if(Sort!="recent"&&Sort!="name"&&Sort!="size")Sort="recent";
        }
        public static Settings Load(string path) {
            Settings settings=null;
            try{if(File.Exists(path))settings=Store.Read<Settings>(path);}
            catch(Exception){try{File.Copy(path,path+".backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"),true);}catch(IOException){}}
            if(settings==null)settings=new Settings();
            settings.Clamp();return settings;
        }
        public void Save(string path){try{Clamp();Store.Save(path,this);}catch(IOException){}catch(UnauthorizedAccessException){}}
        public void Remember(string folder) {
            RecentFolders.RemoveAll(f=>String.Equals(f,folder,StringComparison.OrdinalIgnoreCase));
            RecentFolders.Insert(0,folder);if(RecentFolders.Count>6)RecentFolders.RemoveRange(6,RecentFolders.Count-6);
        }
    }

    public sealed class DownloadItem {
        public readonly Job Job;
        public Engine Engine;
        public Task Task;
        public long Bytes;
        public double Rate;
        public long PreviewBytes=-1;
        public string PreviewPhase;
        public double PreviewRate;
        public Task<string> HashTask;
        public CancellationTokenSource HashCancel;
        public long HashDone;
        public bool Removing, RecycleOnRemove, Replacing, Restart;
        long lastBytes;
        DateTime lastTick=DateTime.UtcNow;
        public DownloadItem(Job job) { Job=job;Bytes=job.Saved;lastBytes=Bytes; }
        public bool Busy {get{return Task!=null&&!Task.IsCompleted;}}
        public string Phase {get{return PreviewPhase??(Engine!=null?Engine.Phase:Job.State);}}
        public string Detail {get{return Engine!=null?Engine.Detail:Job.Detail;}}
        public string Checkpoint {get{return Path.Combine(Job.WorkFolder??Path.Combine(Job.Folder,AppPaths.WorkFolderName,Job.Id),"job.json");}}
        public bool Active {get{return Busy || PreviewPhase=="Downloading" || PreviewPhase=="Finalizing";}}
        public bool Hashing {get{return HashTask!=null&&!HashTask.IsCompleted;}}
        public int Connections {get{return Engine!=null?Engine.ActiveConnections:0;}}
        public void Update() {
            if(PreviewBytes>=0){Bytes=PreviewBytes;Rate=PreviewRate;return;}
            if(Engine==null)return;
            long bytes=Engine.Downloaded;var now=DateTime.UtcNow;
            double instant=Math.Max(0,bytes-lastBytes)/Math.Max(.01,(now-lastTick).TotalSeconds);
            Rate=Busy?(Rate==0?instant:Rate*.7+instant*.3):0;
            Bytes=bytes;lastBytes=bytes;lastTick=now;
        }
        public double Fraction {get{return Job.Total>0?Math.Min(1,(double)Bytes/Job.Total):Phase=="Completed"?1:0;}}
        public string Host {get{Uri u;return Uri.TryCreate(Job.Url,UriKind.Absolute,out u)?u.Host:"Arquivo local";}}
        public string Title {get{
            if(Job.Filename!=null)return Job.Filename;
            Uri u;if(Uri.TryCreate(Job.Url,UriKind.Absolute,out u)){string last=Uri.UnescapeDataString(u.AbsolutePath.Split('/').Last());if(last.Length>0)return last;}
            return "Consultando arquivo…";
        }}
        public DateTime When {get{DateTime d;return DateTime.TryParse(Job.Completed??Job.Created,out d)?d:DateTime.MinValue;}}
    }

    public sealed class CoordinatorEvent {
        public readonly DownloadItem Item;public readonly string Kind;
        public CoordinatorEvent(DownloadItem item,string kind){Item=item;Kind=kind;}
    }

    public sealed class DownloadCoordinator : IDisposable {
        public readonly List<DownloadItem> Items=new List<DownloadItem>();
        public readonly string HistoryPath;
        public readonly Settings Settings;
        public string Warning;
        DateTime saved=DateTime.MinValue;
        readonly bool preview;
        bool shuttingDown;
        public DownloadCoordinator(string dataRoot,Settings settings,bool isPreview=false) {
            Settings=settings??new Settings();HistoryPath=Path.Combine(dataRoot,"history.json");preview=isPreview;
            if(!preview){RateLimiter.BytesPerSecond=Settings.SpeedLimit;Load();}
        }
        public IEnumerable<DownloadItem> Visible {get{return Items.Where(i=>!i.Removing);}}
        void Load() {
            if(!File.Exists(HistoryPath))return;
            try {
                var doc=Store.Read<HistoryDocument>(HistoryPath);
                if(doc==null||doc.Version!=3||doc.Downloads==null)throw new IOException("Unknown history format.");
                foreach(var entry in doc.Downloads)AddLoaded(entry.Checkpoint,entry.Download,entry.Bytes);
            } catch(Exception) { Warning="Não foi possível ler o histórico. O arquivo original foi guardado como cópia.";try{File.Copy(HistoryPath,HistoryPath+".backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"),false);}catch(IOException){} }
        }
        void AddLoaded(string path,Job fallback,long recordedBytes) {
            try {
                Job job=!String.IsNullOrEmpty(path)&&File.Exists(path)?Store.Read<Job>(path):fallback;
                if(job==null||String.IsNullOrWhiteSpace(job.Id)||String.IsNullOrWhiteSpace(job.Folder))return;
                if(Items.Any(i=>i.Job.Id==job.Id))return;
                if(fallback!=null)job.UserPaused=fallback.UserPaused;
                if(String.IsNullOrEmpty(job.WorkFolder)&&!String.IsNullOrEmpty(path)&&File.Exists(path))job.WorkFolder=Path.GetDirectoryName(path);
                Reconcile(job,recordedBytes);
                Items.Add(new DownloadItem(job));
            } catch(Exception){Warning="Um registro não pôde ser lido. Os demais downloads foram preservados.";}
        }
        // The history describes intent; the destination and saved data describe reality.
        static void Reconcile(Job job,long recordedBytes) {
            string drive=Path.GetPathRoot(job.Folder);
            if(String.IsNullOrEmpty(drive)||!Directory.Exists(drive)) {job.State="Error";job.Detail="A unidade de destino está indisponível. Conecte-a e clique em retomar.";return;}
            bool outputExists=!String.IsNullOrEmpty(job.Filename)&&File.Exists(job.Output);
            if(outputExists && job.State=="Completed" && new FileInfo(job.Output).Length==job.Total) return;
            bool wasComplete=job.State=="Completed";
            string work=job.WorkFolder??Path.Combine(job.Folder,AppPaths.WorkFolderName,job.Id);
            bool hasData=job.Starts!=null&&File.Exists(Path.Combine(work,"data.part"));
            if(!hasData && !outputExists) {
                job.ResetTransfer();
                if(wasComplete||recordedBytes>0){job.UserPaused=true;job.State="Missing";job.Detail="O arquivo não está mais no destino. Clique em baixar novamente.";}
            }
            if(wasComplete && outputExists) {
                // Preserve the altered file; ReserveOutput will choose a new name.
                job.ResetTransfer();job.UserPaused=true;job.State="Changed";job.Detail="O arquivo foi alterado no destino. Baixe novamente para salvar outra cópia.";
            } else if(wasComplete) {job.State="Missing";job.UserPaused=true;}
            if(job.State=="Downloading"||job.State=="Connecting"||job.State=="Finalizing"||job.State=="Queued")job.State="Paused";
        }
        public void RestoreDownloads() {
            foreach(var item in Items.Where(i=>!i.Job.UserPaused && i.Job.State!="Completed" && i.Job.State!="Error").ToList()) {
                try{Start(item);}catch(Exception e){item.Job.State="Error";item.Job.Detail=e.Message;}
            }
            SaveHistory();
        }
        // setup fills the per-download options (name, referrer, after done, connections, limit).
        public DownloadItem Add(string url,string folder,bool start=true,Action<Job> setup=null) {
            string link=Engine.ValidateUrl(url).AbsoluteUri;
            if(String.IsNullOrWhiteSpace(folder)||!Path.IsPathRooted(folder.Trim()))throw new DownloadError("Escolha uma pasta de destino válida.");
            folder=Path.GetFullPath(folder.Trim());
            var old=Visible.FirstOrDefault(i=>i.Job.Url==link&&String.Equals(i.Job.Folder,folder,StringComparison.OrdinalIgnoreCase)&&i.Phase!="Completed");
            if(old!=null){if(setup!=null)setup(old.Job);if(start)Start(old);return old;}
            Directory.CreateDirectory(folder);
            var job=new Job {Id=Guid.NewGuid().ToString("N"),Url=link,Folder=folder,Organize=Settings.Organize};
            if(setup!=null)setup(job);
            job.WorkFolder=Path.Combine(folder,AppPaths.WorkFolderName,job.Id);
            var item=new DownloadItem(job);
            Store.Save(item.Checkpoint,job);Items.Add(item);SaveHistory();if(start)Start(item);return item;
        }
        public void Start(DownloadItem item) {
            if(item.Busy||item.Removing||item.Replacing||item.Job.State=="Completed")return;
            Finish(item);
            item.Job.UserPaused=false;
            if(shuttingDown)return;
            if(ActiveCount>=Settings.MaxActive){item.Job.State="Queued";item.Job.Detail="Na fila. Começa assim que houver uma vaga.";SaveJob(item);SaveHistory();return;}
            item.Engine=new Engine(item.Job,Path.Combine(item.Job.Folder,AppPaths.WorkFolderName)){Connections=item.Job.Connections>0?item.Job.Connections:Settings.Connections};
            item.Job.State="Connecting";item.Job.Detail="Conectando ao servidor…";
            var engine=item.Engine;item.Task=Task.Factory.StartNew(()=>engine.Run(),CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);SaveHistory();
        }
        // Saves changed options; a running download with new connections restarts from where it is.
        public void ApplyOptions(DownloadItem item,bool connectionsChanged) {
            SaveJob(item);SaveHistory();
            if(connectionsChanged&&item.Busy){item.Restart=true;item.Engine.RequestPause();}
        }
        public void Pause(DownloadItem item) {item.Job.UserPaused=true;if(item.Busy)item.Engine.RequestPause();else if(item.Phase=="Queued"){item.Job.State="Paused";item.Job.Detail="Pausado.";SaveJob(item);}SaveHistory();}
        public void PauseAll(bool explicitPause=true) {
            if(!explicitPause)shuttingDown=true;
            foreach(var item in Items.Where(i=>i.Busy||i.Phase=="Queued")){
                if(explicitPause)item.Job.UserPaused=true;
                if(item.Busy)item.Engine.RequestPause();else {item.Job.State="Paused";SaveJob(item);}
            }SaveHistory();
        }
        public void ResumeAll() {foreach(var item in Visible.Where(i=>i.Job.State!="Completed"&&i.Job.State!="Missing"&&i.Job.State!="Changed").ToList())Start(item);}
        public int ActiveCount {get{return Items.Count(i=>i.Active);}}
        public int QueuedCount {get{return Visible.Count(i=>i.Phase=="Queued");}}
        public double TotalRate {get{return Items.Where(i=>i.Active).Sum(i=>i.Rate);}}
        void SaveJob(DownloadItem item) {
            if(preview||item.Job.State=="Completed")return;
            try{Store.Save(item.Checkpoint,item.Job.Snapshot());}catch(IOException e){Warning=e.Message;}catch(UnauthorizedAccessException e){Warning=e.Message;}
        }
        CoordinatorEvent Finish(DownloadItem item) {
            if(item.Task==null||!item.Task.IsCompleted)return null;
            item.Update();
            if(item.Task.IsFaulted){item.Job.State="Error";item.Job.Detail=item.Task.Exception.GetBaseException().Message;}
            else {item.Job.State=item.Engine.Phase;item.Job.Detail=item.Engine.Detail;}
            SaveJob(item);
            item.Engine.Dispose();item.Engine=null;item.Task=null;item.Rate=0;
            if(item.Removing)return null;
            if(item.Job.State=="Completed") {
                item.Bytes=Math.Max(0,item.Job.Total);DeleteWork(item.Job);SaveHistory();
                if(Settings.HashOnComplete||item.Job.ExpectedSha256!=null)ComputeHash(item);
                return new CoordinatorEvent(item,"Completed");
            }
            return item.Job.State=="Error"?new CoordinatorEvent(item,"Failed"):null;
        }
        public List<CoordinatorEvent> Tick() {
            var events=new List<CoordinatorEvent>();
            foreach(var item in Items.ToList()) {
                item.Update();
                var e=Finish(item);if(e!=null)events.Add(e);
                if(item.HashTask!=null&&item.HashTask.IsCompleted){e=HashFinished(item);if(e!=null)events.Add(e);}
                if(item.Removing&&!item.Busy&&item.HashTask==null)RemoveNow(item);
                else if(item.Restart&&!item.Busy){item.Restart=false;Start(item);}
            }
            if(!shuttingDown)foreach(var item in Visible.Where(i=>i.Phase=="Queued"&&!i.Job.UserPaused).ToList()){if(ActiveCount>=Settings.MaxActive)break;Start(item);}
            if((DateTime.UtcNow-saved).TotalSeconds>=2)SaveHistory();
            return events;
        }

        // ---- removing ----------------------------------------------------------------------
        public void Remove(DownloadItem item,bool recycle) {
            if(item.Removing)return;
            item.Removing=true;item.RecycleOnRemove=recycle;
            if(item.HashCancel!=null)item.HashCancel.Cancel();
            if(item.Busy){item.Job.UserPaused=true;item.Engine.RequestPause();return;}
            if(item.HashTask==null)RemoveNow(item);
        }
        public int ClearCompleted() {
            var done=Visible.Where(i=>i.Phase=="Completed").ToList();
            foreach(var item in done)Remove(item,false);
            return done.Count;
        }
        void RemoveNow(DownloadItem item) {
            Items.Remove(item);
            if(!preview) {
                DeleteWork(item.Job);
                if(item.RecycleOnRemove&&item.Job.State=="Completed"&&item.Job.Filename!=null&&File.Exists(item.Job.Output)) {
                    try{FileSystem.DeleteFile(item.Job.Output,UIOption.OnlyErrorDialogs,RecycleOption.SendToRecycleBin);}
                    catch(Exception e){Warning="Não foi possível mover o arquivo para a Lixeira: "+e.Message;}
                }
            }
            SaveHistory();
        }
        // Deletes only the files this app creates, and only inside its own work folders.
        public static void DeleteWork(Job job) {
            string work=job.WorkFolder;
            if(String.IsNullOrEmpty(work)||!Directory.Exists(work))return;
            string parent=Path.GetDirectoryName(work);
            if(!String.Equals(Path.GetFileName(parent),AppPaths.WorkFolderName,StringComparison.OrdinalIgnoreCase))return;
            Engine.ReleaseReservations(work,job.Id);
            string[] ours={"job.json","job.json.tmp","download.log","download.lock","data.part"};
            foreach(string name in ours) {
                string file=Path.Combine(work,name);
                try{if(File.Exists(file))File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
            DeleteIfEmpty(work);DeleteIfEmpty(Path.Combine(parent,"reservations"));DeleteIfEmpty(parent);
        }
        static void DeleteIfEmpty(string folder) {
            try{if(Directory.Exists(folder)&&!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);}catch(IOException){}catch(UnauthorizedAccessException){}
        }

        // ---- replacing an expired link ---------------------------------------------------
        public Task<Engine.Replacement> CheckReplacement(DownloadItem item,string url) {
            if(item.Busy)throw new DownloadError("Pause o download antes de trocar o link.");
            Engine.ValidateUrl(url);
            item.Replacing=true;var job=item.Job;
            return Task.Run(()=>Engine.CheckReplacement(job,url,CancellationToken.None));
        }
        public void ApplyReplacement(DownloadItem item,Engine.Replacement replacement) {
            item.Replacing=false;
            lock(item.Job) {
                item.Job.Url=replacement.Url;
                if(item.Job.Starts!=null){item.Job.ETag=replacement.ETag;item.Job.Modified=replacement.Modified;}
            }
            item.Job.State="Paused";item.Job.Detail="Link atualizado. Continuando de onde parou.";
            SaveJob(item);SaveHistory();Start(item);
        }
        public void CancelReplacement(DownloadItem item){item.Replacing=false;}

        // ---- SHA-256 -----------------------------------------------------------------------
        public void ComputeHash(DownloadItem item) {
            if(item.Hashing||item.Job.State!="Completed"||item.Job.Filename==null||!File.Exists(item.Job.Output))return;
            var cancel=new CancellationTokenSource();string path=item.Job.Output;
            item.HashCancel=cancel;Interlocked.Exchange(ref item.HashDone,0);
            item.HashTask=Task.Factory.StartNew(()=>Engine.ComputeSha256(path,cancel.Token,done=>Interlocked.Exchange(ref item.HashDone,done)),cancel.Token,TaskCreationOptions.LongRunning,TaskScheduler.Default);
        }
        CoordinatorEvent HashFinished(DownloadItem item) {
            var task=item.HashTask;item.HashTask=null;
            if(item.HashCancel!=null){item.HashCancel.Dispose();item.HashCancel=null;}
            if(task.Status!=TaskStatus.RanToCompletion){if(task.IsFaulted)Warning="Não foi possível calcular o SHA-256: "+task.Exception.GetBaseException().Message;return null;}
            item.Job.Sha256=task.Result;
            if(item.Job.ExpectedSha256!=null)item.Job.HashCheck=item.Job.Sha256==item.Job.ExpectedSha256?"match":"mismatch";
            SaveHistory();
            return item.Job.HashCheck=="mismatch"?new CoordinatorEvent(item,"HashMismatch"):item.Job.HashCheck=="match"?new CoordinatorEvent(item,"HashMatch"):null;
        }
        public static string NormalizeHash(string value) {
            string hex=Regex.Replace(value??"",@"\s","").ToLowerInvariant();
            if(!Regex.IsMatch(hex,"^[0-9a-f]{64}$"))throw new DownloadError("Cole um SHA-256 com 64 caracteres (0 a 9 e a até f).");
            return hex;
        }
        public void SetExpectedHash(DownloadItem item,string value) {
            item.Job.ExpectedSha256=NormalizeHash(value);item.Job.HashCheck=null;
            if(item.Job.Sha256!=null)item.Job.HashCheck=item.Job.Sha256==item.Job.ExpectedSha256?"match":"mismatch";
            else if(item.Job.State=="Completed")ComputeHash(item);
            SaveJob(item);SaveHistory();
        }

        public void ApplySettings(){if(!preview)RateLimiter.BytesPerSecond=Settings.SpeedLimit;}
        public void SaveHistory() {
            if(preview)return;
            var doc=new HistoryDocument {Updated=DateTime.Now.ToString("o")};
            foreach(var item in Items)doc.Downloads.Add(new HistoryEntry {Checkpoint=item.Checkpoint,Download=item.Job.Snapshot(),Bytes=item.Bytes});
            try {Store.Save(HistoryPath,doc);saved=DateTime.UtcNow;}catch(IOException e){Warning="Não foi possível salvar o histórico: "+e.Message;}catch(UnauthorizedAccessException e){Warning=e.Message;}
        }
        public void Dispose() {
            PauseAll(false);
            foreach(var item in Items)if(item.HashCancel!=null)item.HashCancel.Cancel();
            var tasks=Items.Where(i=>i.Task!=null).Select(i=>i.Task).ToArray();
            try {Task.WaitAll(tasks,1000);}catch(AggregateException){}
            foreach(var item in Items)Finish(item);SaveHistory();
        }
    }
}
