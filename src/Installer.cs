using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MyDownload {
    // Per-user installation (no administrator): copies the exe to the chosen folder (by default
    // %LOCALAPPDATA%\Programs\MyDownload), adds Start menu and desktop shortcuts and the entry used by
    // Windows Settings > Apps to uninstall. Test copies (--data) use their own folders and registry entry.
    static class Installer {
        const string UninstallKeys=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\";
        static string KeyName {get{return AppPaths.Custom?AppPaths.Name+"-test-"+Program.Short(AppPaths.Root):AppPaths.Name;}}
        public static string DefaultFolder {get{return AppPaths.Custom?Path.Combine(AppPaths.Root,"Programs"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs",AppPaths.Name);}}
        // Where it is installed (as recorded in the uninstall entry), or where it would go.
        public static string Folder {
            get {
                try{using(var key=Registry.CurrentUser.OpenSubKey(UninstallKeys+KeyName)){var saved=key==null?null:key.GetValue("InstallLocation") as string;if(!String.IsNullOrEmpty(saved))return saved;}}catch(Exception){}
                return DefaultFolder;
            }
        }
        public static string InstalledExe {get{return Path.Combine(Folder,AppPaths.Name+".exe");}}
        public static bool DesktopShortcut {get{return File.Exists(DesktopLink);}}
        static string StartMenuLink {get{return Path.Combine(AppPaths.Custom?Path.Combine(AppPaths.Root,"StartMenu"):Environment.GetFolderPath(Environment.SpecialFolder.Programs),AppPaths.Name+".lnk");}}
        static string DesktopLink {get{return Path.Combine(AppPaths.Custom?Path.Combine(AppPaths.Root,"Desktop"):Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),AppPaths.Name+".lnk");}}

        public static bool Installed {
            get {try{using(var key=Registry.CurrentUser.OpenSubKey(UninstallKeys+KeyName))return key!=null&&File.Exists(InstalledExe);}catch(Exception){return false;}}
        }
        public static bool RunningInstalled {get{return String.Equals(Path.GetFullPath(Application.ExecutablePath),Path.GetFullPath(InstalledExe),StringComparison.OrdinalIgnoreCase);}}
        // This exe is a different build from the installed one (a newer build.bat output, for example).
        public static bool Outdated {
            get {
                if(!Installed||RunningInstalled)return false;
                try{return !File.ReadAllBytes(Application.ExecutablePath).SequenceEqual(File.ReadAllBytes(InstalledExe));}catch(IOException){return false;}
            }
        }
        // folder: null keeps the current place. A copy installed somewhere else before is removed.
        public static void Install(string folder=null,bool desktop=true,bool? startup=null) {
            string previous=Installed?InstalledExe:null;
            folder=Path.GetFullPath(folder??Folder);
            string exe=Path.Combine(folder,AppPaths.Name+".exe");
            CloseRunning(exe);
            if(!String.Equals(Path.GetFullPath(Application.ExecutablePath),exe,StringComparison.OrdinalIgnoreCase)) {
                try{Directory.CreateDirectory(folder);File.Copy(Application.ExecutablePath,exe,true);}
                catch(UnauthorizedAccessException){CopyAsAdministrator(folder,exe);}
            }
            string data=AppPaths.Custom?" --data \""+AppPaths.Root+"\"":"";
            using(var key=Registry.CurrentUser.CreateSubKey(UninstallKeys+KeyName)) {
                key.SetValue("DisplayName",AppPaths.Custom?AppPaths.Name+" (teste)":AppPaths.Name);
                key.SetValue("DisplayVersion","3.0");
                key.SetValue("Publisher",AppPaths.Name);
                key.SetValue("DisplayIcon",exe+",0");
                key.SetValue("InstallLocation",folder);
                key.SetValue("UninstallString","\""+exe+"\""+data+" --uninstall");
                key.SetValue("QuietUninstallString","\""+exe+"\""+data+" --uninstall --quiet");
                key.SetValue("InstallDate",DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("EstimatedSize",(int)Math.Max(1,new FileInfo(exe).Length/1024),RegistryValueKind.DWord);
                key.SetValue("NoModify",1,RegistryValueKind.DWord);key.SetValue("NoRepair",1,RegistryValueKind.DWord);
            }
            CreateLink(StartMenuLink);
            if(desktop)CreateLink(DesktopLink);else TryDelete(DesktopLink);
            if(!AppPaths.Custom&&(startup??Startup.Enabled))Startup.Set(true,exe);
            else if(!AppPaths.Custom&&startup==false)Startup.Set(false);
            if(previous!=null&&!String.Equals(previous,exe,StringComparison.OrdinalIgnoreCase)){TryDelete(previous);DeleteIfEmpty(Path.GetDirectoryName(previous));}
        }
        // Closes the open app when it runs from exe, so the file can be replaced.
        static void CloseRunning(string exe){Program.CloseApp(exe);}
        // Folders like Program Files need administrator rights. Only the copy runs elevated (Windows
        // asks first); the app itself keeps running as the normal user.
        static void CopyAsAdministrator(string folder,string exe) {
            var info=new ProcessStartInfo(Application.ExecutablePath,"--copy-to \""+folder+"\""){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
            try{using(var copy=Process.Start(info))copy.WaitForExit();}
            catch(System.ComponentModel.Win32Exception){throw new UnauthorizedAccessException("O Windows não deu permissão de administrador para essa pasta.");}
            if(!File.Exists(exe)||!File.ReadAllBytes(exe).SequenceEqual(File.ReadAllBytes(Application.ExecutablePath)))
                throw new UnauthorizedAccessException("Não foi possível copiar para essa pasta.");
        }
        // Runs elevated (see CopyAsAdministrator).
        public static int CopyTo(string folder) {
            try{Directory.CreateDirectory(folder);File.Copy(Application.ExecutablePath,Path.Combine(folder,AppPaths.Name+".exe"),true);return 0;}
            catch(Exception){return 1;}
        }
        static bool Writable(string folder) {
            try{string probe=Path.Combine(folder,".write-test-"+Guid.NewGuid().ToString("N"));File.WriteAllText(probe,"");File.Delete(probe);return true;}
            catch(Exception){return false;}
        }
        static void TryDelete(string file){try{if(File.Exists(file))File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}}
        static void DeleteIfEmpty(string folder){try{if(Directory.Exists(folder)&&!Directory.EnumerateFileSystemEntries(folder).Any())Directory.Delete(folder);}catch(IOException){}catch(UnauthorizedAccessException){}}
        // Removes what Install created. Downloaded files are never touched; with removeData the
        // history, settings and the unfinished downloads' work folders go too.
        public static void Uninstall(bool removeData) {
            foreach(string link in new[]{StartMenuLink,DesktopLink})try{if(File.Exists(link))File.Delete(link);}catch(IOException){}catch(UnauthorizedAccessException){}
            try{Registry.CurrentUser.DeleteSubKeyTree(UninstallKeys+KeyName,false);}catch(Exception){}
            if(!AppPaths.Custom) {
                try{Startup.Set(false);}catch(Exception){}
                ChromeIntegration.Unregister();
            }
            if(removeData)RemoveData();
        }
        static void RemoveData() {
            string history=Path.Combine(AppPaths.Root,"history.json");
            try {
                if(File.Exists(history))
                    foreach(var entry in Store.Read<HistoryDocument>(history).Downloads)
                        if(entry.Download!=null&&entry.Download.State!="Completed")DownloadCoordinator.DeleteWork(entry.Download);
            } catch(Exception){}
            foreach(string name in new[]{"history.json","settings.json",ChromeIntegration.HostName+".json"})try{File.Delete(Path.Combine(AppPaths.Root,name));}catch(Exception){}
            try{if(Directory.Exists(ChromeIntegration.Folder))Directory.Delete(ChromeIntegration.Folder,true);}catch(Exception){}
            foreach(string file in Directory.Exists(AppPaths.Root)?Directory.GetFiles(AppPaths.Root,"*.backup-*"):new string[0])try{File.Delete(file);}catch(Exception){}
        }
        // The exe cannot delete itself while it runs; cmd does it a moment after it closes. Only the
        // exe goes, and the folder only when nothing else is in it (it may be a folder the user chose).
        public static void RemoveFolderAfterExit(string folder,string exe) {
            if(!File.Exists(exe))return;
            string command="/d /c ping -n 3 127.0.0.1 >nul & del /f /q \""+exe+"\" & rd \""+folder+"\" 2>nul";
            // In a protected folder (Program Files) the removal also needs administrator rights.
            var info=Writable(folder)?new ProcessStartInfo("cmd.exe",command){UseShellExecute=false,CreateNoWindow=true}
                :new ProcessStartInfo("cmd.exe",command){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
            try{Process.Start(info);}catch(Exception){}
        }

        [ComImport,Guid("00021401-0000-0000-C000-000000000046")] class ShellLink {}
        [ComImport,InterfaceType(ComInterfaceType.InterfaceIsIUnknown),Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW {
            void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,int max,IntPtr data,int flags);
            void GetIDList(out IntPtr list);
            void SetIDList(IntPtr list);
            void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder name,int max);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir,int max);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder args,int max);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short key);
            void SetHotkey(short key);
            void GetShowCmd(out int cmd);
            void SetShowCmd(int cmd);
            void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int max,out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path,int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path,int reserved);
            void Resolve(IntPtr hwnd,int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }
        static void CreateLink(string path) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var link=(IShellLinkW)new ShellLink();
            try {
                link.SetPath(InstalledExe);link.SetWorkingDirectory(Folder);link.SetIconLocation(InstalledExe,0);
                link.SetDescription("Gerenciador de downloads que continua de onde parou");
                if(AppPaths.Custom)link.SetArguments("--data \""+AppPaths.Root+"\"");
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(path,true);
            } finally { Marshal.ReleaseComObject(link); }
        }
        public static string LinkTarget(string path) {
            var link=(IShellLinkW)new ShellLink();
            try {
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(path,0);
                var target=new StringBuilder(1024);link.GetPath(target,target.Capacity,IntPtr.Zero,0);return target.ToString();
            } finally { Marshal.ReleaseComObject(link); }
        }
    }
}
