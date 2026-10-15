using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MyDownload {
    // Small always-on-top window with the downloads in progress.
    sealed class MiniWindow : Form {
        readonly DownloadCoordinator manager;readonly Settings settings;
        public event EventHandler ExpandRequested,CloseRequested;
        Rectangle expandBox,closeBox;string hot;
        public MiniWindow(DownloadCoordinator manager,Settings settings) {
            this.manager=manager;this.settings=settings;
            Text=AppPaths.Name+" mini";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
            AutoScaleMode=AutoScaleMode.None;DoubleBuffered=true;Icon=Program.AppIcon;Width=Theme.S(300);Height=Theme.S(96);
            Theme.Changed+=OnTheme;BackColor=Theme.C.Bg;NoFlash.Attach(this);
            LocationChanged+=delegate{if(Visible&&WindowState==FormWindowState.Normal)settings.Mini=new[]{Left,Top};};
        }
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ClassStyle|=0x20000;p.ExStyle|=0x80;return p;}}
        protected override bool ShowWithoutActivation {get{return true;}}
        void OnTheme(object sender,EventArgs e){BackColor=Theme.C.Bg;Invalidate();}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Theme.Rounded(Handle);}
        protected override void Dispose(bool disposing){if(disposing)Theme.Changed-=OnTheme;base.Dispose(disposing);}
        public void Place() {
            var area=Screen.FromPoint(Cursor.Position).WorkingArea;
            if(settings.Mini!=null&&settings.Mini.Length==2) {
                var spot=new Rectangle(settings.Mini[0],settings.Mini[1],Width,Height);
                if(Screen.AllScreens.Any(s=>s.WorkingArea.IntersectsWith(spot))){Location=spot.Location;return;}
            }
            Location=new Point(area.Right-Width-Theme.S(16),area.Bottom-Height-Theme.S(16));
        }
        public void RefreshData() {
            int count=Math.Max(1,Math.Min(4,manager.Visible.Count(i=>i.Active||i.Phase=="Queued")));
            int height=Theme.S(50)+count*Theme.S(44)+Theme.S(8);
            if(height!=Height){int bottom=Bottom;Height=height;if(Visible)Top=bottom-height;}
            Invalidate();
        }
        protected override void WndProc(ref Message m) {
            if(m.Msg==0x84) {
                base.WndProc(ref m);
                var p=PointToClient(new Point((short)(m.LParam.ToInt64()&0xffff),(short)((m.LParam.ToInt64()>>16)&0xffff)));
                if(!expandBox.Contains(p)&&!closeBox.Contains(p))m.Result=(IntPtr)2;
                return;
            }
            if(m.Msg==0xA3){var h=ExpandRequested;if(h!=null)h(this,EventArgs.Empty);return;}
            base.WndProc(ref m);
        }
        protected override void OnMouseMove(MouseEventArgs e) {
            string now=expandBox.Contains(e.Location)?"e":closeBox.Contains(e.Location)?"x":null;
            if(now!=hot){hot=now;Invalidate();}
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e){hot=null;Invalidate();base.OnMouseLeave(e);}
        protected override void OnMouseClick(MouseEventArgs e) {
            EventHandler handler=expandBox.Contains(e.Location)?ExpandRequested:closeBox.Contains(e.Location)?CloseRequested:null;
            if(handler!=null)handler(this,EventArgs.Empty);
            base.OnMouseClick(e);
        }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;g.Clear(c.Bg);
            using(var pen=new Pen(c.Strong))g.DrawRectangle(pen,0,0,Width-1,Height-1);
            int pad=Theme.S(14),size=Theme.S(30);
            closeBox=new Rectangle(Width-Theme.S(8)-size,Theme.S(10),size,size);
            expandBox=new Rectangle(closeBox.X-Theme.S(2)-size,Theme.S(10),size,size);
            if(hot=="e")Theme.Fill(g,expandBox,c.Hover,Theme.S(6));
            if(hot=="x")Theme.Fill(g,closeBox,c.Hover,Theme.S(6));
            Icons.Draw(g,Icons.Expand,expandBox,13,c.Muted);Icons.Draw(g,Icons.Cancel,closeBox,11,c.Muted);
            var active=manager.Visible.Where(i=>i.Active||i.Phase=="Queued").OrderBy(Describe.Order).Take(4).ToList();
            Icons.Draw(g,Icons.Download,new Rectangle(pad,Theme.S(10),Theme.S(20),size),15,c.Accent);
            int running=manager.ActiveCount;
            string head=running>0?Units.Size(manager.TotalRate)+"/s":"Em pausa";
            Theme.Text(g,head,Theme.Ui(10.5f,true),new Rectangle(pad+Theme.S(28),Theme.S(10),Theme.S(120),size),c.Text);
            Theme.Text(g,running>0?running+" baixando":"",Theme.Ui(8.5f),new Rectangle(pad+Theme.S(130),Theme.S(10),expandBox.X-pad-Theme.S(134),size),c.Muted);
            int y=Theme.S(50);
            if(active.Count==0)Theme.Text(g,"Nenhum download em andamento",Theme.Ui(9),new Rectangle(pad,y,Width-2*pad,Theme.S(36)),c.Muted);
            foreach(var item in active) {
                string percent=item.Job.Total>0?Math.Floor(item.Fraction*100)+"%":Units.Size(item.Bytes);
                int pw=Theme.Measure(percent,Theme.Ui(9,true))+Theme.S(4);
                Theme.Text(g,item.Title,Theme.Ui(9),new Rectangle(pad,y,Width-2*pad-pw-Theme.S(8),Theme.S(22)),c.Text);
                Theme.Text(g,percent,Theme.Ui(9,true),new Rectangle(Width-pad-pw,y,pw,Theme.S(22)),Describe.Tint(item),TextFormatFlags.Right);
                var track=new Rectangle(pad,y+Theme.S(26),Width-2*pad,Theme.S(4));
                Theme.Fill(g,track,c.Line,Theme.S(2));
                if(item.Fraction>0)Theme.Fill(g,new Rectangle(track.X,track.Y,Math.Max(Theme.S(4),(int)(track.Width*item.Fraction)),track.Height),Describe.Tint(item),Theme.S(2));
                y+=Theme.S(44);
            }
        }
    }
}
