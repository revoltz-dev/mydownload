using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace MyDownload {
    // Opening screen. Each step does real work (it runs while its name is shown); the bar never
    // gets ahead of the work and takes at least the minimum time, so the steps can be read.
    sealed class SplashWindow : Form {
        readonly string[] labels;readonly Bitmap logo;readonly Stopwatch clock=Stopwatch.StartNew();
        readonly int minimum;
        double done,shown;string status;
        SplashWindow(string[] labels,int minimum) {
            this.labels=labels;this.minimum=minimum;status=labels.Length>0?labels[0]:"";
            Text=AppPaths.Name;FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterScreen;ShowInTaskbar=true;Icon=Program.AppIcon;
            AutoScaleMode=AutoScaleMode.None;DoubleBuffered=true;BackColor=Theme.C.Bg;Size=new Size(Theme.S(440),Theme.S(280));
            logo=Program.IconImage(Theme.S(76));
            NoFlash.Attach(this);
        }
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ClassStyle|=0x20000;return p;}}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Theme.Rounded(Handle);}
        protected override void Dispose(bool disposing){if(disposing)logo.Dispose();base.Dispose(disposing);}
        void Frame() {
            // Eases towards the smaller of "work done" and "time elapsed".
            double time=minimum<=0?1:Math.Min(1,clock.ElapsedMilliseconds/(double)minimum);
            double goal=Math.Min(done,time);
            shown+=Math.Max(0,goal-shown)*0.18;if(goal-shown<0.002||minimum<=0)shown=goal;
            int step=(int)Math.Min(labels.Length-1,Math.Floor(shown*labels.Length));
            status=done>=1&&shown>=0.999?"Pronto":labels[Math.Max(0,step)];
            Invalidate();Update();Application.DoEvents();
        }
        void Run(Action[] work) {
            Show();Frame();
            for(int i=0;i<work.Length;i++){work[i]();done=(i+1)/(double)work.Length;Frame();}
            while(shown<0.999){Frame();Thread.Sleep(15);}
            Thread.Sleep(120);Frame();
        }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;g.Clear(c.Bg);
            using(var pen=new Pen(c.Strong))g.DrawRectangle(pen,0,0,Width-1,Height-1);
            g.DrawImage(logo,(Width-logo.Width)/2,Theme.S(40));
            Theme.Text(g,AppPaths.Name,Theme.Ui(16,true),new Rectangle(0,Theme.S(128),Width,Theme.S(36)),c.Text,TextFormatFlags.HorizontalCenter);
            Theme.Text(g,status,Theme.Ui(9.5f),new Rectangle(0,Theme.S(168),Width,Theme.S(24)),c.Muted,TextFormatFlags.HorizontalCenter);
            var track=new Rectangle((Width-Theme.S(280))/2,Theme.S(212),Theme.S(280),Theme.S(4));
            Theme.Fill(g,track,c.Line,track.Height/2f);
            int fill=(int)Math.Round(track.Width*shown);
            if(fill>=track.Height)Theme.Fill(g,new Rectangle(track.X,track.Y,fill,track.Height),c.Accent,track.Height/2f);
            Theme.Text(g,"versão 3.0",Theme.Ui(8),new Rectangle(0,Height-Theme.S(34),Width-Theme.S(16),Theme.S(24)),c.Faint,TextFormatFlags.Right);
        }
        // Renders the splash half way to a PNG (screenshots and checks).
        public static void Preview(string path) {
            using(var splash=new SplashWindow(new[]{"Conferindo a instalação…","Lendo o histórico de downloads…","Preparando a janela…"},0)) {
                splash.StartPosition=FormStartPosition.Manual;splash.Location=new Point(-20000,-20000);splash.ShowInTaskbar=false;
                splash.done=0.5;splash.shown=0.45;splash.status="Lendo o histórico de downloads…";splash.Show();Application.DoEvents();
                using(var image=new Bitmap(splash.Width,splash.Height)){splash.DrawToBitmap(image,new Rectangle(0,0,splash.Width,splash.Height));image.Save(path);}
            }
        }
        // Runs the steps behind the splash and closes it when done.
        public static void Show(int minimumMs,string[] labels,Action[] work) {
            using(var splash=new SplashWindow(labels,minimumMs))splash.Run(work);
        }
    }
}
