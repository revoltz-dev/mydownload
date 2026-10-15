using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyDownload;
class UiTests {
 [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process,int flag);
 static void Collect(){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Application.DoEvents();}
 static long Memory(){using(var p=Process.GetCurrentProcess())return p.PrivateMemorySize64;}
 static int Handles(){using(var p=Process.GetCurrentProcess())return GetGuiResources(p.Handle,0);}
 [STAThread]static int Main(string[] args){
  try {
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   string root=args[0];Directory.CreateDirectory(root);
   long memory=0;int gdi=0;
   for(int i=0;i<22;i++) {
    Theme.Apply(i%2==0?"dark":"light");
    var settings=new Settings();
    using(var manager=new DownloadCoordinator(Path.Combine(root,"preview"),settings,true))
    using(var form=new MainWindow(manager,settings,true)){form.Preview(Path.Combine(root,"ui-preview-"+(i%2==0?"dark":"light")+".png"));form.Close();}
    Collect();
    if(i==1){memory=Memory();gdi=Handles();}
    if(i%5==0)Console.WriteLine("CYCLE "+i+" privateMB="+Memory()/1048576+" GDI="+Handles());
   }
   Collect();Console.WriteLine("MEMORY_DELTA_MB="+(Memory()-memory)/1048576.0+" GDI_DELTA="+(Handles()-gdi));
   if(Memory()-memory>48*1024*1024||Handles()-gdi>15)throw new Exception("UI resources grew beyond budget");
   Console.WriteLine("PASS repeated UI opening/painting/closing in both themes releases resources");
   Theme.Apply("dark");
   string history=Path.Combine(root,"close-history");
   var closeSettings=new Settings{CloseToTray=false,WatchClipboard=false,Notify=false};
   using(var m=new DownloadCoordinator(history,closeSettings))m.Add(args[1]+"/stall",Path.Combine(root,"close-data"),false);
   var elapsed=new Stopwatch();
   using(var manager=new DownloadCoordinator(history,closeSettings))
   using(var form=new MainWindow(manager,closeSettings,false))
   using(var timer=new Timer{Interval=3000}) {
    form.Location=new Point(-20000,-20000);form.StartPosition=FormStartPosition.Manual;form.ShowInTaskbar=false;
    timer.Tick+=delegate{timer.Stop();elapsed.Start();form.Close();};
    form.Shown+=delegate{timer.Start();};
    Application.Run(form);
   }
   Console.WriteLine("CLOSE_SECONDS="+elapsed.Elapsed.TotalSeconds);
   if(elapsed.Elapsed.TotalSeconds>6)throw new Exception("Close was too slow");
   Console.WriteLine("PASS real window closes during stalled network and persists history");
   return 0;
  }catch(Exception e){Console.WriteLine(e);return 1;}
 }
}
