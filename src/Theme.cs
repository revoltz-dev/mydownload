using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MyDownload {
    sealed class Palette {
        public bool Dark;
        public Color Bg, Card, Raised, Text, Muted, Faint, Line, Strong, Accent, OnAccent, AccentSoft, Ok, Warn, Err, Hover, Pressed;
    }

    static class Theme {
        public static readonly Palette Light=new Palette{Dark=false,Bg=Hex(0xf3f3f3),Card=Hex(0xffffff),Raised=Hex(0xf9f9f9),Text=Hex(0x1a1a1a),Muted=Hex(0x5f6368),
            Faint=Hex(0x8a8a8a),Line=Hex(0xe3e3e3),Strong=Hex(0xcfcfcf),Accent=Hex(0x0067c0),OnAccent=Hex(0xffffff),AccentSoft=Hex(0xdcebf9),
            Ok=Hex(0x0f7b0f),Warn=Hex(0x9d5d00),Err=Hex(0xc42b1c),Hover=Hex(0xebebeb),Pressed=Hex(0xe2e2e2)};
        public static readonly Palette Dark=new Palette{Dark=true,Bg=Hex(0x202020),Card=Hex(0x2b2b2b),Raised=Hex(0x323232),Text=Hex(0xf3f3f3),Muted=Hex(0xa6a6a6),
            Faint=Hex(0x7c7c7c),Line=Hex(0x3b3b3b),Strong=Hex(0x4d4d4d),Accent=Hex(0x4cc2ff),OnAccent=Hex(0x0b0b0b),AccentSoft=Hex(0x1d3a4f),
            Ok=Hex(0x6ccb5f),Warn=Hex(0xfce100),Err=Hex(0xff99a4),Hover=Hex(0x373737),Pressed=Hex(0x404040)};
        public static Palette C=Dark;
        public static string Mode="dark";
        public static event EventHandler Changed;
        public static void Apply(string mode) {
            Mode=mode;bool dark=mode=="dark"||(mode=="system"&&SystemDark());
            C=dark?Dark:Light;
            var handler=Changed;if(handler!=null)handler(null,EventArgs.Empty);
        }
        public static bool SystemDark() {
            try {
                using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
                    object value=key==null?null:key.GetValue("AppsUseLightTheme");
                    return value is int&&(int)value==0;
                }
            } catch(Exception){return false;}
        }

        public static float F=1f;
        public static int S(float value){return (int)Math.Round(value*F);}
        public static void InitScale(){using(var g=Graphics.FromHwnd(IntPtr.Zero))F=Math.Max(1f,g.DpiX/96f);}

        static readonly Dictionary<string,Font> fonts=new Dictionary<string,Font>();
        static string regular,semibold,icons;
        static string Pick(params string[] names) {
            foreach(string name in names){try{using(var f=new FontFamily(name))return name;}catch(ArgumentException){}}
            return SystemFonts.MessageBoxFont.FontFamily.Name;
        }
        public static Font Ui(float points,bool bold=false) {
            if(regular==null){regular=Pick("Segoe UI Variable Text","Segoe UI");semibold=Pick("Segoe UI Variable Text Semibold","Segoe UI Semibold");}
            string key=(bold?"b":"r")+points;Font font;
            if(!fonts.TryGetValue(key,out font)){
                font=bold&&semibold==regular?new Font(regular,points,FontStyle.Bold):new Font(bold?semibold:regular,points);
                fonts[key]=font;
            }
            return font;
        }
        public static string IconFamily {get{if(icons==null)icons=Pick("Segoe Fluent Icons","Segoe MDL2 Assets");return icons;}}
        public static Font IconFont(float pixels) {
            string key="i"+pixels;Font font;
            if(!fonts.TryGetValue(key,out font)){font=new Font(IconFamily,pixels*F,FontStyle.Regular,GraphicsUnit.Pixel);fonts[key]=font;}
            return font;
        }

        public static Color Hex(int rgb){return Color.FromArgb((rgb>>16)&255,(rgb>>8)&255,rgb&255);}
        public static Color Blend(Color a,Color b,float t) {
            t=Math.Max(0,Math.Min(1,t));
            return Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));
        }
        public static GraphicsPath Round(RectangleF r,float radius) {
            var p=new GraphicsPath();
            if(radius<=0){p.AddRectangle(r);return p;}
            float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
            p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
        }
        public static void Fill(Graphics g,RectangleF r,Color color,float radius) {
            var mode=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var p=Round(r,radius))using(var b=new SolidBrush(color))g.FillPath(b,p);
            g.SmoothingMode=mode;
        }
        public static void Stroke(Graphics g,RectangleF r,Color color,float radius,float width=1) {
            var mode=g.SmoothingMode;g.SmoothingMode=SmoothingMode.AntiAlias;
            var inset=new RectangleF(r.X+width/2,r.Y+width/2,r.Width-width,r.Height-width);
            using(var p=Round(inset,radius))using(var pen=new Pen(color,width))g.DrawPath(pen,p);
            g.SmoothingMode=mode;
        }
        public const TextFormatFlags Line1=TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding;
        public static void Text(Graphics g,string text,Font font,Rectangle r,Color color,TextFormatFlags flags=TextFormatFlags.Left) {
            TextRenderer.DrawText(g,text,font,r,color,flags|Line1);
        }
        public static int Measure(string text,Font font) {
            return TextRenderer.MeasureText(text,font,new Size(int.MaxValue,int.MaxValue),Line1).Width;
        }
        // Draws consecutive pieces of text in different colors on one line.
        public static void Pieces(Graphics g,Rectangle r,Font font,params object[] pieces) {
            int x=r.X;
            for(int i=0;i+1<pieces.Length;i+=2) {
                string text=(string)pieces[i];if(String.IsNullOrEmpty(text))continue;
                int width=Measure(text,font);int room=r.Right-x;if(room<=8)break;
                TextRenderer.DrawText(g,text,font,new Rectangle(x,r.Y,Math.Min(width,room),r.Height),(Color)pieces[i+1],Line1);
                x+=width;
            }
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
        // Native caption for dialogs: dark or light frame and a caption colored like the window.
        public static void Frame(Form form) {
            if(!form.IsHandleCreated)return;
            try {
                int dark=C.Dark?1:0;DwmSetWindowAttribute(form.Handle,20,ref dark,4);
                int caption=ColorTranslator.ToWin32(C.Bg);DwmSetWindowAttribute(form.Handle,35,ref caption,4);
                int text=ColorTranslator.ToWin32(C.Text);DwmSetWindowAttribute(form.Handle,36,ref text,4);
            } catch(Exception){}
        }
        public static void Rounded(IntPtr handle) {
            try{int round=2;DwmSetWindowAttribute(handle,33,ref round,4);}catch(Exception){}
        }
    }
}
