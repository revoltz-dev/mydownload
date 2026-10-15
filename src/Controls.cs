using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MyDownload {
    abstract class Painted : Control {
        protected bool hover,pressed;
        protected Painted() {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);
            DoubleBuffered=true;
        }
        protected Color Back {get{return Parent!=null?Parent.BackColor:Theme.C.Bg;}}
        protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){pressed=true;Invalidate();}base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
        protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
        protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
        protected void FocusRing(Graphics g,Rectangle r,float radius) {
            if(Focused&&ShowFocusCues)Theme.Stroke(g,r,Theme.C.Accent,radius,Math.Max(1.5f,Theme.F*1.5f));
        }
    }

    enum ButtonKind { Standard, Primary, Danger, Subtle, Outline, Chrome, Close }
    sealed class FlatButton : Painted {
        public string Glyph;public ButtonKind Kind=ButtonKind.Standard;public float GlyphSize=14;public int Radius=-1;public bool Center=true;
        public FlatButton(){Cursor=Cursors.Hand;TabStop=true;Font=Theme.Ui(9);}
        public int Preferred(int padding=14) {
            int w=Theme.S(padding*2);
            if(!String.IsNullOrEmpty(Glyph))w+=Theme.S(GlyphSize)+(String.IsNullOrEmpty(Text)?0:Theme.S(8));
            if(!String.IsNullOrEmpty(Text))w+=Theme.Measure(Text,Font);
            return w;
        }
        protected override void OnTextChanged(EventArgs e){Invalidate();base.OnTextChanged(e);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter){OnClick(EventArgs.Empty);e.Handled=true;}base.OnKeyDown(e);}
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;g.Clear(Back);
            int radius=Radius>=0?Radius:Theme.S(6);
            Color fill=Color.Empty,fore=c.Text,border=Color.Empty;
            switch(Kind){
                case ButtonKind.Primary:fill=pressed?Theme.Blend(c.Accent,c.Bg,.25f):hover?Theme.Blend(c.Accent,c.Bg,.12f):c.Accent;fore=c.OnAccent;break;
                case ButtonKind.Danger:fill=pressed?Theme.Blend(c.Err,c.Bg,.25f):hover?Theme.Blend(c.Err,c.Bg,.12f):c.Err;fore=c.Dark?Theme.Hex(0x1a0b0d):Color.White;break;
                case ButtonKind.Standard:fill=pressed?c.Pressed:hover?c.Hover:Theme.Blend(c.Card,c.Text,c.Dark?.06f:.03f);border=c.Line;break;
                case ButtonKind.Outline:fill=pressed?c.Pressed:hover?c.Hover:Color.Empty;border=c.Line;fore=hover?c.Text:c.Muted;break;
                case ButtonKind.Subtle:fill=pressed?c.Pressed:hover?c.Hover:Color.Empty;fore=hover?c.Text:c.Muted;break;
                case ButtonKind.Chrome:fill=pressed?c.Pressed:hover?c.Hover:Color.Empty;fore=c.Text;radius=0;break;
                case ButtonKind.Close:fill=pressed?Theme.Hex(0xc42b1c):hover?Theme.Hex(0xc42b1c):Color.Empty;fore=hover?Color.White:c.Text;radius=0;break;
            }
            if(!Enabled){fore=c.Faint;if(Kind==ButtonKind.Primary||Kind==ButtonKind.Danger){fill=c.Line;fore=c.Muted;}}
            var r=new Rectangle(0,0,Width,Height);
            if(fill!=Color.Empty)Theme.Fill(g,r,fill,radius);
            if(border!=Color.Empty)Theme.Stroke(g,r,border,radius);
            bool hasText=!String.IsNullOrEmpty(Text),hasGlyph=!String.IsNullOrEmpty(Glyph);
            int glyph=Theme.S(GlyphSize),gap=hasText&&hasGlyph?Theme.S(8):0;
            int content=(hasGlyph?glyph:0)+gap+(hasText?Theme.Measure(Text,Font):0);
            int x=Center?(Width-content)/2:Theme.S(12);
            if(hasGlyph){Icons.Draw(g,Glyph,new Rectangle(x,0,glyph,Height),GlyphSize,Kind==ButtonKind.Primary||Kind==ButtonKind.Danger||!Enabled?fore:(Kind==ButtonKind.Subtle&&!hover?c.Muted:fore));x+=glyph+gap;}
            if(hasText)Theme.Text(g,Text,Font,new Rectangle(x,0,Math.Max(0,Width-x-Theme.S(6)),Height),fore);
            FocusRing(g,r,radius);
        }
    }

    // Rounded surface; its BackColor is the color children paint on.
    class CardPanel : Panel {
        public int Radius=8;public bool Underline;public bool Border=true;public Control FocusSource;
        public CardPanel(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
        protected override void OnPaintBackground(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;g.Clear(Parent!=null?Parent.BackColor:c.Bg);
            var r=new RectangleF(0,0,Width,Height);float radius=Theme.S(Radius);
            Theme.Fill(g,r,BackColor,radius);
            bool focused=FocusSource!=null&&FocusSource.Focused;
            if(Border)Theme.Stroke(g,r,focused?Theme.Blend(c.Line,c.Accent,.35f):c.Line,radius);
            if(Underline) {
                g.SmoothingMode=SmoothingMode.AntiAlias;
                using(var path=Theme.Round(r,radius)) {
                    g.SetClip(path);
                    using(var b=new SolidBrush(focused?c.Accent:c.Strong))g.FillRectangle(b,0,Height-(focused?Theme.S(2):1),Width,Theme.S(2));
                    g.ResetClip();
                }
            }
        }
    }

    // Single-line box that turns pasted multi-line text into one line of links.
    sealed class PasteBox : TextBox {
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr w,string l);
        string cue;
        public PasteBox(){BorderStyle=BorderStyle.None;Font=Theme.Ui(10.5f);}
        public string Cue {get{return cue;}set{cue=value;if(IsHandleCreated)SendMessage(Handle,0x1501,(IntPtr)1,value);}}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);if(cue!=null)SendMessage(Handle,0x1501,(IntPtr)1,cue);}
        protected override void WndProc(ref Message m) {
            if(m.Msg==0x302) {
                string text=null;try{if(Clipboard.ContainsText())text=Clipboard.GetText();}catch(ExternalException){}
                if(text!=null){SelectedText=Regex.Replace(text.Trim(),@"\s+"," ");return;}
            }
            base.WndProc(ref m);
        }
    }

    sealed class Toggle : Painted {
        bool on;public event EventHandler Changed;
        public Toggle(){Cursor=Cursors.Hand;TabStop=true;Size=new Size(Theme.S(44),Theme.S(22));}
        public bool Checked {get{return on;}set{if(on==value)return;on=value;Invalidate();var h=Changed;if(h!=null)h(this,EventArgs.Empty);}}
        protected override void OnClick(EventArgs e){Checked=!Checked;base.OnClick(e);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space){Checked=!Checked;e.Handled=true;}base.OnKeyDown(e);}
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;g.Clear(Back);g.SmoothingMode=SmoothingMode.AntiAlias;
            var r=new RectangleF(1,1,Width-2,Height-2);float radius=r.Height/2;
            if(on)Theme.Fill(g,r,hover?Theme.Blend(c.Accent,c.Bg,.12f):c.Accent,radius);
            else{Theme.Fill(g,r,hover?c.Hover:Back,radius);Theme.Stroke(g,r,c.Muted,radius);}
            float knob=r.Height-Theme.S(pressed?7:8),y=r.Y+(r.Height-knob)/2,x=on?r.Right-knob-Theme.S(4):r.X+Theme.S(4);
            using(var b=new SolidBrush(on?c.OnAccent:c.Muted))g.FillEllipse(b,x,y,knob,knob);
            FocusRing(g,Rectangle.Round(r),radius);
        }
    }

    sealed class Segmented : Painted {
        public string[] Items=new string[0];int index;public event EventHandler Changed;
        public Segmented(){Cursor=Cursors.Hand;TabStop=true;Font=Theme.Ui(9);}
        public int SelectedIndex {get{return index;}set{if(index==value)return;index=value;Invalidate();var h=Changed;if(h!=null)h(this,EventArgs.Empty);}}
        int Hit(int x){return Items.Length==0?-1:Math.Max(0,Math.Min(Items.Length-1,x*Items.Length/Math.Max(1,Width)));}
        protected override void OnMouseClick(MouseEventArgs e){SelectedIndex=Hit(e.X);Focus();base.OnMouseClick(e);}
        protected override bool IsInputKey(Keys key){return key==Keys.Left||key==Keys.Right||base.IsInputKey(key);}
        protected override void OnKeyDown(KeyEventArgs e) {
            if(e.KeyCode==Keys.Left&&index>0)SelectedIndex=index-1;
            if(e.KeyCode==Keys.Right&&index<Items.Length-1)SelectedIndex=index+1;
            base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;g.Clear(Back);
            var r=new Rectangle(0,0,Width,Height);float radius=Theme.S(6);
            Theme.Fill(g,r,Theme.Blend(c.Card,c.Text,c.Dark?.05f:.03f),radius);Theme.Stroke(g,r,c.Line,radius);
            for(int i=0;i<Items.Length;i++) {
                int x0=i*Width/Items.Length,x1=(i+1)*Width/Items.Length;var cell=new Rectangle(x0,0,x1-x0,Height);
                if(i==index)Theme.Fill(g,Rectangle.Inflate(cell,-Theme.S(3),-Theme.S(3)),c.Accent,Theme.S(4));
                Theme.Text(g,Items[i],Font,cell,i==index?c.OnAccent:c.Text,TextFormatFlags.HorizontalCenter);
            }
            FocusRing(g,r,radius);
        }
    }

    sealed class Stepper : Painted {
        public string[] Values=new string[0];int index;public event EventHandler Changed;
        public Stepper(){Cursor=Cursors.Hand;TabStop=true;Font=Theme.Ui(9.5f,true);}
        public int Index {get{return index;}set{value=Math.Max(0,Math.Min(Values.Length-1,value));if(index==value)return;index=value;Invalidate();var h=Changed;if(h!=null)h(this,EventArgs.Empty);}}
        protected override void OnMouseClick(MouseEventArgs e){if(e.X<Height)Index--;else if(e.X>Width-Height)Index++;Focus();base.OnMouseClick(e);}
        protected override void OnMouseMove(MouseEventArgs e){Invalidate();base.OnMouseMove(e);}
        protected override bool IsInputKey(Keys key){return key==Keys.Left||key==Keys.Right||base.IsInputKey(key);}
        protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Left)Index--;if(e.KeyCode==Keys.Right)Index++;base.OnKeyDown(e);}
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;g.Clear(Back);
            var r=new Rectangle(0,0,Width,Height);float radius=Theme.S(6);
            Theme.Fill(g,r,Theme.Blend(c.Card,c.Text,c.Dark?.05f:.03f),radius);Theme.Stroke(g,r,c.Line,radius);
            var point=PointToClient(Cursor.Position);
            var minus=new Rectangle(0,0,Height,Height);var plus=new Rectangle(Width-Height,0,Height,Height);
            if(hover&&minus.Contains(point)&&index>0)Theme.Fill(g,Rectangle.Inflate(minus,-Theme.S(3),-Theme.S(3)),c.Hover,Theme.S(4));
            if(hover&&plus.Contains(point)&&index<Values.Length-1)Theme.Fill(g,Rectangle.Inflate(plus,-Theme.S(3),-Theme.S(3)),c.Hover,Theme.S(4));
            Theme.Text(g,"−",Theme.Ui(11),minus,index>0?c.Text:c.Faint,TextFormatFlags.HorizontalCenter);
            Theme.Text(g,"+",Theme.Ui(11),plus,index<Values.Length-1?c.Text:c.Faint,TextFormatFlags.HorizontalCenter);
            if(Values.Length>0)Theme.Text(g,Values[index],Font,new Rectangle(Height,0,Width-2*Height,Height),c.Text,TextFormatFlags.HorizontalCenter);
            FocusRing(g,r,radius);
        }
    }

    enum BannerKind { Info, Success, Warning, Error }
    // In-app message bar: replaces message boxes for anything that does not need a decision.
    sealed class Banner : Painted {
        public BannerKind Kind;public string Message="";public string Primary,Secondary;
        public Action PrimaryAction,SecondaryAction;
        Rectangle primaryBox,secondaryBox,closeBox;string hot;
        public Banner(){Font=Theme.Ui(9);Cursor=Cursors.Default;}
        public Color Tint {get{
            var c=Theme.C;
            switch(Kind){case BannerKind.Success:return c.Ok;case BannerKind.Warning:return c.Warn;case BannerKind.Error:return c.Err;default:return c.Accent;}
        }}
        protected override void OnMouseMove(MouseEventArgs e) {
            string now=primaryBox.Contains(e.Location)?"p":secondaryBox.Contains(e.Location)?"s":closeBox.Contains(e.Location)?"x":null;
            if(now!=hot){hot=now;Cursor=now!=null?Cursors.Hand:Cursors.Default;Invalidate();}
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e){hot=null;base.OnMouseLeave(e);}
        protected override void OnMouseClick(MouseEventArgs e) {
            Action action=null;
            if(primaryBox.Contains(e.Location))action=PrimaryAction;
            else if(secondaryBox.Contains(e.Location))action=SecondaryAction??(Action)(()=>Visible=false);
            else if(closeBox.Contains(e.Location))action=(Action)(()=>Visible=false);
            if(action!=null)action();
            base.OnMouseClick(e);
        }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;g.Clear(Back);
            var r=new Rectangle(0,0,Width,Height);float radius=Theme.S(8);
            Theme.Fill(g,r,Theme.Blend(c.Card,Tint,c.Dark?.16f:.10f),radius);Theme.Stroke(g,r,Theme.Blend(c.Line,Tint,.35f),radius);
            string glyph=Kind==BannerKind.Success?Icons.Completed:Kind==BannerKind.Warning?Icons.Warning:Kind==BannerKind.Error?Icons.Error:Icons.Info;
            Icons.Draw(g,glyph,new Rectangle(Theme.S(12),0,Theme.S(18),Height),16,Tint);
            int right=Width-Theme.S(6);
            closeBox=new Rectangle(right-Theme.S(30),(Height-Theme.S(30))/2,Theme.S(30),Theme.S(30));right=closeBox.X-Theme.S(4);
            if(hot=="x")Theme.Fill(g,closeBox,c.Hover,Theme.S(6));
            Icons.Draw(g,Icons.Cancel,closeBox,10,c.Muted);
            secondaryBox=Rectangle.Empty;primaryBox=Rectangle.Empty;
            var bold=Theme.Ui(9,true);
            if(!String.IsNullOrEmpty(Secondary)){int w=Theme.Measure(Secondary,Font)+Theme.S(24);secondaryBox=new Rectangle(right-w,(Height-Theme.S(30))/2,w,Theme.S(30));right=secondaryBox.X-Theme.S(6);
                Theme.Fill(g,secondaryBox,hot=="s"?c.Hover:Color.FromArgb(0,0,0,0),Theme.S(6));Theme.Stroke(g,secondaryBox,c.Line,Theme.S(6));Theme.Text(g,Secondary,Font,secondaryBox,c.Text,TextFormatFlags.HorizontalCenter);}
            if(!String.IsNullOrEmpty(Primary)){int w=Theme.Measure(Primary,bold)+Theme.S(24);primaryBox=new Rectangle(right-w,(Height-Theme.S(30))/2,w,Theme.S(30));right=primaryBox.X-Theme.S(6);
                Theme.Fill(g,primaryBox,hot=="p"?Theme.Blend(c.Accent,c.Bg,.12f):c.Accent,Theme.S(6));Theme.Text(g,Primary,bold,primaryBox,c.OnAccent,TextFormatFlags.HorizontalCenter);}
            Theme.Text(g,Message,Font,new Rectangle(Theme.S(40),0,Math.Max(0,right-Theme.S(48)),Height),c.Text);
        }
    }

    sealed class MenuRenderer : ToolStripProfessionalRenderer {
        public MenuRenderer():base(new MenuColors()){RoundedEdges=false;}
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(Theme.C.Raised);}
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){using(var p=new Pen(Theme.C.Strong))e.Graphics.DrawRectangle(p,0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1);}
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){}
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
            if(!e.Item.Selected||!e.Item.Enabled)return;
            var r=new Rectangle(Theme.S(4),1,e.Item.Width-Theme.S(8),e.Item.Height-2);
            Theme.Fill(e.Graphics,r,Theme.C.Hover,Theme.S(4));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
            var item=e.Item as ToolStripMenuItem;var c=Theme.C;
            Color color=!e.Item.Enabled?c.Faint:e.Item.Tag as string=="danger"?c.Err:c.Text;
            if(item!=null&&e.Text!=item.Text&&e.Item.Enabled)color=c.Muted;
            TextRenderer.DrawText(e.Graphics,e.Text,e.TextFont,e.TextRectangle,color,e.TextFormat);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
            int y=e.Item.Height/2;using(var p=new Pen(Theme.C.Line))e.Graphics.DrawLine(p,Theme.S(10),y,e.Item.Width-Theme.S(10),y);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            Icons.Draw(e.Graphics,Icons.Check,e.ImageRectangle,12,Theme.C.Accent);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e){e.ArrowColor=Theme.C.Muted;base.OnRenderArrow(e);}
        sealed class MenuColors : ProfessionalColorTable {
            public override Color MenuItemSelected{get{return Theme.C.Hover;}}
            public override Color MenuItemBorder{get{return Color.Transparent;}}
            public override Color ToolStripDropDownBackground{get{return Theme.C.Raised;}}
            public override Color ImageMarginGradientBegin{get{return Theme.C.Raised;}}
            public override Color ImageMarginGradientMiddle{get{return Theme.C.Raised;}}
            public override Color ImageMarginGradientEnd{get{return Theme.C.Raised;}}
            public override Color SeparatorDark{get{return Theme.C.Line;}}
            public override Color SeparatorLight{get{return Theme.C.Line;}}
        }
    }

    static class Menus {
        public static ContextMenuStrip Create() {
            var menu=new ContextMenuStrip{Renderer=new MenuRenderer(),ShowImageMargin=false,ShowCheckMargin=false,Font=Theme.Ui(9.5f),Padding=new Padding(0,Theme.S(4),0,Theme.S(4))};
            menu.Opening+=delegate{menu.BackColor=Theme.C.Raised;};
            return menu;
        }
        public static ToolStripMenuItem Item(ToolStrip menu,string text,Action action,string shortcut=null,bool danger=false) {
            var item=new ToolStripMenuItem(text){ShortcutKeyDisplayString=shortcut,Padding=new Padding(Theme.S(6),Theme.S(5),Theme.S(6),Theme.S(5)),Tag=danger?"danger":null};
            item.Click+=delegate{action();};menu.Items.Add(item);return item;
        }
        public static void Separator(ToolStrip menu){menu.Items.Add(new ToolStripSeparator());}
        public static void Clear(ToolStrip menu){foreach(var item in menu.Items.Cast<ToolStripItem>().ToList())item.Dispose();menu.Items.Clear();}
    }

    // Themed tooltip; the native one stays light in dark mode.
    sealed class Tips : ToolTip {
        public Tips(){OwnerDraw=true;InitialDelay=450;ReshowDelay=80;AutoPopDelay=10000;Draw+=OnDraw;Popup+=OnPopup;}
        void OnPopup(object sender,PopupEventArgs e) {
            var size=TextRenderer.MeasureText(GetToolTip(e.AssociatedControl)??"",Theme.Ui(9),new Size(Theme.S(420),int.MaxValue),TextFormatFlags.WordBreak);
            e.ToolTipSize=new Size(size.Width+Theme.S(20),size.Height+Theme.S(12));
        }
        void OnDraw(object sender,DrawToolTipEventArgs e) {
            var c=Theme.C;e.Graphics.Clear(c.Raised);
            using(var p=new Pen(c.Strong))e.Graphics.DrawRectangle(p,0,0,e.Bounds.Width-1,e.Bounds.Height-1);
            TextRenderer.DrawText(e.Graphics,e.ToolTipText,Theme.Ui(9),new Rectangle(Theme.S(10),Theme.S(6),e.Bounds.Width-Theme.S(20),e.Bounds.Height-Theme.S(12)),c.Text,TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
        }
    }

    // Windows' modern folder picker (IFileOpenDialog), with the classic dialog as a fallback.
    static class FolderPicker {
        [ComImport,Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")] class FileOpenDialogClass {}
        [ComImport,Guid("42f85136-db7e-439c-85f1-e4075d135fc8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count,IntPtr filters);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events,out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
        }
        [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem {
            void BindToHandler(IntPtr bindContext,ref Guid handler,ref Guid id,out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint type,[MarshalAs(UnmanagedType.LPWStr)] out string name);
        }
        [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)]
        static extern void SHCreateItemFromParsingName(string path,IntPtr bindContext,[MarshalAs(UnmanagedType.LPStruct)] Guid id,out IShellItem item);
        public static string Pick(IWin32Window owner,string start,string title) {
            try {
                var dialog=(IFileDialog)new FileOpenDialogClass();
                try {
                    dialog.SetOptions(0x20|0x40|0x800);dialog.SetTitle(title);
                    if(!String.IsNullOrEmpty(start)&&Directory.Exists(start)){IShellItem folder;SHCreateItemFromParsingName(start,IntPtr.Zero,typeof(IShellItem).GUID,out folder);dialog.SetFolder(folder);}
                    int hr=dialog.Show(owner==null?IntPtr.Zero:owner.Handle);
                    if(hr!=0)return null;
                    IShellItem result;dialog.GetResult(out result);string path;result.GetDisplayName(0x80058000,out path);
                    return path;
                } finally { Marshal.ReleaseComObject(dialog); }
            } catch(Exception) {
                using(var classic=new FolderBrowserDialog{SelectedPath=start,Description=title})return classic.ShowDialog(owner)==DialogResult.OK?classic.SelectedPath:null;
            }
        }
    }

    // Brings a window of this app to the front when the user asked for it from another program (a
    // download clicked in Chrome). Plain SetForegroundWindow is refused there and only flashes the
    // taskbar button, so the call is made with input attached to the window that is in front.
    static class Foreground {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,IntPtr process);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach,uint to,bool on);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hwnd);
        public static void Bring(IntPtr hwnd) {
            if(hwnd==IntPtr.Zero||GetForegroundWindow()==hwnd)return;
            if(SetForegroundWindow(hwnd)&&GetForegroundWindow()==hwnd)return;
            uint front=GetWindowThreadProcessId(GetForegroundWindow(),IntPtr.Zero),me=GetCurrentThreadId();
            bool attached=front!=0&&front!=me&&AttachThreadInput(me,front,true);
            try{BringWindowToTop(hwnd);SetForegroundWindow(hwnd);}
            finally{if(attached)AttachThreadInput(me,front,false);}
        }
    }

    // Windows shows a new window before its first paint, which flashes white on the dark theme. The
    // window stays cloaked (kept off screen by DWM) until it has painted once.
    static class NoFlash {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        public static void Attach(Form form) {
            form.HandleCreated+=delegate{Cloak(form.Handle,true);};
            form.Shown+=delegate{Reveal(form);};
        }
        public static void Cloak(IntPtr handle,bool on){try{int value=on?1:0;DwmSetWindowAttribute(handle,13,ref value,4);}catch(Exception){}}
        public static void Reveal(Form form) {
            form.Update();
            form.BeginInvoke((Action)(()=>{if(form.IsDisposed)return;form.Update();Cloak(form.Handle,false);}));
        }
    }

    static class Links {
        static readonly Regex url=new Regex(@"https?://[^\s""'<>]+",RegexOptions.IgnoreCase);
        public static List<string> Extract(string text) {
            var found=new List<string>();if(String.IsNullOrEmpty(text))return found;
            foreach(Match m in url.Matches(text)){string link=m.Value.TrimEnd('.',',',';',')',']','}','>');if(!found.Contains(link))found.Add(link);}
            return found;
        }
        // Links dropped or pasted as files: .txt lists and .url shortcuts.
        public static List<string> FromFiles(IEnumerable<string> files) {
            var found=new List<string>();
            foreach(string file in files) {
                try {
                    var info=new FileInfo(file);if(!info.Exists||info.Length>4*1024*1024)continue;
                    string ext=info.Extension.ToLowerInvariant();
                    if(ext==".url"){foreach(string line in File.ReadAllLines(file))if(line.StartsWith("URL=",StringComparison.OrdinalIgnoreCase))found.AddRange(Extract(line.Substring(4)));}
                    else if(ext==".txt"||ext==".csv"||ext==".lst")found.AddRange(Extract(File.ReadAllText(file)));
                } catch(IOException){} catch(UnauthorizedAccessException){}
            }
            return found.Distinct().ToList();
        }
    }
}
