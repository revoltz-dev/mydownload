using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace MyDownload {
    // Every connection writes straight into one data.part file (sparse when it has several
    // connections) and the finished file is only renamed into place.
    [DataContract]
    public sealed class Job {
        [DataMember] public string Id;
        [DataMember] public string Url;
        [DataMember] public string Folder;
        [DataMember] public string Filename;
        [DataMember] public long Total = -1;
        [DataMember] public bool Resume;
        [DataMember] public string ETag;
        [DataMember] public string Modified;
        [DataMember] public bool Sparse;
        [DataMember] public long[] Starts;
        [DataMember] public long[] Ends;
        [DataMember] public long[] Done;
        [DataMember] public string State = "New";
        [DataMember] public string Detail = "";
        [DataMember] public string Sha256;
        [DataMember] public string ExpectedSha256;
        [DataMember] public string HashCheck;
        [DataMember] public string Created = DateTime.Now.ToString("s");
        [DataMember] public string Completed;
        [DataMember] public bool UserPaused;
        [DataMember] public bool Organize;
        [DataMember] public string WorkFolder;
        // Per-download options.
        [DataMember] public string Name;        // file name chosen by the user instead of the server's
        [DataMember] public string Referrer;    // page that had the link (sent by the browser extension)
        [DataMember] public string AfterDone;   // null, "open", "folder", "shutdown" or "sleep"
        [DataMember] public int Connections;    // 0 = global setting
        [DataMember] public long SpeedLimit;    // bytes per second, 0 = no limit
        public string Output { get { return Path.Combine(Folder, Filename ?? "download"); } }
        public string DataPath { get { return WorkFolder==null?null:Path.Combine(WorkFolder,"data.part"); } }
        // Bytes confirmed on disk while no engine is running.
        public long Saved {
            get {
                if(State=="Completed")return Math.Max(0,Total);
                lock(this)return Done==null?0:Done.Sum();
            }
        }
        public Job Snapshot() {
            lock(this) {
                var copy=(Job)MemberwiseClone();
                if(Starts!=null)copy.Starts=(long[])Starts.Clone();if(Ends!=null)copy.Ends=(long[])Ends.Clone();if(Done!=null)copy.Done=(long[])Done.Clone();
                return copy;
            }
        }
        public void ResetTransfer() {
            lock(this){Starts=null;Ends=null;Done=null;Sparse=false;Sha256=null;HashCheck=null;Total=-1;ETag=null;Modified=null;Resume=false;Completed=null;}
        }
        public override string ToString() { return (Filename ?? new Uri(Url).Host) + "  ·  " + State; }
    }

    public static class Store {
        public static void Save<T>(string file, T value) {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temp = file + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                stream.Flush(true);
            }
            if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
        }
        public static T Read<T>(string file) {
            using (var stream = File.OpenRead(file)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }

    public sealed class DownloadError : Exception { public DownloadError(string message) : base(message) {} }

    public static class Units {
        public static string Size(double value) {
            string[] units={"B","KB","MB","GB","TB"};int i=0;
            while(value>=1000&&i<4){value/=1000;i++;}
            return value.ToString(i==0||value>=100?"0":"0.0",System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))+" "+units[i];
        }
        public static string Duration(double seconds) {
            if(seconds>=86400)return Math.Ceiling(seconds/3600)+" h";
            if(seconds>=3600){int h=(int)(seconds/3600),m=(int)Math.Ceiling((seconds-h*3600)/60);return m>=60?(h+1)+" h":h+" h "+m.ToString("00")+" min";}
            if(seconds>=60)return Math.Ceiling(seconds/60)+" min";
            return Math.Max(1,Math.Ceiling(seconds))+" s";
        }
    }

    public enum FileKind { Other, Archive, Disc, Program, Video, Music, Picture, Document }
    public static class Categories {
        static readonly Dictionary<string,FileKind> kinds=new Dictionary<string,FileKind>(StringComparer.OrdinalIgnoreCase);
        static Categories() {
            Map(FileKind.Archive,".zip",".rar",".7z",".gz",".tgz",".bz2",".xz",".tar",".zst",".cab");
            Map(FileKind.Disc,".iso",".img",".vhd",".vhdx",".dmg");
            Map(FileKind.Program,".exe",".msi",".msix",".appx",".appxbundle",".msixbundle",".apk",".bat",".jar");
            Map(FileKind.Video,".mp4",".mkv",".avi",".mov",".webm",".wmv",".flv",".m4v",".ts",".mpg",".mpeg");
            Map(FileKind.Music,".mp3",".wav",".flac",".ogg",".m4a",".aac",".wma",".opus");
            Map(FileKind.Picture,".png",".jpg",".jpeg",".webp",".gif",".bmp",".tif",".tiff",".heic",".svg",".psd");
            Map(FileKind.Document,".pdf",".doc",".docx",".xls",".xlsx",".ppt",".pptx",".txt",".csv",".epub",".odt",".rtf");
        }
        static void Map(FileKind kind,params string[] extensions){foreach(string e in extensions)kinds[e]=kind;}
        public static FileKind Of(string name) {
            FileKind kind;string ext=Path.GetExtension(name??"");
            return ext.Length>0&&kinds.TryGetValue(ext,out kind)?kind:FileKind.Other;
        }
        // Subfolder used when "organize by type" is on. Compressed files, disc images and unknown
        // types stay in the chosen folder.
        public static string FolderFor(string name) {
            switch(Of(name)){
                case FileKind.Program:return "Programs";
                case FileKind.Video:return "Videos";
                case FileKind.Music:return "Music";
                case FileKind.Picture:return "Pictures";
                case FileKind.Document:return "Documents";
                default:return null;
            }
        }
        // Files that run code when opened. The app shows them in their folder instead of opening them.
        public static bool IsExecutable(string name) {
            string[] risky={".exe",".msi",".msix",".msixbundle",".appx",".appxbundle",".bat",".cmd",".com",".scr",".ps1",".vbs",".vbe",".js",".jse",".wsf",".wsh",".hta",".lnk",".jar",".cpl",".reg",".pif",".application"};
            return Array.IndexOf(risky,Path.GetExtension(name??"").ToLowerInvariant())>=0;
        }
        public static bool LooksLikeFile(Uri uri) {
            string last=uri.AbsolutePath.Split('/').Last();
            return Of(Uri.UnescapeDataString(last))!=FileKind.Other;
        }
    }

    static class NativeFile {
        [DllImport("kernel32.dll", SetLastError=true)]
        static extern bool DeviceIoControl(SafeFileHandle device, uint code, ref byte input, uint inputSize, IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
        const uint FsctlSetSparse=0x000900C4;
        // Without the sparse flag NTFS zero-fills everything before a write at a far offset,
        // which costs as much as the download itself on a hard disk.
        public static void SetSparse(FileStream file,bool sparse) {
            byte value=(byte)(sparse?1:0);uint returned;
            if(!DeviceIoControl(file.SafeFileHandle,FsctlSetSparse,ref value,1,IntPtr.Zero,0,out returned,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    // Token bucket shared by the connections that use it.
    public sealed class Throttle {
        readonly object gate=new object();
        long limit;
        double tokens;
        long last=Stopwatch.GetTimestamp();
        public long BytesPerSecond {
            get { return Interlocked.Read(ref limit); }
            set { value=Math.Max(0,value);if(value==BytesPerSecond)return;lock(gate){Interlocked.Exchange(ref limit,value);tokens=0;last=Stopwatch.GetTimestamp();} }
        }
        public int Chunk(int wanted) {
            long rate=BytesPerSecond;
            return rate<=0?wanted:(int)Math.Max(16*1024,Math.Min(wanted,rate/8));
        }
        public void Take(int count,CancellationToken token) {
            double wait;
            lock(gate) {
                long rate=Interlocked.Read(ref limit);if(rate<=0)return;
                long now=Stopwatch.GetTimestamp();
                tokens=Math.Min(rate*0.25,tokens+(now-last)/(double)Stopwatch.Frequency*rate);last=now;
                tokens-=count;if(tokens>=0)return;
                wait=-tokens/rate;
            }
            if(token.WaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Min(wait,5))))token.ThrowIfCancellationRequested();
        }
    }
    // Global limit, shared by every connection of every download.
    public static class RateLimiter {
        public static readonly Throttle Global=new Throttle();
        public static long BytesPerSecond {get{return Global.BytesPerSecond;}set{Global.BytesPerSecond=value;}}
    }

    public sealed class Engine : IDisposable {
        public readonly Job Job;
        public readonly string Work;
        public volatile string Phase = "Connecting";
        public volatile string Detail = "Consultando o arquivo…";
        public readonly CancellationTokenSource Stop = new CancellationTokenSource();
        private readonly CancellationTokenSource abort = new CancellationTokenSource();
        private readonly HttpClient client;
        private readonly object logGate = new object();
        private readonly object gate = new object();
        private FileStream outputReservation;
        private string reservationPath;
        public volatile bool PauseRequested;
        public int TimeoutMs = 20000;
        public int RetryMs = 2000;
        public int CheckpointMs = 2000;
        public long ParallelThreshold = 16 * 1024 * 1024;
        public long MinSplit = 2 * 1024 * 1024;
        public int Connections = 8;
        public bool AllowSparse = true;
        private readonly Throttle own = new Throttle();
        private List<Segment> segments;
        private FileStream flushHandle;
        private volatile string target;
        private int connected;
        public int ActiveConnections { get { return Volatile.Read(ref connected); } }

        sealed class Segment {
            public long Start;
            public long End;      // inclusive; long.MaxValue while the size is unknown
            public long Written;  // bytes handed to Windows from Start onwards
            public bool Active;
            public long Remaining { get { long end=Volatile.Read(ref End);return end==long.MaxValue?long.MaxValue:end-(Start+Interlocked.Read(ref Written))+1; } }
        }

        public void RequestPause() {
            if(PauseRequested)return;PauseRequested=true;
            // Cancellation callbacks can close sockets slowly. Never run them on the UI thread.
            ThreadPool.QueueUserWorkItem(delegate {try{Stop.Cancel();client.CancelPendingRequests();}catch(ObjectDisposedException){} });
        }
        public Engine(Job job, string dataRoot) {
            Job = job; Work = Job.WorkFolder ?? Path.Combine(dataRoot, job.Id);Job.WorkFolder=Work;
            Directory.CreateDirectory(Work);
            try{var root=new DirectoryInfo(Path.GetDirectoryName(Work));if((root.Attributes&FileAttributes.Hidden)==0)root.Attributes|=FileAttributes.Hidden;}catch(IOException){}catch(UnauthorizedAccessException){}
            client = NewClient(new Uri(job.Url), job.Referrer);
        }
        static HttpClient NewClient(Uri uri, string referrer) {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None, AllowAutoRedirect = true, UseCookies = false };
            var http = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 MyDownload/3.0");
            http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("identity");
            Uri page;
            http.DefaultRequestHeaders.Referrer = !String.IsNullOrEmpty(referrer) && Uri.TryCreate(referrer, UriKind.Absolute, out page) && (page.Scheme == "http" || page.Scheme == "https")
                ? page : new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
            return http;
        }
        public static Uri ValidateUrl(string value) {
            Uri uri;
            if (!Uri.TryCreate((value??"").Trim(), UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https") || !String.IsNullOrEmpty(uri.UserInfo))
                throw new DownloadError("Cole um link direto que comece com http:// ou https://.");
            return uri;
        }
        public sealed class LinkInfo { public string Name; public long Size = -1; public bool Resumable, Page; }
        // Asks the server for the file name and size without downloading it. Null when it fails.
        public static LinkInfo Inspect(string url, string referrer, int timeoutMs) {
            var uri = ValidateUrl(url);
            using (var http = NewClient(uri, referrer))
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            using (var timeout = new CancellationTokenSource(timeoutMs)) {
                request.Headers.Range = new RangeHeaderValue(0, 0);
                try {
                    using (var response = http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).GetAwaiter().GetResult()) {
                        if ((int)response.StatusCode >= 400) return null;
                        var range = response.Content.Headers.ContentRange;
                        var disposition = response.Content.Headers.ContentDisposition;
                        var type = response.Content.Headers.ContentType;
                        string name = disposition == null ? null : (disposition.FileNameStar ?? disposition.FileName);
                        var info = new LinkInfo();
                        info.Resumable = response.StatusCode == HttpStatusCode.PartialContent && range != null && range.HasLength;
                        info.Size = info.Resumable ? range.Length.Value : (response.Content.Headers.ContentLength ?? -1);
                        info.Name = CleanName(String.IsNullOrEmpty(name) ? response.RequestMessage.RequestUri.AbsolutePath.Split('/').Last() : name.Trim('"'));
                        info.Page = disposition == null && type != null && type.MediaType == "text/html";
                        return info;
                    }
                } catch (Exception) { return null; }
            }
        }
        // Used by the browser extension: true when the link answers a file without the browser's
        // session (no error, no login page).
        public static bool Reachable(string url, string referrer) {
            var info = Inspect(url, referrer, 10000);
            return info != null && !info.Page;
        }
        public static string CleanName(string raw) {
            string name = Uri.UnescapeDataString(raw ?? "download").Replace('\\', '/').Split('/').Last();
            name = Regex.Replace(name, "[<>:\"/\\\\|?*\\x00-\\x1f]", "_").Trim().TrimEnd('.', ' ');
            if (String.IsNullOrWhiteSpace(name)) name = "download";
            if (Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)", RegexOptions.IgnoreCase)) name = "_" + name;
            if (name.Length > 160) name = name.Substring(0, 140) + Path.GetExtension(name).Substring(0, Math.Min(16, Path.GetExtension(name).Length));
            return name;
        }
        private void Check() { Stop.Token.ThrowIfCancellationRequested(); abort.Token.ThrowIfCancellationRequested(); }
        private void Log(string text) { lock (logGate) File.AppendAllText(Path.Combine(Work, "download.log"), DateTime.Now.ToString("s") + " " + text + Environment.NewLine); }
        private void Save() { Store.Save(Path.Combine(Work, "job.json"), Job.Snapshot()); }
        private void Set(string phase, string detail) { Phase = Job.State = phase; Detail = Job.Detail = detail; Save(); }
        public long Downloaded {
            get {
                if (Job.State == "Completed") return Job.Total;
                lock (gate) if (segments != null) { long sum = 0; foreach (var s in segments) sum += Written(s); return sum; }
                return Job.Saved;
            }
        }
        static long Written(Segment s) {
            long written=Interlocked.Read(ref s.Written),end=Volatile.Read(ref s.End);
            return end==long.MaxValue?written:Math.Max(0,Math.Min(written,end-s.Start+1));
        }
        private HttpResponseMessage Request(long? start, long? end, bool conditional) {
            Check();
            using (var request = new HttpRequestMessage(HttpMethod.Get, target ?? Job.Url)) {
                if (start.HasValue) request.Headers.Range = new RangeHeaderValue(start.Value, end);
                if (conditional) request.Headers.TryAddWithoutValidation("If-Range", !String.IsNullOrEmpty(Job.ETag) ? Job.ETag : Job.Modified);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(Stop.Token, abort.Token)) {
                    timeout.CancelAfter(TimeoutMs);
                    return Checked(client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).GetAwaiter().GetResult());
                }
            }
        }
        static HttpResponseMessage Checked(HttpResponseMessage response) {
            int status = (int)response.StatusCode;
            if (status >= 400) {
                response.Dispose();
                if (status == 408 || status == 429 || status >= 500) throw new HttpRequestException("Servidor temporariamente indisponível (HTTP " + status + ").");
                if (status == 401 || status == 403 || status == 410) throw new DownloadError("O site recusou o link (HTTP " + status + "). Ele pode ter expirado: use Atualizar link para continuar de onde parou.");
                throw new DownloadError("O servidor retornou HTTP " + status + ". Confira o link.");
            }
            if (response.Content.Headers.ContentEncoding.Any(x => x != "identity")) {
                response.Dispose(); throw new DownloadError("O servidor enviou uma codificação incompatível com a retomada.");
            }
            return response;
        }
        private int Read(Stream source, byte[] buffer, int count) {
            Check();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(Stop.Token, abort.Token)) {
                timeout.CancelAfter(TimeoutMs);
                // Framework streams may ignore ReadAsync cancellation. Close the response stream too.
                using(timeout.Token.Register(()=>{try{source.Dispose();}catch(IOException){}catch(ObjectDisposedException){}})) {
                    try{return source.ReadAsync(buffer,0,count,timeout.Token).GetAwaiter().GetResult();}
                    catch(Exception){if(timeout.IsCancellationRequested)throw new OperationCanceledException(timeout.Token);throw;}
                }
            }
        }
        private bool Retryable(Exception error) {
            return error is HttpRequestException || error is OperationCanceledException || error is System.Net.WebException || error is RemoteReadError;
        }
        private sealed class RemoteReadError : Exception { public RemoteReadError(Exception e) : base(e.Message, e) {} }
        private void Retry(int failures, Exception error) {
            Check();
            int delay = Math.Min(60000, RetryMs * (1 << Math.Min(5, failures - 1)));
            Detail = "Conexão interrompida. Nova tentativa em " + Math.Max(1, delay / 1000) + " s…";
            var cause = error.GetBaseException();
            Log("Network failure (" + cause.GetType().Name + "): " + cause.Message + ". Retrying in " + delay + " ms; saved data kept.");
            // After repeated failures go back through the original link in case the mirror changed.
            if (failures >= 3) target = null;
            if (Stop.Token.WaitHandle.WaitOne(delay)) Check();
            Check();
        }
        private void Probe() {
            int fails = 0;
            while (true) {
                try {
                    using (var response = Request(0, 0, false)) {
                        var range = response.Content.Headers.ContentRange;
                        bool ranged = response.StatusCode == HttpStatusCode.PartialContent && range != null && range.From == 0 && range.To == 0 && range.HasLength;
                        if (response.StatusCode == HttpStatusCode.PartialContent && !ranged) throw new DownloadError("O servidor retornou uma faixa inválida.");
                        long total = ranged ? range.Length.Value : (response.Content.Headers.ContentLength ?? -1);
                        string etag = response.Headers.ETag != null && !response.Headers.ETag.IsWeak ? response.Headers.ETag.ToString() : null;
                        string modified = response.Content.Headers.LastModified.HasValue ? response.Content.Headers.LastModified.Value.ToString("r") : null;
                        bool canResume = ranged && total > 0 && (etag != null || modified != null);
                        // Redirects are resolved once per run; the original link is what gets saved.
                        target = response.RequestMessage.RequestUri.AbsoluteUri;
                        if (Job.Starts != null) {
                            if (Job.Total != total || Job.Resume != canResume || (Job.ETag != null ? Job.ETag != etag : Job.Modified != modified))
                                throw new DownloadError("O arquivo mudou no servidor. O que já foi baixado continua salvo; adicione o link como outro download.");
                            ReserveOutput();
                            Save();
                        } else {
                            Job.Total = total; Job.ETag = etag; Job.Modified = modified; Job.Resume = canResume;
                            var disposition = response.Content.Headers.ContentDisposition;
                            string name = disposition == null ? null : (disposition.FileNameStar ?? disposition.FileName);
                            string served = CleanName(String.IsNullOrEmpty(name) ? response.RequestMessage.RequestUri.AbsolutePath.Split('/').Last() : name.Trim('"'));
                            if (served == "download" && response.Content.Headers.ContentType != null && response.Content.Headers.ContentType.MediaType == "text/html")
                                throw new DownloadError("Esse link abre uma página. Copie o link direto do arquivo.");
                            Job.Filename = String.IsNullOrWhiteSpace(Job.Name) ? served : CleanName(Job.Name);
                            if (Job.Organize) {
                                string sub = Categories.FolderFor(Job.Filename);
                                if (sub != null && !String.Equals(Path.GetFileName(Job.Folder.TrimEnd('\\')), sub, StringComparison.OrdinalIgnoreCase)) Job.Folder = Path.Combine(Job.Folder, sub);
                            }
                            Directory.CreateDirectory(Job.Folder);
                            ReserveOutput();
                            Plan(total, canResume);
                            Save();
                        }
                        return;
                    }
                } catch (Exception e) { if (!Retryable(e)) throw; Retry(++fails, e); }
            }
        }
        private void Plan(long total, bool canResume) {
            Directory.CreateDirectory(Work);
            bool sparse = canResume && AllowSparse && PrepareSparse(total);
            if (!sparse) using (new FileStream(Job.DataPath, FileMode.Create, FileAccess.Write, FileShare.Read)) {}
            // Without sparse files (FAT32, exFAT) several connections would force Windows to
            // zero-fill the gaps, so those downloads use one connection writing in order.
            int connections = sparse && total >= ParallelThreshold ? Math.Max(1, Connections) : 1;
            long width = total > 0 ? (long)Math.Ceiling((double)total / connections) : 0;
            lock (Job) {
                Job.Sparse = sparse;
                Job.Starts = new long[connections]; Job.Ends = new long[connections]; Job.Done = new long[connections];
                for (int i = 0; i < connections; i++) { Job.Starts[i] = i * width; Job.Ends[i] = total > 0 ? Math.Min(total - 1, (i + 1) * width - 1) : -1; }
            }
            Log("Plan: " + connections + " connection(s)" + (sparse ? ", sparse file" : "") + ", " + total + " bytes, resume " + canResume + ".");
        }
        private bool PrepareSparse(long total) {
            try {
                using (var file = new FileStream(Job.DataPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read)) { NativeFile.SetSparse(file, true); file.SetLength(total); }
                return true;
            } catch (Win32Exception e) { Log("Sparse files unavailable: " + e.Message); }
            catch (IOException e) { Log("Sparse files unavailable: " + e.Message); }
            TryDelete(Job.DataPath);
            return false;
        }
        static void TryDelete(string path) { try { if (path != null && File.Exists(path)) File.Delete(path); } catch (IOException) {} catch (UnauthorizedAccessException) {} }
        private void ReserveOutput() {
            if(outputReservation!=null)return;
            string basis=Job.Filename;
            string reservations=Path.Combine(Path.GetDirectoryName(Work),"reservations");Directory.CreateDirectory(reservations);
            for(int suffix=0;suffix<10000;suffix++) {
                string name=suffix==0?basis:Path.GetFileNameWithoutExtension(basis)+" ("+suffix+")"+Path.GetExtension(basis);
                string output=Path.Combine(Job.Folder,name);
                if(File.Exists(output))continue;
                string key;
                using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(output).ToUpperInvariant()))).Replace("-","");
                string path=Path.Combine(reservations,key+".lock");
                FileStream reservation;
                try { reservation=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
                catch(IOException ex) { if((ex.HResult&0xffff)==32 || (ex.HResult&0xffff)==33)continue;throw; }
                try {
                    var buffer=new byte[(int)Math.Min(256,reservation.Length)];reservation.Read(buffer,0,buffer.Length);
                    string owner=Encoding.UTF8.GetString(buffer);
                    if(owner.Length>0 && owner!=Job.Id){reservation.Dispose();continue;}
                    byte[] id=Encoding.UTF8.GetBytes(Job.Id);reservation.Position=0;reservation.Write(id,0,id.Length);reservation.SetLength(id.Length);reservation.Flush(true);
                    outputReservation=reservation;reservationPath=path;Job.Filename=name;return;
                } catch { reservation.Dispose();throw; }
            }
            throw new DownloadError("Não foi possível reservar um nome livre para o arquivo.");
        }
        private void ReleaseReservation(bool delete) {
            if(outputReservation!=null){outputReservation.Dispose();outputReservation=null;}
            if(delete&&reservationPath!=null)TryDelete(reservationPath);
        }
        // Removes the name reservation a job left behind (used when a download is removed).
        public static void ReleaseReservations(string work, string id) {
            try {
                string folder=Path.Combine(Path.GetDirectoryName(work),"reservations");if(!Directory.Exists(folder))return;
                foreach(string file in Directory.GetFiles(folder,"*.lock")) {
                    try{if(new FileInfo(file).Length<=256&&File.ReadAllText(file)==id)File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}
                }
            } catch(IOException){} catch(UnauthorizedAccessException){}
        }

        private void Transfer() {
            bool restart = !Job.Resume;
            if (restart) { using (new FileStream(Job.DataPath, FileMode.Create, FileAccess.Write, FileShare.Read)) {} lock (Job) Job.Done = new long[Job.Starts.Length]; }
            lock (gate) {
                segments = new List<Segment>();
                for (int i = 0; i < Job.Starts.Length; i++) {
                    long end = Job.Ends[i] < 0 ? long.MaxValue : Job.Ends[i];
                    long done = Job.Done != null && i < Job.Done.Length ? Job.Done[i] : 0;
                    segments.Add(new Segment { Start = Job.Starts[i], End = end, Written = end == long.MaxValue ? done : Math.Min(done, end - Job.Starts[i] + 1) });
                }
            }
            int workers = Job.Resume && Job.Total > 0 && Job.Sparse ? Math.Max(1, Connections) : 1;
            Detail = Job.Resume ? "Baixando com até " + workers + (workers == 1 ? " conexão" : " conexões") : "Este servidor não permite continuar: se a conexão cair, o arquivo recomeça.";
            Log("Run: " + workers + " worker(s), " + segments.Count + " segment(s), " + Downloaded + " bytes already saved.");
            flushHandle = new FileStream(Job.DataPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1);
            try {
                var tasks = Enumerable.Range(0, workers).Select(i => Task.Factory.StartNew(() => {
                    try { Worker(); } catch { abort.Cancel(); throw; }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
                try {
                    while (!Task.WaitAll(tasks, CheckpointMs)) Checkpoint();
                } catch (AggregateException errors) {
                    throw errors.Flatten().InnerExceptions.FirstOrDefault(e => !(e is OperationCanceledException)) ?? errors.Flatten().InnerExceptions[0];
                } finally {
                    try { Checkpoint(); } catch (IOException e) { Log("Checkpoint failed: " + e.Message); }
                }
            } finally { flushHandle.Dispose(); flushHandle = null; }
            Finish();
        }
        private void Worker() {
            Interlocked.Increment(ref connected);
            try {
                using (var output = new FileStream(Job.DataPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1)) {
                    Segment segment;
                    while ((segment = Claim()) != null) {
                        try { Download(segment, output); } finally { lock (gate) segment.Active = false; }
                    }
                }
            } finally { Interlocked.Decrement(ref connected); }
        }
        // Gives a connection the next unfinished range, or splits the largest range still being
        // downloaded so no connection sits idle while one slow connection finishes the file.
        private Segment Claim() {
            Check();
            lock (gate) {
                var free = segments.Where(s => !s.Active && s.Remaining > 0).OrderBy(s => s.Start).FirstOrDefault();
                if (free != null) { free.Active = true; return free; }
                if (!Job.Resume || Job.Total <= 0 || !Job.Sparse) return null;
                var victim = segments.Where(s => s.Active).OrderByDescending(s => s.Remaining).FirstOrDefault();
                if (victim == null || victim.Remaining < 2 * MinSplit) return null;
                long position = victim.Start + Interlocked.Read(ref victim.Written);
                long middle = position + victim.Remaining / 2;
                var created = new Segment { Start = middle, End = victim.End, Active = true };
                Volatile.Write(ref victim.End, middle - 1);
                segments.Add(created);
                return created;
            }
        }
        private void Download(Segment segment, FileStream output) {
            int failures = 0;
            var buffer = new byte[256 * 1024];
            while (true) {
                Check();
                long end = Volatile.Read(ref segment.End);
                long position = segment.Start + Interlocked.Read(ref segment.Written);
                if (end != long.MaxValue && position > end) return;
                long before = Interlocked.Read(ref segment.Written);
                try {
                    bool ranged = Job.Resume;
                    using (var response = Request(ranged ? (long?)position : null, ranged ? (long?)end : null, ranged)) {
                        if (ranged) {
                            var range = response.Content.Headers.ContentRange;
                            if (response.StatusCode != HttpStatusCode.PartialContent || range == null || range.From != position || range.To != end || range.Length != Job.Total)
                                throw new DownloadError("O servidor não confirmou a retomada. O que já foi baixado continua salvo, sem risco de arquivo corrompido.");
                            if (Job.ETag != null && (response.Headers.ETag == null || response.Headers.ETag.ToString() != Job.ETag))
                                throw new DownloadError("O arquivo mudou durante o download. O que já foi baixado continua salvo.");
                            if (Job.ETag == null && (!response.Content.Headers.LastModified.HasValue || response.Content.Headers.LastModified.Value.ToString("r") != Job.Modified))
                                throw new DownloadError("O servidor não confirmou a versão do arquivo. O que já foi baixado continua salvo.");
                        } else {
                            if (response.StatusCode != HttpStatusCode.OK) throw new DownloadError("Resposta inesperada do servidor.");
                            long length = response.Content.Headers.ContentLength ?? -1;
                            if (Job.Total >= 0 && length >= 0 && Job.Total != length) throw new DownloadError("O tamanho do arquivo mudou no servidor.");
                            if (Interlocked.Read(ref segment.Written) != 0) { Interlocked.Exchange(ref segment.Written, 0); before = 0; position = segment.Start; }
                        }
                        output.Position = position;
                        bool ended = false;
                        using (var source = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()) {
                            while (true) {
                                long limit = Volatile.Read(ref segment.End);
                                long at = segment.Start + Interlocked.Read(ref segment.Written);
                                if (limit != long.MaxValue && at > limit) { ended = true; break; }
                                int wanted = limit == long.MaxValue ? buffer.Length : (int)Math.Min(buffer.Length, limit - at + 1);
                                int read;
                                own.BytesPerSecond = Job.SpeedLimit;
                                try { read = Read(source, buffer, own.Chunk(RateLimiter.Global.Chunk(wanted))); }
                                catch (IOException e) { throw new RemoteReadError(e); }
                                if (read == 0) {
                                    // Unknown size: the end of the stream is the end of the file.
                                    if (limit == long.MaxValue) { lock (gate) Volatile.Write(ref segment.End, segment.Start + Interlocked.Read(ref segment.Written) - 1); ended = true; }
                                    break;
                                }
                                RateLimiter.Global.Take(read, Stop.Token);
                                own.Take(read, Stop.Token);
                                // The range may have been split while this read was in flight.
                                limit = Volatile.Read(ref segment.End);
                                int take = limit == long.MaxValue ? read : (int)Math.Max(0, Math.Min(read, limit - at + 1));
                                if (take > 0) { output.Write(buffer, 0, take); Interlocked.Add(ref segment.Written, take); }
                                if (take < read) { ended = true; break; }
                            }
                        }
                        if (ended) return;
                        throw new RemoteReadError(new IOException("Resposta incompleta."));
                    }
                } catch (Exception e) {
                    if (!Retryable(e)) throw;
                    long after = Interlocked.Read(ref segment.Written);
                    failures = after > before ? 1 : failures + 1;
                    Retry(failures, e);
                }
            }
        }
        // Order matters: snapshot the counters, flush the file to disk, then record the snapshot.
        // A crash can only lose bytes written after the snapshot, which are downloaded again.
        private void Checkpoint() {
            if (segments == null || flushHandle == null) return;
            long[] starts, ends, done;
            lock (gate) {
                starts = segments.Select(s => s.Start).ToArray();
                ends = segments.Select(s => { long e = Volatile.Read(ref s.End); return e == long.MaxValue ? -1 : e; }).ToArray();
                done = segments.Select(s => Written(s)).ToArray();
            }
            flushHandle.Flush(true);
            lock (Job) { Job.Starts = starts; Job.Ends = ends; Job.Done = done; }
            Save();
        }
        private void Finish() {
            Check();
            long total;
            lock (gate) {
                if (Job.Total >= 0) {
                    if (segments.Any(s => s.Remaining > 0)) throw new RemoteReadError(new IOException("Faltam partes do arquivo."));
                    total = Job.Total;
                } else total = Interlocked.Read(ref segments[0].Written);
            }
            Set("Finalizing", "Finalizando…");
            using (var file = new FileStream(Job.DataPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                if (Job.Total < 0) file.SetLength(total);
                if (file.Length != total) throw new DownloadError("O tamanho final não corresponde ao informado pelo servidor.");
                if (Job.Sparse) try { NativeFile.SetSparse(file, false); } catch (Win32Exception e) { Log("Could not clear the sparse flag: " + e.Message); }
                file.Flush(true);
            }
            lock (Job) { Job.Total = total; if (Job.Done.Length == 1 && Job.Ends[0] < 0) { Job.Ends[0] = total - 1; Job.Done[0] = total; } }
            Check();
            if (File.Exists(Job.Output)) throw new DownloadError("Já existe um arquivo com esse nome no destino. O download foi preservado.");
            Save();
            // data.part lives inside the destination folder, so this is a rename on the same disk.
            File.Move(Job.DataPath, Job.Output);
            Job.Completed = DateTime.Now.ToString("s");
            Set("Completed", "Arquivo salvo.");
            Log("Completed: " + Job.Output + " (" + total + " bytes).");
            ReleaseReservation(true);
        }
        // A crash between the rename and the final save leaves a finished file and no data.part.
        private bool Recover() {
            if (Job.Starts == null) return false;
            bool hasData = File.Exists(Job.DataPath);
            if (!hasData && Job.Total > 0 && Job.Done != null && Job.Done.Sum() == Job.Total && Job.Filename != null && File.Exists(Job.Output) && new FileInfo(Job.Output).Length == Job.Total) {
                Job.Completed = Job.Completed ?? DateTime.Now.ToString("s"); Set("Completed", "Arquivo conferido após a retomada."); return true;
            }
            long length = hasData ? new FileInfo(Job.DataPath).Length : -1;
            bool sizeOk = hasData && (Job.Sparse ? length == Job.Total : length >= (Job.Done == null ? 0 : Job.Done.Sum()));
            if (!sizeOk) {
                Log("Saved data missing or resized; starting over.");
                Job.ResetTransfer();
            }
            return false;
        }
        private void CheckSpace() {
            long needed = Math.Max(0, Job.Total - Downloaded) + 64 * 1024 * 1024;
            long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(Job.Folder))).AvailableFreeSpace;
            if (Job.Total > 0 && free < needed) throw new DownloadError("Falta espaço nesta unidade: precisa de mais " + Units.Size(needed - free) + ".");
        }
        public void Run() {
            FileStream fileLock;
            try { fileLock = new FileStream(Path.Combine(Work, "download.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { Phase = "Error"; Detail = "Este download já está aberto em outro programa ou a pasta está indisponível."; return; }
            try {
                using (fileLock) {
                    if (Job.State == "Completed") return;
                    if (Recover()) return;
                    Set("Connecting", "Consultando o arquivo…"); Probe();
                    CheckSpace();
                    Set("Downloading", "Preparando a transferência…");
                    Transfer();
                }
            } catch (OperationCanceledException) { Outcome("Paused", "Progresso salvo. Clique em retomar quando quiser."); }
            catch (Exception error) { if(Stop.IsCancellationRequested)Outcome("Paused","Progresso salvo.");else Outcome("Error", error is DownloadError ? error.Message : "Não foi possível concluir: " + error.Message); }
            finally { ReleaseReservation(false); }
        }
        private void Outcome(string phase, string detail) {
            Phase = Job.State = phase; Detail = Job.Detail = detail;
            try { Log(phase + ": " + detail); } catch (IOException) {} catch (UnauthorizedAccessException) {}
            try { Save(); } catch (IOException) {} catch (UnauthorizedAccessException) {}
        }
        public void Dispose() { client.Dispose(); Stop.Dispose(); abort.Dispose(); }

        public static string Hex(byte[] hash) { return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant(); }
        public static string ComputeSha256(string path, CancellationToken token, Action<long> progress) {
            using (var sha = SHA256.Create())
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan)) {
                var buffer = new byte[1 << 20]; long done = 0; int count;
                while ((count = file.Read(buffer, 0, buffer.Length)) > 0) {
                    token.ThrowIfCancellationRequested();
                    sha.TransformBlock(buffer, 0, count, null, 0); done += count;
                    if (progress != null) progress(done);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return Hex(sha.Hash);
            }
        }

        // ---- replacing an expired link -------------------------------------------------
        public sealed class Replacement { public string Url, ETag, Modified; public long Total; public int Samples; }
        // Accepts a new link only when it serves the same size, supports ranges and returns the
        // same bytes as samples of what is already saved. Nothing is changed when it fails.
        public static Replacement CheckReplacement(Job job, string url, CancellationToken token) {
            var uri = ValidateUrl(url);
            using (var http = NewClient(uri, job.Referrer)) {
                var result = new Replacement { Url = uri.AbsoluteUri };
                using (var response = Send(http, result.Url, 0, 0, token)) {
                    var range = response.Content.Headers.ContentRange;
                    bool ranged = response.StatusCode == HttpStatusCode.PartialContent && range != null && range.From == 0 && range.To == 0 && range.HasLength;
                    if (!ranged) throw new DownloadError("O servidor do link novo não permite continuar de onde parou. Adicione-o como um download novo.");
                    result.Total = range.Length.Value;
                    result.ETag = response.Headers.ETag != null && !response.Headers.ETag.IsWeak ? response.Headers.ETag.ToString() : null;
                    result.Modified = response.Content.Headers.LastModified.HasValue ? response.Content.Headers.LastModified.Value.ToString("r") : null;
                    if (result.ETag == null && result.Modified == null) throw new DownloadError("O servidor do link novo não identifica a versão do arquivo, então não dá para continuar com segurança.");
                }
                if (job.Total > 0 && result.Total != job.Total) throw new DownloadError("O link novo é de um arquivo de outro tamanho (" + Units.Size(result.Total) + " em vez de " + Units.Size(job.Total) + "). Nada foi alterado.");
                foreach (var sample in Samples(job)) {
                    byte[] local = ReadSaved(job, sample[0], (int)sample[1]);
                    byte[] remote;
                    using (var response = Send(http, result.Url, sample[0], sample[0] + sample[1] - 1, token)) {
                        var range = response.Content.Headers.ContentRange;
                        if (response.StatusCode != HttpStatusCode.PartialContent || range == null || range.From != sample[0]) throw new DownloadError("O servidor do link novo não confirmou a faixa pedida. Nada foi alterado.");
                        remote = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    }
                    if (!remote.SequenceEqual(local)) throw new DownloadError("O conteúdo do link novo é diferente do que já foi baixado. Nada foi alterado.");
                    result.Samples++;
                }
                return result;
            }
        }
        static HttpResponseMessage Send(HttpClient http, string url, long start, long end, CancellationToken token) {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url)) {
                request.Headers.Range = new RangeHeaderValue(start, end);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    timeout.CancelAfter(20000);
                    try { return Checked(http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token).GetAwaiter().GetResult()); }
                    catch (OperationCanceledException) { if (token.IsCancellationRequested) throw; throw new DownloadError("O servidor do link novo não respondeu a tempo."); }
                    catch (HttpRequestException e) { throw new DownloadError("Não foi possível abrir o link novo: " + e.GetBaseException().Message); }
                }
            }
        }
        // Up to three 64 KB windows taken from what is already on disk: start, middle and end.
        static List<long[]> Samples(Job job) {
            var regions = new List<long[]>();
            var copy = job.Snapshot();
            if (copy.Starts == null || copy.Done == null || copy.WorkFolder == null) return regions;
            for (int i = 0; i < copy.Starts.Length && i < copy.Done.Length; i++) if (copy.Done[i] > 0) regions.Add(new[] { copy.Starts[i], copy.Done[i] });
            var samples = new List<long[]>();
            if (regions.Count == 0) return samples;
            const long size = 64 * 1024;
            var first = regions.OrderBy(r => r[0]).First();
            var largest = regions.OrderByDescending(r => r[1]).First();
            var last = regions.OrderBy(r => r[0]).Last();
            samples.Add(new[] { first[0], Math.Min(size, first[1]) });
            if (largest[1] > 3 * size) samples.Add(new[] { largest[0] + largest[1] / 2, size });
            if (last[1] > size) samples.Add(new[] { last[0] + last[1] - size, size });
            return samples;
        }
        static byte[] ReadSaved(Job job, long offset, int count) {
            var buffer = new byte[count];
            using (var file = new FileStream(job.DataPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                file.Position = offset; int total = 0, read;
                while (total < count && (read = file.Read(buffer, total, count - total)) > 0) total += read;
                if (total != count) throw new DownloadError("Não foi possível ler o que já foi baixado para comparar.");
            }
            return buffer;
        }
    }
}
