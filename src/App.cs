using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName=".NET Framework 4.8")]
[assembly: System.Reflection.AssemblyTitle("MyDownload")]
[assembly: System.Reflection.AssemblyProduct("MyDownload")]
[assembly: System.Reflection.AssemblyVersion("3.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("3.0.0.0")]
namespace MyDownload {
    sealed class TitleBar : Panel {
        public readonly FlatButton Mini,ThemeButton,SettingsButton,Minimize,Maximize,Close;
        public TitleBar() {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            Mini=Button(Icons.Mini,ButtonKind.Subtle,"Modo mini");ThemeButton=Button(Icons.Sun,ButtonKind.Subtle,"Tema");SettingsButton=Button(Icons.Settings,ButtonKind.Subtle,"Configurações");
            Minimize=Button(Icons.Minimize,ButtonKind.Chrome,"Minimizar");Maximize=Button(Icons.Maximize,ButtonKind.Chrome,"Maximizar");Close=Button(Icons.Close,ButtonKind.Close,"Fechar");
        }
        FlatButton Button(string glyph,ButtonKind kind,string name) {
            var b=new FlatButton{Glyph=glyph,Kind=kind,GlyphSize=kind==ButtonKind.Subtle?15:10,TabStop=false,AccessibleName=name};
            Controls.Add(b);return b;
        }
        protected override void OnLayout(LayoutEventArgs e) {
            if(Close==null){base.OnLayout(e);return;}
            int w=Theme.S(46),x=Width;
            foreach(var b in new[]{Close,Maximize,Minimize}){x-=w;b.SetBounds(x,0,w,Height);}
            x-=Theme.S(10);int s=Theme.S(34),y=(Height-s)/2;
            foreach(var b in new[]{SettingsButton,ThemeButton,Mini}){x-=s;b.SetBounds(x,y,s,s);x-=Theme.S(2);}
            base.OnLayout(e);
        }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;g.Clear(BackColor);
            Icons.Draw(g,Icons.Download,new Rectangle(Theme.S(16),0,Theme.S(18),Height),15,Theme.C.Accent);
            Theme.Text(g,AppPaths.Name,Theme.Ui(9,true),new Rectangle(Theme.S(42),0,Theme.S(260),Height),Theme.C.Text);
        }
        // Empty areas belong to the window, so Windows handles dragging and double-click.
        protected override void WndProc(ref Message m){if(m.Msg==0x84){m.Result=(IntPtr)(-1);return;}base.WndProc(ref m);}
    }

    sealed class Pills : Painted {
        public string[] Labels=new string[0];int[] counts=new int[0];int selected,hot=-1;Rectangle[] boxes=new Rectangle[0];
        public event Action<int> Picked;
        public Pills(){Cursor=Cursors.Hand;Font=Theme.Ui(9);}
        public int Selected{get{return selected;}set{if(selected!=value){selected=value;Invalidate();}}}
        public void SetCounts(int[] value){if(!value.SequenceEqual(counts)){counts=value;Invalidate();}}
        protected override void OnMouseMove(MouseEventArgs e){int now=Array.FindIndex(boxes,b=>b.Contains(e.Location));if(now!=hot){hot=now;Invalidate();}base.OnMouseMove(e);}
        protected override void OnMouseLeave(EventArgs e){hot=-1;base.OnMouseLeave(e);}
        protected override void OnMouseClick(MouseEventArgs e){int i=Array.FindIndex(boxes,b=>b.Contains(e.Location));if(i>=0){Selected=i;var h=Picked;if(h!=null)h(i);}base.OnMouseClick(e);}
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;g.Clear(Back);
            boxes=new Rectangle[Labels.Length];int x=0;var bold=Theme.Ui(9,true);
            for(int i=0;i<Labels.Length;i++) {
                string count=i<counts.Length?counts[i].ToString():"";
                var font=i==selected?bold:Font;
                int lw=Theme.Measure(Labels[i],font),cw=Theme.Measure(count,Font);
                var box=new Rectangle(x,0,lw+cw+Theme.S(30),Height);boxes[i]=box;
                if(i==selected){Theme.Fill(g,box,c.Card,box.Height/2f);Theme.Stroke(g,box,c.Line,box.Height/2f);}
                else if(i==hot)Theme.Fill(g,box,c.Hover,box.Height/2f);
                Theme.Text(g,Labels[i],font,new Rectangle(box.X+Theme.S(13),0,lw+Theme.S(2),Height),i==selected?c.Text:c.Muted);
                Theme.Text(g,count,Font,new Rectangle(box.X+Theme.S(17)+lw,0,cw+Theme.S(4),Height),c.Faint);
                x=box.Right+Theme.S(4);
            }
        }
    }

    // Replaces the filter row while two or more downloads are selected.
    sealed class SelectionBar : CardPanel {
        public readonly FlatButton Pause,Resume,Options,Copy,Remove,All,Clear;
        public int Count;
        public SelectionBar() {
            Pause=Button("Pausar",Icons.Pause);Resume=Button("Retomar",Icons.Play);Options=Button("Opções",Icons.Settings);
            Copy=Button("Copiar links",Icons.Copy);Remove=Button("Remover",Icons.Delete);All=Button("Selecionar todos",null);Clear=Button(null,Icons.Cancel);
        }
        FlatButton Button(string text,string glyph){var b=new FlatButton{Text=text,Glyph=glyph,Kind=ButtonKind.Subtle,GlyphSize=13,TabStop=false};Controls.Add(b);return b;}
        string Label{get{return Count+" selecionados";}}
        protected override void OnLayout(LayoutEventArgs e) {
            if(Clear==null){base.OnLayout(e);return;}
            int h=Theme.S(30),y=(Height-h)/2,x=Theme.S(16)+Theme.Measure(Label,Theme.Ui(9.5f,true))+Theme.S(16);
            foreach(var b in new[]{Pause,Resume,Options,Copy,Remove}){b.SetBounds(x,y,b.Preferred(10),h);x=b.Right+Theme.S(2);}
            Clear.SetBounds(Width-Theme.S(6)-h,y,h,h);
            All.SetBounds(Clear.Left-Theme.S(2)-All.Preferred(10),y,All.Preferred(10),h);
            base.OnLayout(e);
        }
        protected override void OnPaint(PaintEventArgs e) {
            Theme.Text(e.Graphics,Label,Theme.Ui(9.5f,true),new Rectangle(Theme.S(16),0,Theme.S(160),Height),Theme.C.Text);
            base.OnPaint(e);
        }
    }

    sealed class StatusBar : Painted {
        string left="",right="";Color rightColor;
        public StatusBar(){Font=Theme.Ui(8.5f);}
        public void Set(string l,string r,Color color){if(l==left&&r==right&&color==rightColor)return;left=l;right=r;rightColor=color;Invalidate();}
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;g.Clear(Back);
            using(var p=new Pen(c.Line))g.DrawLine(p,0,0,Width,0);
            int m=Theme.S(28),rw=Math.Min(Theme.Measure(right,Font)+Theme.S(4),Width/2);
            Theme.Text(g,left,Font,new Rectangle(m,1,Width-2*m-rw-Theme.S(16),Height-1),c.Muted);
            Theme.Text(g,right,Font,new Rectangle(Width-m-rw,1,rw,Height-1),rightColor==Color.Empty?c.Muted:rightColor,TextFormatFlags.Right);
        }
    }

    // Total progress on the taskbar button.
    static class TaskbarProgress {
        [ComImport,Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITaskbarList3 {
            void HrInit();void AddTab(IntPtr hwnd);void DeleteTab(IntPtr hwnd);void ActivateTab(IntPtr hwnd);void SetActiveAlt(IntPtr hwnd);
            void MarkFullscreenWindow(IntPtr hwnd,[MarshalAs(UnmanagedType.Bool)] bool fullscreen);
            void SetProgressValue(IntPtr hwnd,ulong completed,ulong total);
            void SetProgressState(IntPtr hwnd,int state);
        }
        [ComImport,Guid("56FDF344-FD6D-11d0-958A-006097C9A090"),ClassInterface(ClassInterfaceType.None)] class TaskbarList {}
        static ITaskbarList3 list;static bool failed;static int lastState=-1;static ulong lastValue=ulong.MaxValue;
        public const int None=0,Indeterminate=1,Normal=2;
        public static void Set(IntPtr hwnd,int state,double fraction) {
            if(failed||hwnd==IntPtr.Zero)return;
            try {
                if(list==null){list=(ITaskbarList3)new TaskbarList();list.HrInit();}
                if(state!=lastState){list.SetProgressState(hwnd,state);lastState=state;lastValue=ulong.MaxValue;}
                if(state==Normal){ulong value=(ulong)(Math.Max(0,Math.Min(1,fraction))*1000);if(value!=lastValue){list.SetProgressValue(hwnd,value,1000);lastValue=value;}}
            } catch(Exception){failed=true;}
        }
    }

    sealed class MainWindow : Form {
        [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd,int flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,ref Margins margins);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        [StructLayout(LayoutKind.Sequential)] struct Margins{public int Left,Right,Top,Bottom;}
        [StructLayout(LayoutKind.Sequential)] struct NativeRect{public int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)] struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public int Flags;}
        [StructLayout(LayoutKind.Sequential)] struct MinMaxInfo{public Point Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize;}

        static readonly string[] FilterNames={"Todos","Baixando","Concluídos","Atenção"};
        readonly DownloadCoordinator manager;readonly Settings settings;readonly bool preview;
        readonly Tips tips=new Tips();
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=500},bannerTimer=new System.Windows.Forms.Timer{Interval=6000};
        readonly TitleBar title;readonly CardPanel command,searchField;readonly PasteBox link,search;readonly FlatButton folderChip,add,sortButton;
        readonly Pills pills;readonly DownloadList list;readonly Banner banner;readonly StatusBar status;
        readonly ContextMenuStrip rowMenu,folderMenu,sortMenu,trayMenu;
        NotifyIcon tray;Icon trayIcon;MiniWindow mini;
        int filter;bool exiting,closing,started,disposed;DateTime closeStarted;
        bool shutdownArmed;int completedSinceArmed;
        DateTime ignoreClipboardUntil;string lastOffered;Action balloonAction;
        readonly SelectionBar selectionBar;
        readonly List<Incoming> waiting=new List<Incoming>();bool asking;
        DateTime? sleepAt;
        public bool StartHidden,ExtensionUpdated,JustInstalled;
        public readonly List<Incoming> PendingIncoming=new List<Incoming>();

        public MainWindow(DownloadCoordinator manager,Settings settings,bool isPreview) {
            this.manager=manager;this.settings=settings;preview=isPreview;
            Text=Program.WindowTitle;Font=Theme.Ui(9.5f);AutoScaleMode=AutoScaleMode.None;FormBorderStyle=FormBorderStyle.None;DoubleBuffered=true;KeyPreview=true;
            MinimumSize=new Size(Theme.S(720),Theme.S(520));Size=new Size(Theme.S(980),Theme.S(720));StartPosition=FormStartPosition.CenterScreen;Icon=Program.AppIcon;

            title=new TitleBar();Controls.Add(title);
            title.Minimize.Click+=delegate{WindowState=FormWindowState.Minimized;};
            title.Maximize.Click+=delegate{WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;};
            title.Close.Click+=delegate{Close();};
            title.SettingsButton.Click+=delegate{OpenSettings();};
            title.ThemeButton.Click+=delegate{settings.Theme=Theme.C.Dark?"light":"dark";SaveSettings();Theme.Apply(settings.Theme);};
            title.Mini.Click+=delegate{ShowMini();};
            tips.SetToolTip(title.SettingsButton,"Configurações (Ctrl+,)");tips.SetToolTip(title.Mini,"Modo mini: uma janelinha por cima das outras");

            command=new CardPanel{Underline=true};Controls.Add(command);
            link=new PasteBox{Cue="Cole um link para baixar — ou vários de uma vez"};command.Controls.Add(link);command.FocusSource=link;
            link.GotFocus+=delegate{command.Invalidate();};link.LostFocus+=delegate{command.Invalidate();};
            link.KeyDown+=(sender,e)=>{if(e.KeyCode==Keys.Enter){AddFromBox();e.SuppressKeyPress=true;}};
            folderChip=new FlatButton{Kind=ButtonKind.Outline,Glyph=Icons.Folder,GlyphSize=13};command.Controls.Add(folderChip);
            folderChip.Click+=delegate{ShowFolderMenu();};
            add=new FlatButton{Kind=ButtonKind.Primary,Text="Baixar",Glyph=Icons.Download,GlyphSize=13,Font=Theme.Ui(9.5f,true)};command.Controls.Add(add);
            add.Click+=delegate{AddFromBox();};tips.SetToolTip(add,"Baixar (Enter)");
            command.Paint+=(sender,e)=>Icons.Draw(e.Graphics,Icons.Link,new Rectangle(Theme.S(14),0,Theme.S(22),command.Height),17,Theme.C.Accent);
            command.Layout+=delegate{LayoutCommand();};

            pills=new Pills{Labels=FilterNames};Controls.Add(pills);pills.Picked+=i=>{filter=i;RefreshList();};
            searchField=new CardPanel();Controls.Add(searchField);
            search=new PasteBox{Cue="Buscar",Font=Theme.Ui(9.5f)};searchField.Controls.Add(search);searchField.FocusSource=search;
            search.TextChanged+=delegate{RefreshList();};search.GotFocus+=delegate{searchField.Invalidate();};search.LostFocus+=delegate{searchField.Invalidate();};
            searchField.Paint+=(sender,e)=>Icons.Draw(e.Graphics,Icons.Search,new Rectangle(Theme.S(9),0,Theme.S(18),searchField.Height),13,Theme.C.Muted);
            searchField.Layout+=delegate{search.SetBounds(Theme.S(32),(searchField.Height-search.Height)/2,Math.Max(10,searchField.Width-Theme.S(40)),search.Height);};
            sortButton=new FlatButton{Kind=ButtonKind.Subtle,Glyph=Icons.Sort,GlyphSize=13};Controls.Add(sortButton);
            sortButton.Click+=delegate{ShowSortMenu();};

            list=new DownloadList{Tips=tips};Controls.Add(list);
            list.ActionClicked+=OnRowAction;list.MenuRequested+=ShowRowMenu;list.Activated+=item=>Primary(item);list.Command+=OnListCommand;
            list.SelectionChanged+=delegate{UpdateSelectionBar();};
            selectionBar=new SelectionBar{Visible=false};Controls.Add(selectionBar);selectionBar.BringToFront();
            selectionBar.Pause.Click+=delegate{foreach(var i in list.Selected)if(i.Active||i.Phase=="Queued")manager.Pause(i);RefreshAll();};
            selectionBar.Resume.Click+=delegate{foreach(var i in list.Selected.Where(Resumable))manager.Start(i);RefreshAll();};
            selectionBar.Options.Click+=delegate{EditOptions(list.Selected);};
            selectionBar.Copy.Click+=delegate{var s=list.Selected;CopyText(String.Join(Environment.NewLine,s.Select(i=>i.Job.Url)),s.Count+" links copiados.");};
            selectionBar.Remove.Click+=delegate{Remove(list.Selected,false);};
            selectionBar.All.Click+=delegate{list.SelectAll();};
            selectionBar.Clear.Click+=delegate{list.ClearSelection();};
            banner=new Banner{Visible=false};Controls.Add(banner);banner.VisibleChanged+=delegate{LayoutAll();};
            status=new StatusBar();Controls.Add(status);

            rowMenu=Menus.Create();folderMenu=Menus.Create();sortMenu=Menus.Create();trayMenu=Menus.Create();
            bannerTimer.Tick+=delegate{bannerTimer.Stop();banner.Visible=false;};
            timer.Tick+=delegate{Tick();};
            if(!preview) {
                foreach(Control c in new Control[]{this,title,command,link,pills,list,status}){c.AllowDrop=true;c.DragEnter+=OnDragEnter;c.DragDrop+=OnDragDrop;}
                CreateTray();
            }
            RestoreWindow();
            Theme.Changed+=OnTheme;ApplyTheme();NoFlash.Attach(this);Shown+=delegate{Activate();};
            FormClosing+=OnClosing;
            if(preview)SeedPreview();
            LayoutAll();RefreshAll();
        }

        // ---- window chrome -----------------------------------------------------------------
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.Style|=0x20000|0x10000|0x80000;return p;}}
        protected override void SetVisibleCore(bool value) {
            if(StartHidden&&value){if(!IsHandleCreated)CreateHandle();value=false;}
            base.SetVisibleCore(value);
        }
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            Theme.Rounded(Handle);
            try{int policy=2;DwmSetWindowAttribute(Handle,2,ref policy,4);var margins=new Margins{Bottom=1};DwmExtendFrameIntoClientArea(Handle,ref margins);}catch(Exception){}
            if(!preview)AddClipboardFormatListener(Handle);
            if(!started){started=true;BeginInvoke((Action)Begin);}
        }
        protected override void OnHandleDestroyed(EventArgs e){if(!preview)RemoveClipboardFormatListener(Handle);base.OnHandleDestroyed(e);}
        void Begin() {
            timer.Start();
            if(!preview)OfferInstall();
            if(ExtensionUpdated)ShowBanner(BannerKind.Info,"A extensão do Chrome foi atualizada. Em chrome://extensions, clique em ↻ na extensão MyDownload (ou feche e abra o Chrome).",null,null,0);
            if(!preview){manager.RestoreDownloads();if(PendingIncoming.Count>0){var items=PendingIncoming.ToList();PendingIncoming.Clear();ShowMain();Receive(items);}}
            RefreshAll();
        }
        protected override void WndProc(ref Message m) {
            if(m.Msg==0x24){base.WndProc(ref m);FitMaximized(m.LParam);return;}
            if(m.Msg==0x31D){base.WndProc(ref m);OnClipboard();return;}
            if(m.Msg==0x4A){ReceiveLinks(m.LParam);m.Result=(IntPtr)1;return;}
            base.WndProc(ref m);
            if(m.Msg==0x84&&m.Result==(IntPtr)1) {
                long packed=m.LParam.ToInt64();var p=PointToClient(new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff)));
                int edge=Theme.S(6);
                if(WindowState!=FormWindowState.Maximized) {
                    bool l=p.X<edge,r=p.X>=ClientSize.Width-edge,t=p.Y<edge,b=p.Y>=ClientSize.Height-edge;
                    if(l&&t)m.Result=(IntPtr)13;else if(r&&t)m.Result=(IntPtr)14;else if(l&&b)m.Result=(IntPtr)16;else if(r&&b)m.Result=(IntPtr)17;
                    else if(l)m.Result=(IntPtr)10;else if(r)m.Result=(IntPtr)11;else if(t)m.Result=(IntPtr)12;else if(b)m.Result=(IntPtr)15;
                }
                if(m.Result==(IntPtr)1&&p.Y<title.Height)m.Result=(IntPtr)2;
            }
        }
        // A borderless window would cover the taskbar when maximized; limit it to the work area.
        void FitMaximized(IntPtr lParam) {
            var info=(MinMaxInfo)Marshal.PtrToStructure(lParam,typeof(MinMaxInfo));
            var monitor=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};
            if(!GetMonitorInfo(MonitorFromWindow(Handle,2),ref monitor))return;
            info.MaxPosition=new Point(monitor.Work.Left-monitor.Monitor.Left,monitor.Work.Top-monitor.Monitor.Top);
            info.MaxSize=new Point(monitor.Work.Right-monitor.Work.Left,monitor.Work.Bottom-monitor.Work.Top);
            info.MinTrackSize=new Point(MinimumSize.Width,MinimumSize.Height);
            Marshal.StructureToPtr(info,lParam,true);
        }
        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            if(title==null)return;
            title.Maximize.Glyph=WindowState==FormWindowState.Maximized?Icons.Restore:Icons.Maximize;title.Maximize.Invalidate();
            LayoutAll();
        }
        void LayoutAll() {
            if(status==null)return;
            int w=ClientSize.Width,h=ClientSize.Height,m=Theme.S(28);
            title.SetBounds(0,0,w,Theme.S(40));
            command.SetBounds(m,Theme.S(44),w-2*m,Theme.S(50));
            int y=Theme.S(110),row=Theme.S(32);
            sortButton.Text=SortName();int sw=sortButton.Preferred(10);
            sortButton.SetBounds(w-m-sw,y,sw,row);
            int searchWidth=Theme.S(220);searchField.SetBounds(sortButton.Left-Theme.S(8)-searchWidth,y,searchWidth,row);
            pills.SetBounds(m,y,Math.Max(Theme.S(100),searchField.Left-Theme.S(12)-m),row);
            if(selectionBar!=null)selectionBar.SetBounds(m,y-Theme.S(2),w-2*m,row+Theme.S(4));
            int bottom=h-Theme.S(30);status.SetBounds(0,bottom,w,Theme.S(30));
            if(banner.Visible){banner.SetBounds(m,bottom-Theme.S(54),w-2*m,Theme.S(46));bottom-=Theme.S(62);}
            int top=y+row+Theme.S(8);
            list.SetBounds(0,top,w,Math.Max(0,bottom-top));
        }
        void LayoutCommand() {
            if(add==null)return;
            int h=command.Height,bh=Theme.S(34);
            add.Size=new Size(add.Preferred(16),bh);add.Location=new Point(command.Width-Theme.S(8)-add.Width,(h-bh)/2);
            folderChip.Text=FolderName(settings.Folder);tips.SetToolTip(folderChip,"Salvar em "+settings.Folder);
            int fw=Math.Min(Theme.S(240),folderChip.Preferred(12));
            folderChip.SetBounds(add.Left-Theme.S(8)-fw,(h-Theme.S(30))/2,fw,Theme.S(30));
            link.SetBounds(Theme.S(44),(h-link.Height)/2,Math.Max(Theme.S(60),folderChip.Left-Theme.S(56)),link.Height);
        }
        void OnTheme(object sender,EventArgs e){ApplyTheme();}
        void ApplyTheme() {
            var c=Theme.C;BackColor=c.Bg;title.BackColor=c.Bg;selectionBar.BackColor=Theme.Blend(c.Card,c.Accent,c.Dark?.12f:.07f);
            command.BackColor=c.Card;link.BackColor=c.Card;link.ForeColor=c.Text;
            searchField.BackColor=c.Card;search.BackColor=c.Card;search.ForeColor=c.Text;
            title.ThemeButton.Glyph=c.Dark?Icons.Sun:Icons.Moon;tips.SetToolTip(title.ThemeButton,c.Dark?"Usar tema claro":"Usar tema escuro");
            Invalidate(true);
        }
        void RestoreWindow() {
            var w=settings.Window;if(w==null||w.Length!=5)return;
            var bounds=new Rectangle(w[0],w[1],Math.Max(MinimumSize.Width,w[2]),Math.Max(MinimumSize.Height,w[3]));
            if(!Screen.AllScreens.Any(s=>s.WorkingArea.IntersectsWith(bounds)))return;
            StartPosition=FormStartPosition.Manual;Bounds=bounds;if(w[4]==1)WindowState=FormWindowState.Maximized;
        }
        void SaveWindow() {
            if(preview)return;
            var b=WindowState==FormWindowState.Normal?Bounds:RestoreBounds;
            settings.Window=new[]{b.X,b.Y,b.Width,b.Height,WindowState==FormWindowState.Maximized?1:0};SaveSettings();
        }
        void SaveSettings(){if(!preview)settings.Save(AppPaths.SettingsFile);}
        static string FolderName(string path){string name=Path.GetFileName((path??"").TrimEnd('\\'));return String.IsNullOrEmpty(name)?path:name;}
        string SortName(){return settings.Sort=="name"?"Nome":settings.Sort=="size"?"Tamanho":"Mais recentes";}

        // ---- refreshing -------------------------------------------------------------------
        void Tick() {
            foreach(var e in manager.Tick())OnEvent(e);
            if(!String.IsNullOrEmpty(manager.Warning)){ShowBanner(BannerKind.Warning,manager.Warning,null,null,8000);manager.Warning=null;}
            RefreshList();UpdateStatus();UpdateTaskbar();UpdateTray();
            if(mini!=null&&mini.Visible)mini.RefreshData();
            CheckShutdown();
            if(sleepAt.HasValue&&DateTime.UtcNow>=sleepAt.Value){sleepAt=null;banner.Visible=false;SetSuspendState(false,false,false);}
            if(closing&&(manager.ActiveCount==0||(DateTime.UtcNow-closeStarted).TotalSeconds>=10))Close();
        }
        void RefreshAll(){RefreshList();UpdateStatus();}
        IEnumerable<DownloadItem> Sorted(IEnumerable<DownloadItem> items,bool pending) {
            if(settings.Sort=="name")return items.OrderBy(i=>i.Title,StringComparer.CurrentCultureIgnoreCase);
            if(settings.Sort=="size")return items.OrderByDescending(i=>i.Job.Total);
            if(pending)return items.OrderBy(Describe.Order).ThenByDescending(i=>Describe.Order(i)==1?-i.When.Ticks:i.When.Ticks);
            return items.OrderByDescending(i=>i.When);
        }
        void RefreshList() {
            var all=manager.Visible.ToList();
            pills.SetCounts(new[]{all.Count,all.Count(Describe.InProgress),all.Count(i=>i.Phase=="Completed"),all.Count(Describe.Attention)});
            pills.Selected=filter;
            string q=search.Text.Trim();
            var shown=all.Where(i=>(filter==0||filter==1&&Describe.InProgress(i)||filter==2&&i.Phase=="Completed"||filter==3&&Describe.Attention(i))
                &&(q.Length==0||(i.Title+" "+i.Host).IndexOf(q,StringComparison.CurrentCultureIgnoreCase)>=0)).ToList();
            var rows=new List<ListRow>();
            var now=Sorted(shown.Where(i=>i.Phase!="Completed"),true).ToList();
            if(now.Count>0){rows.Add(new ListRow{Header="Agora"});rows.AddRange(now.Select(i=>new ListRow{Item=i}));}
            var done=Sorted(shown.Where(i=>i.Phase=="Completed"),false).ToList();
            foreach(string group in new[]{"Hoje","Ontem","Últimos 7 dias","Mais antigos"}) {
                var part=done.Where(i=>Describe.Group(i)==group).ToList();if(part.Count==0)continue;
                rows.Add(new ListRow{Header=group});rows.AddRange(part.Select(i=>new ListRow{Item=i}));
            }
            if(all.Count==0){list.EmptyTitle="Cole um link acima para começar";list.EmptyText="Ou arraste para esta janela um link ou um arquivo .txt com vários links.";}
            else if(q.Length>0){list.EmptyTitle="Nada encontrado";list.EmptyText="Nenhum download com “"+q+"” neste filtro.";}
            else{list.EmptyTitle="Nada por aqui";list.EmptyText=filter==3?"Nenhum download precisa de atenção.":"Nenhum download neste filtro.";}
            list.SetRows(rows);
        }
        void UpdateStatus() {
            int active=manager.ActiveCount,queued=manager.QueuedCount;
            string left=active>0?active+" baixando · "+Units.Size(manager.TotalRate)+"/s no total"+(queued>0?" · "+queued+" na fila":""):queued>0?queued+" na fila":"Nenhum download em andamento";
            string right;Color color=Color.Empty;
            if(closing)right="Salvando o progresso antes de fechar…";
            else if(shutdownArmed){right="O computador desliga quando tudo terminar";color=Theme.C.Warn;}
            else if(settings.SpeedLimit>0)right="Limite de velocidade: "+Units.Size(settings.SpeedLimit)+"/s";
            else if(preview)right="Prévia com dados de exemplo";
            else right="Retoma sozinho se a internet cair";
            status.Set(left,right,color);
        }
        void UpdateTaskbar() {
            if(preview||!IsHandleCreated)return;
            var active=manager.Visible.Where(i=>i.Active&&i.Job.Total>0).ToList();
            if(active.Count>0)TaskbarProgress.Set(Handle,TaskbarProgress.Normal,active.Sum(i=>(double)Math.Min(i.Bytes,i.Job.Total))/active.Sum(i=>(double)i.Job.Total));
            else TaskbarProgress.Set(Handle,manager.ActiveCount>0?TaskbarProgress.Indeterminate:TaskbarProgress.None,0);
        }
        void UpdateTray() {
            if(tray==null)return;
            int active=manager.ActiveCount;
            string text=active>0?AppPaths.Name+" — "+active+" baixando · "+Units.Size(manager.TotalRate)+"/s":AppPaths.Name;
            if(text.Length>63)text=text.Substring(0,63);
            if(tray.Text!=text)tray.Text=text;
        }
        void OnEvent(CoordinatorEvent e) {
            var item=e.Item;bool away=!Visible||WindowState==FormWindowState.Minimized||Form.ActiveForm==null;
            switch(e.Kind){
                case "Completed":
                    if(shutdownArmed)completedSinceArmed++;
                    switch(item.Job.AfterDone){
                        case "open":OpenFile(item);break;
                        case "folder":ShowInFolder(item);break;
                        case "shutdown":PowerAction(false,"“"+item.Title+"” terminou.");return;
                        case "sleep":PowerAction(true,"“"+item.Title+"” terminou.");return;
                    }
                    if(settings.Notify&&away)Balloon("Download concluído",item.Title,()=>{ShowMain();list.Select(item);});
                    break;
                case "Failed":
                    if(settings.Notify&&away)Balloon("Não foi possível baixar",item.Title+"\n"+item.Detail,()=>{ShowMain();list.Select(item);});
                    if(Describe.Expired(item))ShowBanner(BannerKind.Error,item.Title+": o link expirou ou foi recusado.","Atualizar link",()=>UpdateLink(item),0);
                    else ShowBanner(BannerKind.Error,item.Title+": "+item.Detail,"Tentar de novo",()=>{manager.Start(item);RefreshAll();},0);
                    break;
                case "HashMismatch":ShowBanner(BannerKind.Error,"O SHA-256 de "+item.Title+" não confere com o informado. O arquivo pode estar corrompido ou ser outro.",null,null,0);break;
                case "HashMatch":ShowBanner(BannerKind.Success,"O SHA-256 de "+item.Title+" confere com o informado.",null,null,6000);break;
            }
        }
        void ShowBanner(BannerKind kind,string text,string primary,Action action,int autoHide,string secondary=null,Action secondaryAction=null) {
            banner.Kind=kind;banner.Message=text;banner.Primary=primary;banner.Secondary=secondary;
            banner.PrimaryAction=action==null?null:(Action)(()=>{banner.Visible=false;action();});
            banner.SecondaryAction=secondaryAction==null?null:(Action)(()=>{banner.Visible=false;secondaryAction();});
            bannerTimer.Stop();banner.Visible=true;banner.Invalidate();
            if(autoHide>0){bannerTimer.Interval=autoHide;bannerTimer.Start();}
        }
        void Balloon(string heading,string text,Action action) {
            if(tray==null)return;
            balloonAction=action;tray.ShowBalloonTip(5000,heading,text,ToolTipIcon.None);
        }

        // ---- adding --------------------------------------------------------------------------
        void AddFromBox() {
            var links=Links.Extract(link.Text);
            if(links.Count==0) {
                if(link.Text.Trim().Length>0)ShowBanner(BannerKind.Error,"Cole um link direto que comece com http:// ou https://.",null,null,6000);
                link.Focus();return;
            }
            link.Text="";AddLinks(links);
        }
        void AddLinks(List<string> links){Receive(links.Select(url=>new Incoming{Url=url}).ToList());}
        // Every new download passes here: from the browser, the link bar, pasting, dragging or a
        // second launch. Asks where to save (one window at a time) unless the user turned that off.
        void Receive(List<Incoming> items) {
            if(items.Count==0)return;
            waiting.AddRange(items);
            if(asking)return;
            while(waiting.Count>0) {
                var batch=waiting.ToList();waiting.Clear();
                if(!settings.AskFolder||preview){AddIncoming(batch,new DownloadChoice{Folder=settings.Folder});continue;}
                asking=true;DownloadChoice choice;
                try{choice=DownloadDialog.Ask(this,settings,batch);}finally{asking=false;}
                if(choice==null)continue;
                if(choice.Remember){settings.Folder=choice.Folder;settings.AskFolder=false;SaveSettings();LayoutCommand();command.Invalidate();}
                AddIncoming(batch,choice);
            }
        }
        void AddIncoming(List<Incoming> items,DownloadChoice choice) {
            string folder=choice.Folder;int added=0;var errors=new List<string>();DownloadItem first=null;
            foreach(var incoming in items) {
                var source=incoming;
                try {
                    var item=manager.Add(source.Url,folder,true,job=>{
                        if(items.Count==1&&choice.Name!=null)job.Name=choice.Name;
                        if(source.Referrer!=null)job.Referrer=source.Referrer;
                        job.AfterDone=choice.AfterDone;job.Connections=choice.Connections;job.SpeedLimit=choice.SpeedLimit;
                    });
                    if(first==null)first=item;added++;
                } catch(Exception e){errors.Add(e.Message);}
            }
            if(added>0){settings.Remember(folder);SaveSettings();}
            filter=0;search.Text="";RefreshAll();if(first!=null)list.Select(first);
            if(errors.Count>0)ShowBanner(BannerKind.Error,(added>0?added+" adicionado(s). ":"")+(errors.Count==1?"Um link não foi adicionado: ":errors.Count+" links não foram adicionados: ")+errors[0],null,null,0);
            else if(added>1||!settings.AskFolder)ShowBanner(BannerKind.Success,(added==1?"Baixando “"+first.Title+"”":added+" downloads adicionados")+" em "+FolderName(folder)+".",null,null,5000);
        }
        void PasteLinks() {
            var links=new List<string>();
            try {
                if(Clipboard.ContainsFileDropList())links.AddRange(Links.FromFiles(Clipboard.GetFileDropList().Cast<string>()));
                if(Clipboard.ContainsText())links.AddRange(Links.Extract(Clipboard.GetText()));
            } catch(ExternalException){}
            links=links.Distinct().ToList();
            if(links.Count==0){ShowBanner(BannerKind.Warning,"Nenhum link na área de transferência.",null,null,4000);return;}
            AddLinks(links);
        }
        void OnDragEnter(object sender,DragEventArgs e) {
            bool ok=e.Data.GetDataPresent(DataFormats.FileDrop)||e.Data.GetDataPresent(DataFormats.UnicodeText)||e.Data.GetDataPresent(DataFormats.Text);
            e.Effect=ok?DragDropEffects.Copy:DragDropEffects.None;
        }
        void OnDragDrop(object sender,DragEventArgs e) {
            var links=new List<string>();
            if(e.Data.GetDataPresent(DataFormats.FileDrop))links.AddRange(Links.FromFiles((string[])e.Data.GetData(DataFormats.FileDrop)));
            if(e.Data.GetDataPresent(DataFormats.UnicodeText))links.AddRange(Links.Extract((string)e.Data.GetData(DataFormats.UnicodeText)));
            else if(e.Data.GetDataPresent(DataFormats.Text))links.AddRange(Links.Extract((string)e.Data.GetData(DataFormats.Text)));
            links=links.Distinct().ToList();
            if(links.Count==0){ShowBanner(BannerKind.Warning,"Nenhum link encontrado no que foi arrastado.",null,null,5000);return;}
            Activate();AddLinks(links);
        }
        void OnClipboard() {
            if(preview||!settings.WatchClipboard||DateTime.UtcNow<ignoreClipboardUntil)return;
            string text=null;
            try{if(Clipboard.ContainsText())text=Clipboard.GetText();}catch(ExternalException){return;}
            if(String.IsNullOrWhiteSpace(text))return;text=text.Trim();
            if(text.Length>2048||text.IndexOfAny(new[]{' ','\n','\r','\t'})>=0)return;
            Uri uri;
            if(!Uri.TryCreate(text,UriKind.Absolute,out uri)||(uri.Scheme!="http"&&uri.Scheme!="https")||!Categories.LooksLikeFile(uri))return;
            if(text==lastOffered||manager.Visible.Any(i=>i.Job.Url==uri.AbsoluteUri))return;
            lastOffered=text;
            string name=Uri.UnescapeDataString(uri.AbsolutePath.Split('/').Last());
            Action download=()=>AddLinks(new List<string>{text});
            ShowBanner(BannerKind.Info,"Link copiado: "+name+" · "+uri.Host,"Baixar",download,20000,"Ignorar",()=>{});
            if(!Visible&&settings.Notify)Balloon("Link copiado","Clique para baixar "+name,()=>{ShowMain();download();});
        }
        // From a second launch of the exe ("show") or the browser extension ("browser"): both bring
        // the window to the front and go through Receive.
        void ReceiveLinks(IntPtr lParam) {
            var data=(Program.CopyData)Marshal.PtrToStructure(lParam,typeof(Program.CopyData));
            if(data.Data!=(IntPtr)Program.CopyMagic||data.Pointer==IntPtr.Zero)return;
            string payload=Marshal.PtrToStringUni(data.Pointer,data.Size/2).TrimEnd('\0');
            var items=payload.Split('\n').Skip(1).Select(Incoming.Parse).Where(i=>i!=null).ToList();
            BeginInvoke((Action)(()=>{ShowMain();Receive(items);}));
        }

        // ---- actions -------------------------------------------------------------------------
        static bool Resumable(DownloadItem i){return !i.Active&&!i.Replacing&&(i.Phase=="Paused"||i.Phase=="New"||i.Phase=="Error"||i.Phase=="Missing"||i.Phase=="Changed");}
        void Primary(DownloadItem item) {
            switch(item.Phase){
                case "Downloading":case "Connecting":case "Queued":manager.Pause(item);break;
                case "Finalizing":break;
                case "Completed":OpenFile(item);break;
                case "Missing":case "Changed":manager.Start(item);break;
                default:if(Describe.Expired(item))UpdateLink(item);else manager.Start(item);break;
            }
            RefreshAll();
        }
        string PrimaryLabel(DownloadItem item) {
            switch(item.Phase){
                case "Downloading":case "Connecting":case "Queued":return "Pausar";
                case "Completed":return Categories.IsExecutable(item.Title)?"Mostrar na pasta":"Abrir";
                case "Missing":case "Changed":return "Baixar novamente";
                default:return Describe.Expired(item)?"Atualizar link…":"Retomar";
            }
        }
        void OnRowAction(DownloadItem item,RowAction action,Point screen) {
            switch(action){
                case RowAction.Primary:Primary(item);break;
                case RowAction.Folder:ShowInFolder(item);break;
                case RowAction.More:if(!list.Selected.Contains(item))list.Select(item);ShowRowMenu(screen);break;
            }
        }
        void OnListCommand(Keys keys) {
            var items=list.Selected;if(items.Count==0)return;
            switch(keys&Keys.KeyCode){
                case Keys.Space:
                    if(items.Any(i=>i.Active||i.Phase=="Queued")){foreach(var i in items)if(i.Active||i.Phase=="Queued")manager.Pause(i);}
                    else foreach(var i in items.Where(Resumable))manager.Start(i);
                    RefreshAll();break;
                case Keys.Enter:if(items.Count==1)Primary(items[0]);break;
                case Keys.Delete:Remove(items,false);break;
                case Keys.C:if((keys&Keys.Control)!=0)CopyText(String.Join(Environment.NewLine,items.Select(i=>i.Job.Url)),items.Count==1?"Link copiado.":items.Count+" links copiados.");break;
            }
        }
        void ShowRowMenu(Point screen) {
            var items=list.Selected;if(items.Count==0)return;
            Menus.Clear(rowMenu);
            if(items.Count==1) {
                var item=items[0];
                if(Describe.Primary(item)!=null)Menus.Item(rowMenu,PrimaryLabel(item),()=>Primary(item),item.Phase=="Completed"?"Enter":"Espaço");
                Menus.Item(rowMenu,"Mostrar na pasta",()=>ShowInFolder(item));
                Menus.Separator(rowMenu);
                Menus.Item(rowMenu,"Copiar link",()=>CopyText(item.Job.Url,"Link copiado."),"Ctrl+C");
                if(item.Phase!="Completed"&&!item.Busy&&!item.Replacing)Menus.Item(rowMenu,"Atualizar link…",()=>UpdateLink(item));
                if(item.Phase=="Completed") {
                    if(item.Job.Sha256!=null)Menus.Item(rowMenu,"Copiar SHA-256",()=>CopyText(item.Job.Sha256,"SHA-256 copiado."));
                    else if(!item.Hashing)Menus.Item(rowMenu,"Calcular SHA-256",()=>{manager.ComputeHash(item);RefreshList();});
                }
                Menus.Item(rowMenu,"Conferir com um SHA-256…",()=>ExpectHash(item));
                if(item.Phase!="Completed")Menus.Item(rowMenu,"Opções do download…",()=>EditOptions(items),"Alt+Enter");
                Menus.Separator(rowMenu);
                Menus.Item(rowMenu,"Remover da lista",()=>Remove(items,false),"Del");
                if(item.Phase=="Completed")Menus.Item(rowMenu,"Mover arquivo para a Lixeira",()=>Remove(items,true),"Shift+Del",true);
            } else {
                int n=items.Count,done=items.Count(i=>i.Phase=="Completed");
                if(items.Any(i=>i.Active||i.Phase=="Queued"))Menus.Item(rowMenu,"Pausar selecionados",()=>{foreach(var i in items)if(i.Active||i.Phase=="Queued")manager.Pause(i);RefreshAll();},"Espaço");
                if(items.Any(Resumable))Menus.Item(rowMenu,"Retomar selecionados",()=>{foreach(var i in items.Where(Resumable))manager.Start(i);RefreshAll();});
                if(items.Any(i=>i.Phase!="Completed"))Menus.Item(rowMenu,"Opções dos downloads…",()=>EditOptions(items),"Alt+Enter");
                Menus.Item(rowMenu,"Copiar "+n+" links",()=>CopyText(String.Join(Environment.NewLine,items.Select(i=>i.Job.Url)),n+" links copiados."),"Ctrl+C");
                Menus.Separator(rowMenu);
                Menus.Item(rowMenu,"Remover "+n+" da lista",()=>Remove(items,false),"Del");
                if(done>0)Menus.Item(rowMenu,"Mover "+done+(done==1?" arquivo":" arquivos")+" para a Lixeira",()=>Remove(items.Where(i=>i.Phase=="Completed").ToList(),true),"Shift+Del",true);
            }
            rowMenu.Show(screen);
        }
        // Options of downloads already in the list (after done, connections, speed limit).
        void EditOptions(List<DownloadItem> items) {
            items=items.Where(i=>i.Phase!="Completed").ToList();
            if(items.Count==0){ShowBanner(BannerKind.Info,"Downloads concluídos não têm opções para mudar.",null,null,4000);return;}
            var choice=DownloadDialog.Edit(this,settings,items);if(choice==null)return;
            foreach(var item in items) {
                bool connectionsChanged=item.Job.Connections!=choice.Connections;
                item.Job.AfterDone=choice.AfterDone;item.Job.Connections=choice.Connections;item.Job.SpeedLimit=choice.SpeedLimit;
                manager.ApplyOptions(item,connectionsChanged);
            }
            RefreshAll();
        }
        void ShowShortcuts() {
            Dialog.Inform(this,"Atalhos do teclado","Atalhos do teclado",
                "Ctrl+L ou Ctrl+N  ·  ir para a barra de link\n"+
                "Ctrl+V  ·  colar links (fora da barra)\n"+
                "Ctrl+F  ·  buscar\n"+
                "Ctrl+A  ·  selecionar todos\n"+
                "Ctrl+clique ou Shift+clique  ·  selecionar vários\n"+
                "Esc  ·  limpar a seleção ou a busca\n"+
                "Espaço  ·  pausar ou retomar os selecionados\n"+
                "Enter  ·  abrir o arquivo\n"+
                "Ctrl+E  ·  mostrar na pasta\n"+
                "Alt+Enter  ·  opções do download\n"+
                "Ctrl+C  ·  copiar os links\n"+
                "Delete  ·  remover da lista\n"+
                "Shift+Delete  ·  mover o arquivo para a Lixeira\n"+
                "Ctrl+P / Ctrl+R  ·  pausar todos / retomar todos\n"+
                "Ctrl+1 a Ctrl+4  ·  Todos, Baixando, Concluídos, Atenção\n"+
                "Ctrl+,  ·  configurações\n"+
                "F1  ·  esta lista");
        }
        void UpdateSelectionBar() {
            var selected=list.Selected;bool show=selected.Count>=2;
            if(show){selectionBar.Count=selected.Count;selectionBar.PerformLayout();selectionBar.Invalidate();}
            if(selectionBar.Visible!=show){selectionBar.Visible=show;pills.Visible=!show;searchField.Visible=!show;sortButton.Visible=!show;}
        }
        void ShowFolderMenu() {
            Menus.Clear(folderMenu);folderMenu.ShowCheckMargin=true;
            var folders=new List<string>{settings.Folder};
            folders.AddRange(settings.RecentFolders.Where(f=>!String.Equals(f,settings.Folder,StringComparison.OrdinalIgnoreCase)&&Directory.Exists(f)));
            foreach(string folder in folders.Take(6)) {
                string path=folder;
                var item=Menus.Item(folderMenu,path,()=>{settings.Folder=path;SaveSettings();LayoutCommand();command.Invalidate();});
                item.Checked=String.Equals(path,settings.Folder,StringComparison.OrdinalIgnoreCase);
            }
            Menus.Separator(folderMenu);
            Menus.Item(folderMenu,"Escolher outra pasta…",()=>{string picked=FolderPicker.Pick(this,settings.Folder,"Pasta para salvar os downloads");if(picked!=null){settings.Folder=picked;settings.Remember(picked);SaveSettings();LayoutCommand();command.Invalidate();}});
            Menus.Item(folderMenu,"Abrir esta pasta",()=>OpenFolder(settings.Folder));
            folderMenu.Show(folderChip,new Point(0,folderChip.Height+Theme.S(4)));
        }
        void ShowSortMenu() {
            Menus.Clear(sortMenu);sortMenu.ShowCheckMargin=true;
            foreach(var option in new[]{new[]{"recent","Mais recentes primeiro"},new[]{"name","Nome (A–Z)"},new[]{"size","Tamanho (maior primeiro)"}}) {
                string key=option[0];
                var item=Menus.Item(sortMenu,option[1],()=>{settings.Sort=key;SaveSettings();LayoutAll();RefreshList();});item.Checked=settings.Sort==key;
            }
            Menus.Separator(sortMenu);
            Menus.Item(sortMenu,"Pausar todos",()=>{manager.PauseAll();RefreshAll();}).Enabled=manager.ActiveCount>0||manager.QueuedCount>0;
            Menus.Item(sortMenu,"Retomar todos",()=>{manager.ResumeAll();RefreshAll();}).Enabled=manager.Visible.Any(Resumable);
            int done=manager.Visible.Count(i=>i.Phase=="Completed");
            Menus.Item(sortMenu,"Limpar concluídos da lista",()=>{int n=manager.ClearCompleted();RefreshAll();ShowBanner(BannerKind.Info,n+(n==1?" download concluído saiu":" downloads concluídos saíram")+" da lista. Os arquivos continuam nas pastas.",null,null,6000);}).Enabled=done>0;
            Menus.Separator(sortMenu);
            Menus.Item(sortMenu,"Selecionar todos",()=>{list.SelectAll();list.Focus();},"Ctrl+A");
            Menus.Item(sortMenu,"Atalhos do teclado",ShowShortcuts,"F1");
            sortMenu.Show(sortButton,new Point(sortButton.Width-sortMenu.PreferredSize.Width,sortButton.Height+Theme.S(4)));
        }
        void OpenFile(DownloadItem item) {
            if(item.Job.Filename==null||!File.Exists(item.Job.Output)){ShowBanner(BannerKind.Warning,"O arquivo não está mais na pasta de destino.",null,null,6000);return;}
            if(Categories.IsExecutable(item.Job.Filename)){ShowInFolder(item);return;}
            try{Process.Start(new ProcessStartInfo(item.Job.Output){UseShellExecute=true});}
            catch(Exception e){ShowBanner(BannerKind.Error,"Não foi possível abrir: "+e.Message,null,null,8000);}
        }
        void ShowInFolder(DownloadItem item) {
            if(item.Phase=="Completed"&&item.Job.Filename!=null&&File.Exists(item.Job.Output)) {
                try{Process.Start("explorer.exe","/select,\""+item.Job.Output+"\"");return;}catch(Exception){}
            }
            OpenFolder(item.Job.Folder);
        }
        void OpenFolder(string path) {
            if(!Directory.Exists(path)){ShowBanner(BannerKind.Warning,"A pasta não existe mais: "+path,null,null,6000);return;}
            try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch(Exception e){ShowBanner(BannerKind.Error,e.Message,null,null,6000);}
        }
        void CopyText(string text,string done) {
            ignoreClipboardUntil=DateTime.UtcNow.AddSeconds(1);
            try{Clipboard.SetText(text);ShowBanner(BannerKind.Success,done,null,null,2500);}catch(ExternalException){ShowBanner(BannerKind.Warning,"A área de transferência está ocupada. Tente de novo.",null,null,4000);}
        }
        void Remove(List<DownloadItem> items,bool recycle) {
            var unfinished=items.Where(i=>i.Phase!="Completed").ToList();
            if(recycle) {
                string heading=items.Count==1?"Mover “"+items[0].Title+"” para a Lixeira?":"Mover "+items.Count+" arquivos para a Lixeira?";
                if(!Dialog.Confirm(this,"Mover para a Lixeira",heading,"O download também sai da lista. Dá para recuperar pela Lixeira do Windows.","Mover para a Lixeira",true))return;
            } else if(unfinished.Count>0) {
                long saved=unfinished.Sum(i=>i.Bytes);
                string what=unfinished.Count==1?"“"+unfinished[0].Title+"” ainda não terminou":unfinished.Count+" downloads ainda não terminaram";
                if(!Dialog.Confirm(this,"Remover da lista","Remover da lista?",what+". O que já foi baixado"+(saved>0?" ("+Units.Size(saved)+")":"")+" será apagado. Arquivos concluídos continuam na pasta.","Remover",true))return;
            }
            foreach(var i in items)manager.Remove(i,recycle);
            RefreshAll();
            if(!recycle&&unfinished.Count==0)ShowBanner(BannerKind.Info,items.Count==1?"Saiu da lista. O arquivo continua na pasta.":items.Count+" downloads saíram da lista. Os arquivos continuam nas pastas.",null,null,5000);
        }
        async void UpdateLink(DownloadItem item) {
            if(item.Busy){ShowBanner(BannerKind.Info,"Pause o download antes de trocar o link.",null,null,5000);return;}
            string suggestion=null;
            try{if(Clipboard.ContainsText()){var found=Links.Extract(Clipboard.GetText());if(found.Count==1&&found[0]!=item.Job.Url)suggestion=found[0];}}catch(ExternalException){}
            string url=Dialog.Prompt(this,"Atualizar link","Atualizar o link de “"+item.Title+"”",
                "Cole um link novo do mesmo arquivo. Antes de continuar, o "+AppPaths.Name+" confere o tamanho e compara trechos já baixados com o link novo.",
                "https://",suggestion,"Conferir e continuar",value=>{try{Engine.ValidateUrl(value);return null;}catch(DownloadError e){return e.Message;}});
            if(url==null)return;
            try {
                var task=manager.CheckReplacement(item,url);RefreshAll();
                var result=await task;
                manager.ApplyReplacement(item,result);
                ShowBanner(BannerKind.Success,"Link atualizado. “"+item.Title+"” continua de onde parou"+(result.Samples>0?" (conferido em "+result.Samples+(result.Samples==1?" trecho).":" trechos)."):"."),null,null,7000);
            } catch(Exception e) {
                manager.CancelReplacement(item);
                ShowBanner(BannerKind.Error,(e is AggregateException?e.GetBaseException():e).Message,null,null,0);
            }
            RefreshAll();
        }
        void ExpectHash(DownloadItem item) {
            string message="Cole o SHA-256 publicado pelo site. "+(item.Phase=="Completed"?"O "+AppPaths.Name+" calcula o do arquivo e compara.":"A conferência acontece quando o download terminar.");
            string value=Dialog.Prompt(this,"Conferir SHA-256","Conferir “"+item.Title+"”",message,"64 caracteres de 0 a 9 e de a até f",item.Job.ExpectedSha256,"Conferir",
                v=>{try{DownloadCoordinator.NormalizeHash(v);return null;}catch(DownloadError e){return e.Message;}});
            if(value==null)return;
            manager.SetExpectedHash(item,value);
            if(item.Job.HashCheck=="match")ShowBanner(BannerKind.Success,"O SHA-256 de "+item.Title+" confere com o informado.",null,null,6000);
            else if(item.Job.HashCheck=="mismatch")ShowBanner(BannerKind.Error,"O SHA-256 de "+item.Title+" não confere com o informado.",null,null,0);
            RefreshAll();
        }
        void OpenSettings() {
            bool install=false;
            using(var window=new SettingsWindow(settings,manager,()=>{LayoutCommand();LayoutAll();RefreshAll();},shutdownArmed,on=>{shutdownArmed=on;completedSinceArmed=0;UpdateStatus();},()=>install=true))
                window.ShowDialog(this);
            if(install)InstallNow();
        }

        // ---- installing in Windows ----------------------------------------------------------
        void OfferInstall() {
            if(JustInstalled)ShowBanner(BannerKind.Success,"Instalado. O "+AppPaths.Name+" está no menu Iniciar"+(Installer.DesktopShortcut?" e na área de trabalho":"")+". Para fixar na barra de tarefas: botão direito no atalho > Fixar na barra de tarefas.",null,null,0);
        }
        // Copies this exe into the install folder and switches to that copy (which waits for this
        // one to pause and save its downloads before opening).
        void InstallNow() {
            try{Installer.Install();}
            catch(Exception e){ShowBanner(BannerKind.Error,"Não foi possível instalar: "+e.Message,null,null,0);return;}
            if(Installer.RunningInstalled){ShowBanner(BannerKind.Success,"Instalação atualizada.",null,null,5000);return;}
            try{Process.Start(new ProcessStartInfo(Installer.InstalledExe,(AppPaths.Custom?"--data \""+AppPaths.Root+"\" ":"")+"--after-install"){UseShellExecute=false,WorkingDirectory=Installer.Folder});}
            catch(Exception e){ShowBanner(BannerKind.Error,"Instalado, mas não deu para abrir a cópia instalada: "+e.Message,null,null,0);return;}
            ExitApp();
        }
        void CheckShutdown() {
            if(!shutdownArmed||preview||manager.ActiveCount>0||manager.QueuedCount>0||completedSinceArmed==0)return;
            shutdownArmed=false;
            PowerAction(false,"Tudo baixado.");
        }
        [DllImport("powrprof.dll")] static extern bool SetSuspendState(bool hibernate,bool forceCritical,bool disableWakeEvent);
        // Shuts down or suspends the PC after 60 seconds, with a button to cancel. The progress of
        // anything still downloading is saved and resumes later.
        void PowerAction(bool sleep,string reason) {
            if(preview)return;
            ShowMain();
            if(sleep) {
                sleepAt=DateTime.UtcNow.AddSeconds(60);
                ShowBanner(BannerKind.Warning,reason+" O computador vai suspender em 60 segundos.","Cancelar",()=>{sleepAt=null;ShowBanner(BannerKind.Info,"Suspensão cancelada.",null,null,4000);},0);
                return;
            }
            try {
                Process.Start(new ProcessStartInfo("shutdown.exe","/s /t 60 /c \""+AppPaths.Name+": "+reason+"\""){UseShellExecute=false,CreateNoWindow=true});
                ShowBanner(BannerKind.Warning,reason+" O computador desliga em 60 segundos.","Cancelar desligamento",()=>{
                    try{Process.Start(new ProcessStartInfo("shutdown.exe","/a"){UseShellExecute=false,CreateNoWindow=true});}catch(Exception){}
                    ShowBanner(BannerKind.Info,"Desligamento cancelado.",null,null,4000);
                },0);
            } catch(Exception e){ShowBanner(BannerKind.Error,"Não foi possível agendar o desligamento: "+e.Message,null,null,0);}
        }

        // ---- tray, mini window and closing ---------------------------------------------------
        void CreateTray() {
            trayIcon=new Icon(Program.AppIcon,SystemInformation.SmallIconSize);
            tray=new NotifyIcon{Icon=trayIcon,Text=AppPaths.Name,Visible=true,ContextMenuStrip=trayMenu};
            tray.MouseClick+=(sender,e)=>{if(e.Button==MouseButtons.Left)ShowMain();};
            tray.BalloonTipClicked+=delegate{var action=balloonAction;balloonAction=null;if(action!=null)action();else ShowMain();};
            Menus.Item(trayMenu,"Abrir "+AppPaths.Name,ShowMain);
            Menus.Item(trayMenu,"Modo mini",ShowMini);
            Menus.Separator(trayMenu);
            Menus.Item(trayMenu,"Pausar todos",()=>{manager.PauseAll();RefreshAll();});
            Menus.Item(trayMenu,"Retomar todos",()=>{manager.ResumeAll();RefreshAll();});
            Menus.Separator(trayMenu);
            Menus.Item(trayMenu,"Sair",ExitApp);
        }
        public void ShowMain() {
            if(mini!=null&&mini.Visible)mini.Hide();
            StartHidden=false;
            if(!Visible){if(IsHandleCreated)NoFlash.Cloak(Handle,true);Show();NoFlash.Reveal(this);}
            if(WindowState==FormWindowState.Minimized)ShowWindow(Handle,9);
            Foreground.Bring(Handle);Activate();
        }
        void HideToTray() {
            Hide();
            if(!settings.TrayHintShown&&tray!=null) {
                settings.TrayHintShown=true;SaveSettings();
                Balloon(AppPaths.Name+" continua aberto","Os downloads seguem por aqui. Para sair, clique com o botão direito neste ícone.",ShowMain);
            }
        }
        void ShowMini() {
            if(mini==null) {
                mini=new MiniWindow(manager,settings);
                mini.ExpandRequested+=delegate{ShowMain();};
                mini.CloseRequested+=delegate{mini.Hide();if(tray!=null)HideToTray();else ShowMain();};
            }
            mini.RefreshData();mini.Place();mini.Show();Hide();
        }
        void ExitApp(){exiting=true;if(mini!=null)mini.Hide();Close();}
        void OnClosing(object sender,FormClosingEventArgs e) {
            if(!exiting&&!preview&&e.CloseReason==CloseReason.UserClosing&&settings.CloseToTray&&tray!=null){e.Cancel=true;HideToTray();return;}
            if(!closing)SaveWindow();
            bool urgent=e.CloseReason==CloseReason.WindowsShutDown||e.CloseReason==CloseReason.TaskManagerClosing;
            if(!urgent&&!preview&&manager.ActiveCount>0&&(!closing||(DateTime.UtcNow-closeStarted).TotalSeconds<10)) {
                if(!closing){closeStarted=DateTime.UtcNow;closing=true;manager.PauseAll(false);UpdateStatus();}
                e.Cancel=true;return;
            }
            timer.Stop();if(tray!=null)tray.Visible=false;if(mini!=null)mini.Hide();
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData) {
            bool typing=ActiveControl is TextBoxBase;
            switch(keyData){
                case Keys.Control|Keys.L:case Keys.Control|Keys.N:link.Focus();link.SelectAll();return true;
                case Keys.Control|Keys.F:search.Focus();search.SelectAll();return true;
                case Keys.Control|Keys.Oemcomma:OpenSettings();return true;
                case Keys.F1:ShowShortcuts();return true;
                case Keys.Control|Keys.P:manager.PauseAll();RefreshAll();return true;
                case Keys.Control|Keys.R:manager.ResumeAll();RefreshAll();return true;
                case Keys.Control|Keys.D1:case Keys.Control|Keys.D2:case Keys.Control|Keys.D3:case Keys.Control|Keys.D4:
                    filter=(int)(keyData&Keys.KeyCode)-(int)Keys.D1;RefreshList();return true;
                case Keys.Escape:
                    if(banner.Visible){banner.Visible=false;return true;}
                    if(search.Text.Length>0){search.Text="";return true;}
                    if(list.Selected.Count>0){list.ClearSelection();return true;}
                    break;
            }
            if(typing)return base.ProcessCmdKey(ref msg,keyData);
            // Outside text boxes these act on the selected downloads, wherever the focus is.
            var selected=list.Selected;
            switch(keyData){
                case Keys.Control|Keys.V:PasteLinks();return true;
                case Keys.Control|Keys.A:list.SelectAll();list.Focus();return true;
                case Keys.Delete:if(selected.Count>0)Remove(selected,false);return true;
                case Keys.Shift|Keys.Delete:if(selected.Any(i=>i.Phase=="Completed"))Remove(selected.Where(i=>i.Phase=="Completed").ToList(),true);return true;
                case Keys.Control|Keys.C:if(selected.Count>0)CopyText(String.Join(Environment.NewLine,selected.Select(i=>i.Job.Url)),selected.Count==1?"Link copiado.":selected.Count+" links copiados.");return true;
                case Keys.Control|Keys.E:if(selected.Count>0)ShowInFolder(selected[0]);return true;
                case Keys.Alt|Keys.Enter:if(selected.Count>0)EditOptions(selected);return true;
            }
            return base.ProcessCmdKey(ref msg,keyData);
        }
        protected override void Dispose(bool disposing) {
            if(disposing&&!disposed) {
                disposed=true;Theme.Changed-=OnTheme;timer.Dispose();bannerTimer.Dispose();tips.Dispose();
                foreach(var menu in new[]{rowMenu,folderMenu,sortMenu,trayMenu})menu.Dispose();
                if(tray!=null){tray.Visible=false;tray.Dispose();}
                if(trayIcon!=null)trayIcon.Dispose();
                if(mini!=null)mini.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---- previews used by tests and screenshots -------------------------------------------
        void SeedPreview() {
            var today=DateTime.Today;
            Seed("ubuntu-24.04-desktop-amd64.iso","releases.ubuntu.com",6100000000,.72,24800000,"Downloading",today.AddHours(17),null,null);
            Seed("Projeto final.zip","drive.exemplo.com",2400000000,.41,13600000,"Downloading",today.AddHours(17.2),null,null);
            Seed("Gravação da aula 03.mp4","cdn.exemplo.com",1300000000,0,0,"Queued",today.AddHours(17.3),null,null);
            Seed("Vídeo da apresentação.mp4","arquivos.exemplo.com",846000000,.4,0,"Paused",today.AddHours(15.5),null,null);
            Seed("setup-editor-3.2.exe","downloads.exemplo.com",92000000,.63,0,"Error",today.AddDays(-1).AddHours(22),null,"O site recusou o link (HTTP 403). Ele pode ter expirado: use Atualizar link para continuar de onde parou.");
            var photos=Seed("Fotos de viagem.zip","fotos.exemplo.com",148000000,1,0,"Completed",today.AddHours(16),today.AddHours(16).AddMinutes(40),null);photos.Job.HashCheck="match";
            Seed("Planilha de custos.xlsx","planilhas.exemplo.com",2100000,1,0,"Completed",today.AddHours(11),today.AddHours(11).AddMinutes(2),null);
            Seed("Backup do projeto.7z","arquivos.exemplo.com",2234224629,1,0,"Completed",today.AddDays(-1).AddHours(18),today.AddDays(-1).AddHours(18).AddMinutes(9),null);
            ShowBanner(BannerKind.Info,"Link copiado: filme-4k-hdr.mkv · cdn.exemplo.com","Baixar",()=>{},0,"Ignorar",()=>{});
        }
        DownloadItem Seed(string name,string host,long total,double fraction,double rate,string phase,DateTime created,DateTime? completed,string detail) {
            var job=new Job{Id="preview"+manager.Items.Count,Url="https://"+host+"/"+Uri.EscapeDataString(name),Folder=AppPaths.Downloads,Filename=name,Total=total,State=phase,Detail=detail??"",Created=created.ToString("s")};
            if(completed.HasValue)job.Completed=completed.Value.ToString("s");
            var item=new DownloadItem(job){PreviewBytes=(long)(total*fraction),PreviewPhase=phase,PreviewRate=rate};item.Update();manager.Items.Add(item);
            return item;
        }
        public void Preview(string path,bool selection=false) {
            StartPosition=FormStartPosition.Manual;Location=new Point(-20000,-20000);ShowInTaskbar=false;Show();Application.DoEvents();RefreshAll();
            if(selection){banner.Visible=false;list.SelectAll();}else if(manager.Items.Count>0)list.Select(manager.Items[0]);
            using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(0,0,Width,Height));image.Save(path);}
            Hide();
        }
        // Renders the "Novo download" or "Opções do download" window to a PNG.
        public void PreviewDialog(string path,bool fresh) {
            StartPosition=FormStartPosition.Manual;Location=new Point(-20000,-20000);ShowInTaskbar=false;Show();
            using(var capture=new System.Windows.Forms.Timer{Interval=600}) {
                capture.Tick+=delegate{
                    capture.Stop();
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f is DownloadDialog);if(dialog==null)return;
                    using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(0,0,dialog.Width,dialog.Height));image.Save(path);}
                    dialog.Close();
                };
                capture.Start();
                if(fresh)DownloadDialog.Ask(this,settings,new List<Incoming>{new Incoming{Url="https://arquivos.exemplo.com/backup-do-projeto.7z",Name="Backup do projeto.7z",Size=2234224629,Referrer="https://arquivos.exemplo.com/"}});
                else DownloadDialog.Edit(this,settings,manager.Items.Where(i=>i.Phase=="Downloading").Take(1).ToList());
            }
        }
        public void PreviewMini(string path) {
            using(var window=new MiniWindow(manager,settings)) {
                window.RefreshData();window.Location=new Point(-20000,-20000);window.Show();Application.DoEvents();
                using(var image=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(image,new Rectangle(0,0,window.Width,window.Height));image.Save(path);}
            }
        }
        public void PreviewSettings(string path) {
            using(var window=new SettingsWindow(settings,manager,null,false,null,null)) {
                window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-20000,-20000);window.Show();Application.DoEvents();
                using(var image=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(image,new Rectangle(0,0,window.Width,window.Height));image.Save(path);}
            }
        }
    }

    // A download handed to the app: the link plus what the browser already knows about it.
    sealed class Incoming {
        public string Url,Name,Referrer;public long Size;
        public string ToLine(){return Clean(Url)+"\t"+Clean(Name)+"\t"+Size+"\t"+Clean(Referrer);}
        static string Clean(string text){return (text??"").Replace("\t"," ").Replace("\r"," ").Replace("\n"," ");}
        public static Incoming Parse(string line) {
            var parts=(line??"").Trim('\r').Split('\t');
            var found=Links.Extract(parts[0]);if(found.Count!=1||found[0]!=parts[0].Trim())return null;
            long size;
            return new Incoming{Url=found[0],Name=parts.Length>1&&parts[1].Length>0?parts[1]:null,Size=parts.Length>2&&Int64.TryParse(parts[2],out size)?size:0,Referrer=parts.Length>3&&parts[3].Length>0?parts[3]:null};
        }
    }

    static class Program {
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string title);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr w,ref CopyData data);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out int process);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int process);
        [StructLayout(LayoutKind.Sequential)] public struct CopyData{public IntPtr Data;public int Size;public IntPtr Pointer;}
        public const int CopyMagic=0x4D59444C;
        static Icon icon;
        public static Icon AppIcon {get{if(icon==null)icon=new Icon(new MemoryStream(IconBytes()));return icon;}}
        public static string WindowTitle {get{return AppPaths.Custom?AppPaths.Name+" ("+AppPaths.Root+")":AppPaths.Name;}}

        [STAThread]
        static int Main(string[] args) {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.SystemDefault;ServicePointManager.DefaultConnectionLimit=64;
            if(args.Length>0&&args[0]=="--download-test")return Diagnostics.RunDownloadTest(args);
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Theme.InitScale();
            var list=args.ToList();
            int data=list.IndexOf("--data");
            if(data>=0&&data+1<list.Count){AppPaths.Root=list[data+1];AppPaths.Custom=true;list.RemoveRange(data,2);}
            // Chrome and Edge start the exe as their native messaging host with the extension origin.
            if(list.Count>0&&list[0].StartsWith("chrome-extension://"))return ChromeIntegration.RunHost();
            if(list.Count>1&&list[0]=="--icon"){File.WriteAllBytes(list[1],IconBytes());return 0;}
            if(list.Count>1&&list[0]=="--preview")return Preview(list);
            if(list.Count>0&&list[0]=="--install-extension"){Console.WriteLine(ChromeIntegration.Install());return 0;}
            if(list.Count>1&&list[0]=="--copy-to")return Installer.CopyTo(list[1]);
            if(list.Count>0&&list[0]=="--install"){int to=list.IndexOf("--to");Installer.Install(to>0&&to+1<list.Count?list[to+1]:null);Console.WriteLine(Installer.InstalledExe);return 0;}
            if(list.Count>0&&list[0]=="--uninstall")return Uninstall(list.Contains("--quiet"),list.Contains("--remove-data"));
            // The app always runs from its install folder. Anywhere else this exe installs it (or
            // updates a different build) and then opens the installed copy with the same arguments.
            if(!Installer.RunningInstalled&&(!AppPaths.Custom||Installer.Installed)) {
                bool update=Installer.Installed;
                if(!update||Installer.Outdated) {
                    Theme.Apply(Settings.Load(AppPaths.SettingsFile).Theme);
                    var result=SetupWindow.Run(update);
                    if(result==SetupResult.Cancel)return 0;
                    if(result==SetupResult.Installed)list.Add("--after-install");
                }
                return LaunchInstalled(list);
            }
            bool tray=list.Remove("--tray"),installed=list.Remove("--after-install");
            // --incoming carries a download sent by the browser extension (name, size and page included).
            var incoming=new List<Incoming>();string command="show";
            int coded=list.IndexOf("--incoming");
            if(coded>=0&&coded+1<list.Count) {
                try{foreach(string line in Encoding.UTF8.GetString(Convert.FromBase64String(list[coded+1])).Split('\n'))if(Incoming.Parse(line)!=null)incoming.Add(Incoming.Parse(line));}catch(FormatException){}
                list.RemoveRange(coded,2);command="browser";
            }
            incoming.AddRange(Links.Extract(String.Join(" ",list)).Select(url=>new Incoming{Url=url}));
            bool first;
            using(var mutex=new Mutex(true,"Local\\MyDownload."+(AppPaths.Custom?Short(AppPaths.Root):"Main"),out first)) {
                // Right after installing, the copy that did the install is still closing.
                if(!first&&installed){try{first=mutex.WaitOne(30000);}catch(AbandonedMutexException){first=true;}}
                if(!first){Forward(command,incoming.Select(i=>i.ToLine()),5000);return 0;}
                DownloadCoordinator manager=null;MainWindow window=null;
                try {
                    Directory.CreateDirectory(AppPaths.Root);
                    var settings=Settings.Load(AppPaths.SettingsFile);Theme.Apply(settings.Theme);
                    bool extensionUpdated=false,hidden=tray&&incoming.Count==0;
                    Action[] work={
                        ()=>extensionUpdated=ChromeIntegration.Refresh(),
                        ()=>manager=new DownloadCoordinator(AppPaths.Root,settings),
                        ()=>{window=new MainWindow(manager,settings,false){StartHidden=hidden,ExtensionUpdated=extensionUpdated,JustInstalled=installed};window.PendingIncoming.AddRange(incoming);}
                    };
                    // Opened by the user: splash for at least 3 s. Started with Windows (tray): no splash.
                    // A download clicked in the browser: splash only while it really loads.
                    if(hidden)foreach(var step in work)step();
                    else SplashWindow.Show(incoming.Count>0||AppPaths.Custom?0:3000,new[]{"Conferindo a instalação…","Lendo o histórico de downloads…","Preparando a janela…"},work);
                    Application.Run(window);
                    return 0;
                } catch(Exception e) { MessageBox.Show(e.Message,AppPaths.Name,MessageBoxButtons.OK,MessageBoxIcon.Error);return 1; }
                finally { if(window!=null)window.Dispose();if(manager!=null)manager.Dispose(); }
            }
        }
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,int message,IntPtr w,IntPtr l);
        static int LaunchInstalled(List<string> args) {
            var parts=new List<string>();
            if(AppPaths.Custom){parts.Add("--data");parts.Add(AppPaths.Root);}
            parts.AddRange(args);
            string line=String.Join(" ",parts.Select(a=>"\""+a.Replace("\"","\\\"")+"\""));
            try{Process.Start(new ProcessStartInfo(Installer.InstalledExe,line){UseShellExecute=false,WorkingDirectory=Installer.Folder});return 0;}
            catch(Exception e){MessageBox.Show("Não foi possível abrir o "+AppPaths.Name+" instalado: "+e.Message,AppPaths.Name,MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
        // Closes the open app the same way as "Sair" (downloads are paused and saved first). With
        // onlyExe, only when the open app runs from that file.
        public static void CloseApp(string onlyExe=null) {
            IntPtr window=FindWindow(null,WindowTitle);if(window==IntPtr.Zero)return;
            int process;GetWindowThreadProcessId(window,out process);
            if(process==Process.GetCurrentProcess().Id)return;
            try {
                using(var running=Process.GetProcessById(process)) {
                    if(onlyExe!=null&&!String.Equals(running.MainModule.FileName,onlyExe,StringComparison.OrdinalIgnoreCase))return;
                    PostMessage(window,0x10,IntPtr.Zero,IntPtr.Zero);
                    if(!running.WaitForExit(20000)){running.Kill();running.WaitForExit(5000);}
                }
            } catch(Exception){}
        }
        // Started by Windows Settings > Apps > Desinstalar (UninstallString) or by the app itself.
        // --quiet skips the questions (Windows' QuietUninstallString); --remove-data also erases the history.
        static int Uninstall(bool quiet,bool removeData) {
            var settings=Settings.Load(AppPaths.SettingsFile);Theme.Apply(settings.Theme);
            if(!quiet&&!Dialog.ConfirmOption(null,"Desinstalar "+AppPaths.Name,"Desinstalar o "+AppPaths.Name+"?",
                "Remove o app, os atalhos do menu Iniciar e da área de trabalho e a ligação com o Chrome. Os arquivos que você baixou continuam onde estão.",
                "Desinstalar",true,"Apagar também o histórico, as configurações e o que ficou de downloads incompletos",out removeData))return 0;
            string folder=Installer.Folder,exe=Installer.InstalledExe;
            CloseApp();
            Installer.Uninstall(removeData);
            if(!quiet)Dialog.Inform(null,"Desinstalar "+AppPaths.Name,AppPaths.Name+" desinstalado",
                "Se você instalou a extensão no Chrome ou no Edge, remova também em chrome://extensions (ou edge://extensions)."+(removeData?"":"\n\nO histórico e as configurações ficaram guardados para uma próxima instalação."));
            Installer.RemoveFolderAfterExit(folder,exe);
            return 0;
        }
        // MyDownload.exe --preview <png> [dark|light] [main|mini|settings]
        static int Preview(List<string> list) {
            Theme.Apply(list.Count>2?list[2]:"dark");
            if(list.Count>3&&list[3].StartsWith("setup")){SetupWindow.Preview(list[1],list[3]=="setup-update");return 0;}
            if(list.Count>3&&list[3]=="splash"){SplashWindow.Preview(list[1]);return 0;}
            var settings=new Settings();
            using(var manager=new DownloadCoordinator(Path.GetTempPath(),settings,true))
            using(var window=new MainWindow(manager,settings,true)) {
                string what=list.Count>3?list[3]:"main";
                if(what=="mini")window.PreviewMini(list[1]);else if(what=="settings")window.PreviewSettings(list[1]);
                else if(what=="new"||what=="options")window.PreviewDialog(list[1],what=="new");else window.Preview(list[1],what=="selection");
            }
            return 0;
        }
        // Hands downloads to the window that is already open, waiting for it while the app starts.
        // The first line is the command ("show" from a second launch, "browser" from the extension),
        // then one Incoming line per download. Returns false when there is no window.
        public static bool Forward(string command,IEnumerable<string> lines,int waitMs) {
            IntPtr window=FindWindow(null,WindowTitle);
            for(var clock=Stopwatch.StartNew();window==IntPtr.Zero&&clock.ElapsedMilliseconds<waitMs;window=FindWindow(null,WindowTitle))Thread.Sleep(100);
            if(window==IntPtr.Zero)return false;
            int process;GetWindowThreadProcessId(window,out process);AllowSetForegroundWindow(process);
            byte[] bytes=Encoding.Unicode.GetBytes(command+"\n"+String.Join("\n",lines)+"\0");
            IntPtr memory=Marshal.AllocHGlobal(bytes.Length);
            try{Marshal.Copy(bytes,0,memory,bytes.Length);var data=new CopyData{Data=(IntPtr)CopyMagic,Size=bytes.Length,Pointer=memory};SendMessage(window,0x4A,IntPtr.Zero,ref data);}
            finally{Marshal.FreeHGlobal(memory);}
            return true;
        }
        public static string Short(string text){using(var sha=SHA1.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToUpperInvariant()))).Replace("-","").Substring(0,12);}
        public static void ShowAbout(IWin32Window owner) {
            string message="Baixa com várias conexões e continua de onde parou se a internet cair.\n\n"+
                "Histórico e configurações: "+AppPaths.Root+"\n"+
                "Downloads em andamento ficam numa pasta oculta "+AppPaths.WorkFolderName+" dentro da pasta de destino, apagada ao concluir.\n\n"+
                "Ícones: Segoe Fluent Icons, do próprio Windows.";
            if(Dialog.Confirm(owner,"Sobre o "+AppPaths.Name,AppPaths.Name+" 3.0",message,"Abrir pasta de dados")&&Directory.Exists(AppPaths.Root))
                Process.Start(new ProcessStartInfo(AppPaths.Root){UseShellExecute=true});
        }

        // App icon drawn from the Fluent download glyph: DIB images for small sizes, PNG for 256 px.
        public static Bitmap IconImage(int size) {
            var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb);
            using(var g=Graphics.FromImage(bitmap)) {
                g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
                float inset=size>=32?size*.06f:.5f;var r=new RectangleF(inset,inset,size-2*inset,size-2*inset);
                using(var path=Theme.Round(r,size*.24f))using(var brush=new SolidBrush(Theme.Hex(0x0067c0)))g.FillPath(brush,path);
                // Centered by the drawn outline of the glyph, not by the font's line height.
                using(var family=new FontFamily(Theme.IconFamily))
                using(var glyph=new GraphicsPath())
                using(var white=new SolidBrush(Color.White)) {
                    glyph.AddString(Icons.Download,family,(int)FontStyle.Regular,100f,PointF.Empty,StringFormat.GenericTypographic);
                    var bounds=glyph.GetBounds();
                    float scale=size*.5f/Math.Max(bounds.Width,bounds.Height);
                    using(var matrix=new Matrix()) {
                        matrix.Translate(-(bounds.X+bounds.Width/2),-(bounds.Y+bounds.Height/2),MatrixOrder.Append);
                        matrix.Scale(scale,scale,MatrixOrder.Append);
                        matrix.Translate(size/2f,size/2f,MatrixOrder.Append);
                        glyph.Transform(matrix);
                    }
                    g.FillPath(white,glyph);
                }
            }
            return bitmap;
        }
        public static byte[] IconBytes() {
            int[] sizes={16,20,24,32,40,48,64,256};
            var images=new List<byte[]>();
            foreach(int s in sizes)using(var b=IconImage(s))images.Add(s>=256?Png(b):Dib(b));
            using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream)) {
                w.Write((short)0);w.Write((short)1);w.Write((short)sizes.Length);
                int offset=6+16*sizes.Length;
                for(int i=0;i<sizes.Length;i++){
                    w.Write((byte)(sizes[i]>=256?0:sizes[i]));w.Write((byte)(sizes[i]>=256?0:sizes[i]));w.Write((byte)0);w.Write((byte)0);
                    w.Write((short)1);w.Write((short)32);w.Write(images[i].Length);w.Write(offset);offset+=images[i].Length;
                }
                foreach(var image in images)w.Write(image);
                return stream.ToArray();
            }
        }
        static byte[] Png(Bitmap b){using(var s=new MemoryStream()){b.Save(s,ImageFormat.Png);return s.ToArray();}}
        static byte[] Dib(Bitmap b) {
            int size=b.Width,mask=((size+31)/32)*4;
            using(var s=new MemoryStream())using(var w=new BinaryWriter(s)) {
                w.Write(40);w.Write(size);w.Write(size*2);w.Write((short)1);w.Write((short)32);w.Write(0);w.Write(size*size*4+mask*size);w.Write(0);w.Write(0);w.Write(0);w.Write(0);
                for(int y=size-1;y>=0;y--)for(int x=0;x<size;x++){var c=b.GetPixel(x,y);w.Write(c.B);w.Write(c.G);w.Write(c.R);w.Write(c.A);}
                w.Write(new byte[mask*size]);
                return s.ToArray();
            }
        }
    }
}
