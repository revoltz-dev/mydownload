using System;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MyDownload {
    // Chrome/Edge extension (embedded in the exe) plus the native messaging host it talks to.
    static class ChromeIntegration {
        public const string HostName="com.mydownload.app";
        // Fixed by the "key" in extension\manifest.json, so the host only answers this extension.
        public const string ExtensionId="mnbmcnjoiikldfeefbacbnndinoalbep";
        static readonly string[] Files={"manifest.json","background.js","popup.html","popup.js"};
        static readonly string[] Browsers={@"Software\Google\Chrome\NativeMessagingHosts\",@"Software\Microsoft\Edge\NativeMessagingHosts\"};
        public static string Folder {get{return Path.Combine(AppPaths.Root,"chrome-extension");}}
        static string HostManifest {get{return Path.Combine(AppPaths.Root,HostName+".json");}}

        public static bool Registered {
            get {try{using(var key=Registry.CurrentUser.OpenSubKey(Browsers[0]+HostName))return key!=null;}catch(Exception){return false;}}
        }
        // Extracts the extension next to the app's json files and registers the host for Chrome and Edge.
        public static string Install() {
            Extract();
            foreach(int size in new[]{16,32,48,128})using(var image=Program.IconImage(size))image.Save(Path.Combine(Folder,"icon"+size+".png"),ImageFormat.Png);
            WriteHostManifest();
            // Test copies (--data) never touch the real browser registration.
            if(!AppPaths.Custom)foreach(string browser in Browsers)using(var key=Registry.CurrentUser.CreateSubKey(browser+HostName))key.SetValue("",HostManifest);
            return Folder;
        }
        public static void Unregister() {
            foreach(string browser in Browsers)try{Registry.CurrentUser.DeleteSubKeyTree(browser+HostName,false);}catch(Exception){}
        }
        // Writes the embedded extension files; returns true when something on disk changed.
        static bool Extract() {
            Directory.CreateDirectory(Folder);
            var assembly=Assembly.GetExecutingAssembly();bool changed=false;
            foreach(string name in Files) {
                byte[] bytes;
                using(var source=assembly.GetManifestResourceStream("ext."+name)) {
                    if(source==null)throw new IOException("A extensão não veio junto neste executável. Compile de novo com o build.bat.");
                    using(var copy=new MemoryStream()){source.CopyTo(copy);bytes=copy.ToArray();}
                }
                string path=Path.Combine(Folder,name);
                if(File.Exists(path)&&File.ReadAllBytes(path).SequenceEqual(bytes))continue;
                File.WriteAllBytes(path,bytes);changed=true;
            }
            return changed;
        }
        // Keeps the host pointing at this exe if it was moved or rebuilt somewhere else, and the
        // installed extension files in step with this version (Chrome picks them up on reload).
        public static bool Refresh() {
            if(AppPaths.Custom||!Registered)return false;
            try{WriteHostManifest();return Directory.Exists(Folder)&&Extract();}catch(IOException){}catch(UnauthorizedAccessException){}
            return false;
        }
        static void WriteHostManifest() {
            string json="{\n  \"name\": \""+HostName+"\",\n  \"description\": \""+AppPaths.Name+"\",\n  \"path\": "+Json(Application.ExecutablePath)+
                ",\n  \"type\": \"stdio\",\n  \"allowed_origins\": [\"chrome-extension://"+ExtensionId+"/\"]\n}\n";
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(HostManifest,json,new UTF8Encoding(false));
        }
        static string Json(string text) {
            var b=new StringBuilder("\"");
            foreach(char c in text??""){if(c=='"'||c=='\\')b.Append('\\').Append(c);else if(c<' ')b.Append("\\u").Append(((int)c).ToString("x4"));else b.Append(c);}
            return b.Append('"').ToString();
        }

        // ---- native messaging host: Chrome starts the exe with the extension origin as argument -----
#pragma warning disable 0649
        [DataContract] sealed class Request {
            [DataMember(Name="action")] public string Action;
            [DataMember(Name="url")] public string Url;
            [DataMember(Name="referrer")] public string Referrer;
            [DataMember(Name="filename")] public string Filename;
            [DataMember(Name="size")] public long Size;
            [DataMember(Name="check")] public bool Check;
        }
#pragma warning restore 0649
        public static int RunHost() {
            try {
                var input=Console.OpenStandardInput();var output=Console.OpenStandardOutput();
                var request=Read(input);if(request==null)return 0;
                string reply;
                if(request.Action=="ping")reply="{\"ok\":true,\"version\":\"3.0\"}";
                else if(request.Action=="add") {
                    string error=null;
                    try {
                        var uri=Engine.ValidateUrl(request.Url);
                        if(request.Check&&!Engine.Reachable(uri.AbsoluteUri,request.Referrer))error="Esse link só funciona dentro do navegador (login ou página).";
                        else Deliver(new Incoming{Url=uri.AbsoluteUri,Name=request.Filename,Size=request.Size,Referrer=request.Referrer});
                    } catch(Exception e){error=e.Message;}
                    reply=error==null?"{\"ok\":true}":"{\"ok\":false,\"reason\":"+Json(error)+"}";
                }
                else reply="{\"ok\":false,\"reason\":\"Pedido desconhecido.\"}";
                Write(output,reply);
                return 0;
            } catch(Exception){return 1;}
        }
        // Messages are a 4-byte length in native byte order followed by UTF-8 JSON.
        static Request Read(Stream input) {
            var header=ReadExactly(input,4);if(header==null)return null;
            int length=BitConverter.ToInt32(header,0);if(length<=0||length>1024*1024)return null;
            var body=ReadExactly(input,length);if(body==null)return null;
            using(var stream=new MemoryStream(body))return (Request)new DataContractJsonSerializer(typeof(Request)).ReadObject(stream);
        }
        static byte[] ReadExactly(Stream input,int count) {
            var buffer=new byte[count];int done=0;
            while(done<count){int read=input.Read(buffer,done,count-done);if(read<=0)return null;done+=read;}
            return buffer;
        }
        static void Write(Stream output,string json) {
            var body=Encoding.UTF8.GetBytes(json);
            output.Write(BitConverter.GetBytes(body.Length),0,4);output.Write(body,0,body.Length);output.Flush();
        }
        // Hands the download to the open window (which comes to the front), or starts the app with it.
        static void Deliver(Incoming item) {
            if(Program.Forward("browser",new[]{item.ToLine()},0))return;
            string arguments=(AppPaths.Custom?"--data \""+AppPaths.Root+"\" ":"")+"--incoming "+Convert.ToBase64String(Encoding.UTF8.GetBytes(item.ToLine()));
            Launch(Application.ExecutablePath,arguments);
        }

        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        struct StartupInfo {
            public int Size;public string Reserved,Desktop,Title;
            public int X,Y,Width,Height,CharsX,CharsY,Fill,Flags;public short Show,Reserved2;
            public IntPtr Reserved3,Input,Output,Error;
        }
        [StructLayout(LayoutKind.Sequential)] struct ProcessInfo{public IntPtr Process,Thread;public int ProcessId,ThreadId;}
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern bool CreateProcess(string application,StringBuilder command,IntPtr processAttributes,IntPtr threadAttributes,bool inheritHandles,uint flags,IntPtr environment,string directory,ref StartupInfo startup,out ProcessInfo info);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        // No inherited handles (the browser's pipes stay with the host) and, when allowed, outside
        // the browser's job so closing the host does not close the app.
        static void Launch(string exe,string arguments) {
            foreach(uint flags in new uint[]{0x01000000|0x200,0x200}) {
                var startup=new StartupInfo{Size=Marshal.SizeOf(typeof(StartupInfo))};ProcessInfo info;
                if(CreateProcess(exe,new StringBuilder("\""+exe+"\" "+arguments),IntPtr.Zero,IntPtr.Zero,false,flags,IntPtr.Zero,Path.GetDirectoryName(exe),ref startup,out info)) {
                    CloseHandle(info.Thread);CloseHandle(info.Process);return;
                }
            }
            throw new IOException("Não foi possível abrir o MyDownload.");
        }
    }
}
