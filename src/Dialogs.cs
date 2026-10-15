using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MyDownload {
    // Themed replacement for MessageBox and input boxes.
    sealed class Dialog : Form {
        readonly string heading,message;string error;
        readonly FlatButton ok,cancel;readonly CardPanel field;readonly PasteBox input;
        readonly Func<string,string> validate;
        readonly Toggle option;readonly string optionText;
        int messageHeight;
        public string Value {get{return input==null?null:input.Text.Trim();}}
        Dialog(string title,string heading,string message,string okText,string cancelText,bool danger,bool withInput,string placeholder,string initial,Func<string,string> validate,string optionText=null) {
            this.optionText=optionText;
            this.heading=heading;this.message=message;this.validate=validate;
            Text=title;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
            StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.None;KeyPreview=true;Font=Theme.Ui(9.5f);DoubleBuffered=true;
            int width=Theme.S(480),pad=Theme.S(24);
            messageHeight=String.IsNullOrEmpty(message)?0:TextRenderer.MeasureText(message,Theme.Ui(9.5f),new Size(width-2*pad,int.MaxValue),TextFormatFlags.WordBreak).Height;
            int y=pad+Theme.S(30)+messageHeight+Theme.S(16);
            if(withInput) {
                field=new CardPanel{Bounds=new Rectangle(pad,y,width-2*pad,Theme.S(40)),Underline=true};
                input=new PasteBox{Cue=placeholder,Text=initial??""};field.Controls.Add(input);field.FocusSource=input;
                input.Bounds=new Rectangle(Theme.S(12),(field.Height-input.Height)/2,field.Width-Theme.S(24),input.Height);
                input.GotFocus+=delegate{field.Invalidate();};input.LostFocus+=delegate{field.Invalidate();};
                input.TextChanged+=delegate{if(error!=null){error=null;Invalidate();}};
                Controls.Add(field);y+=field.Height+Theme.S(30);
            }
            if(optionText!=null){option=new Toggle{Location=new Point(pad,y+Theme.S(8))};Controls.Add(option);y+=Theme.S(58);}
            ok=new FlatButton{Text=okText,Kind=danger?ButtonKind.Danger:ButtonKind.Primary,Font=Theme.Ui(9.5f,true)};
            ok.Size=new Size(Math.Max(Theme.S(96),ok.Preferred(18)),Theme.S(34));
            ok.Click+=delegate{Accept();};Controls.Add(ok);
            int right=width-pad;ok.Location=new Point(right-ok.Width,y);right=ok.Left-Theme.S(8);
            if(cancelText!=null) {
                cancel=new FlatButton{Text=cancelText,Kind=ButtonKind.Standard};
                cancel.Size=new Size(Math.Max(Theme.S(96),cancel.Preferred(18)),Theme.S(34));
                cancel.Location=new Point(right-cancel.Width,y);cancel.Click+=delegate{DialogResult=DialogResult.Cancel;};Controls.Add(cancel);
            }
            ClientSize=new Size(width,y+ok.Height+pad);
            Theme.Changed+=OnTheme;ApplyTheme();NoFlash.Attach(this);
            Shown+=delegate{if(input!=null){input.Focus();input.SelectAll();}else ok.Focus();};
        }
        void OnTheme(object sender,EventArgs e){ApplyTheme();}
        void ApplyTheme() {
            BackColor=Theme.C.Bg;
            if(field!=null){field.BackColor=Theme.C.Card;input.BackColor=Theme.C.Card;input.ForeColor=Theme.C.Text;}
            Theme.Frame(this);Invalidate(true);
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Theme.Frame(this);}
        protected override void Dispose(bool disposing){if(disposing)Theme.Changed-=OnTheme;base.Dispose(disposing);}
        void Accept() {
            if(validate!=null){error=validate(Value);if(error!=null){Invalidate();if(input!=null)input.Focus();return;}}
            DialogResult=DialogResult.OK;
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData) {
            if(keyData==Keys.Enter&&!(ActiveControl is FlatButton)){Accept();return true;}
            if(keyData==Keys.Escape){DialogResult=DialogResult.Cancel;return true;}
            return base.ProcessCmdKey(ref msg,keyData);
        }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;int pad=Theme.S(24);
            Theme.Text(g,heading,Theme.Ui(12.5f,true),new Rectangle(pad,pad-Theme.S(2),ClientSize.Width-2*pad,Theme.S(30)),c.Text);
            if(messageHeight>0)TextRenderer.DrawText(g,message,Theme.Ui(9.5f),new Rectangle(pad,pad+Theme.S(32),ClientSize.Width-2*pad,messageHeight),c.Muted,TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
            if(error!=null&&field!=null)Theme.Text(g,error,Theme.Ui(9),new Rectangle(pad,field.Bottom+Theme.S(4),ClientSize.Width-2*pad,Theme.S(22)),c.Err);
            if(option!=null)TextRenderer.DrawText(g,optionText,Font,new Rectangle(option.Right+Theme.S(12),option.Top-Theme.S(9),ClientSize.Width-option.Right-Theme.S(12)-pad,Theme.S(42)),c.Text,TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter);
        }
        DialogResult Run(IWin32Window owner) {
            // Without an owner (the uninstaller started by Windows) it stands on its own.
            if(owner==null){StartPosition=FormStartPosition.CenterScreen;ShowInTaskbar=true;Icon=Program.AppIcon;}
            return ShowDialog(owner);
        }
        public static bool Confirm(IWin32Window owner,string title,string heading,string message,string okText,bool danger=false) {
            using(var d=new Dialog(title,heading,message,okText,"Cancelar",danger,false,null,null,null))return d.Run(owner)==DialogResult.OK;
        }
        public static bool ConfirmOption(IWin32Window owner,string title,string heading,string message,string okText,bool danger,string optionText,out bool chosen) {
            using(var d=new Dialog(title,heading,message,okText,"Cancelar",danger,false,null,null,null,optionText)) {
                bool ok=d.Run(owner)==DialogResult.OK;chosen=ok&&d.option.Checked;return ok;
            }
        }
        public static void Inform(IWin32Window owner,string title,string heading,string message) {
            using(var d=new Dialog(title,heading,message,"OK",null,false,false,null,null,null))d.Run(owner);
        }
        public static string Prompt(IWin32Window owner,string title,string heading,string message,string placeholder,string initial,string okText,Func<string,string> validate) {
            using(var d=new Dialog(title,heading,message,okText,"Cancelar",false,true,placeholder,initial,validate))return d.Run(owner)==DialogResult.OK?d.Value:null;
        }
    }

    // Extracts the extension, opens its folder and explains the three clicks Chrome requires
    // (Chrome does not let programs install extensions by themselves).
    static class ChromeGuide {
        public static bool Run(IWin32Window owner) {
            string folder;
            try{folder=ChromeIntegration.Install();}
            catch(Exception e){Dialog.Inform(owner,"Extensão do Chrome","Não foi possível preparar a extensão",e.Message);return false;}
            try{Clipboard.SetText(folder);}catch(System.Runtime.InteropServices.ExternalException){}
            try{Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception){}
            Dialog.Inform(owner,"Extensão do Chrome","Falta só instalar no Chrome",Steps(folder));
            return true;
        }
        public static string Steps(string folder) {
            return "A pasta da extensão abriu no Explorer e o caminho dela já está copiado.\n\n"+
                "1. No Chrome, digite chrome://extensions na barra de endereço e aperte Enter.\n"+
                "2. Ligue o “Modo do desenvolvedor”, no canto superior direito.\n"+
                "3. Clique em “Carregar sem compactação”, cole o caminho na barra de cima da janela e clique em “Selecionar pasta”:\n"+
                folder+"\n"+
                "4. Pronto. Os downloads do Chrome passam a vir pra cá. Arquivos com menos de 1 MB e links que precisam de login continuam no Chrome.\n\n"+
                "No Edge é igual, em edge://extensions. Não apague essa pasta: o Chrome usa a extensão direto dela.";
        }
    }

    static class Startup {
        const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool Enabled {
            get {try{using(var key=Registry.CurrentUser.OpenSubKey(Key))return key!=null&&key.GetValue(AppPaths.Name)!=null;}catch(Exception){return false;}}
        }
        public static void Set(bool on,string exe=null) {
            using(var key=Registry.CurrentUser.CreateSubKey(Key)) {
                if(on)key.SetValue(AppPaths.Name,"\""+(exe??Application.ExecutablePath)+"\" --tray");
                else key.DeleteValue(AppPaths.Name,false);
            }
        }
    }

    // One row of the settings window: title and description on the left, the control on the right.
    sealed class SettingRow : Panel {
        public string Title,Description;public readonly Control Editor;
        public SettingRow(string title,string description,Control editor,int editorWidth,int editorHeight) {
            Title=title;Description=description;Editor=editor;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            editor.Size=new Size(editorWidth,editorHeight);Controls.Add(editor);
        }
        protected override void OnLayout(LayoutEventArgs e){Editor.Location=new Point(Width-Theme.S(16)-Editor.Width,(Height-Editor.Height)/2);base.OnLayout(e);}
        protected override void OnPaintBackground(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;g.Clear(Parent!=null?Parent.BackColor:c.Bg);
            var r=new Rectangle(0,0,Width,Height);Theme.Fill(g,r,BackColor,Theme.S(6));Theme.Stroke(g,r,c.Line,Theme.S(6));
            int textWidth=Editor.Left-Theme.S(32);
            Theme.Text(g,Title,Theme.Ui(9.5f,true),new Rectangle(Theme.S(16),Theme.S(10),textWidth,Theme.S(22)),c.Text);
            Theme.Text(g,Description,Theme.Ui(8.5f),new Rectangle(Theme.S(16),Theme.S(32),textWidth,Theme.S(20)),c.Muted);
        }
    }

    sealed class SettingsWindow : Form {
        static readonly long[] Limits={0,512000,1000000,2000000,5000000,10000000,20000000,50000000};
        static readonly string[] LimitNames={"Sem limite","512 KB/s","1 MB/s","2 MB/s","5 MB/s","10 MB/s","20 MB/s","50 MB/s"};
        static readonly string[] Themes={"dark","light","system"};
        readonly Settings settings;readonly DownloadCoordinator manager;readonly Action changed;
        readonly List<SettingRow> rows=new List<SettingRow>();
        readonly FlatButton about,close;
        SettingRow folderRow;
        readonly Action<bool> SetShutdown;
        public SettingsWindow(Settings settings,DownloadCoordinator manager,Action changed,bool shutdownArmed,Action<bool> setShutdown,Action install) {
            this.settings=settings;this.manager=manager;this.changed=changed;SetShutdown=setShutdown;
            Text="Configurações — "+AppPaths.Name;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
            StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.None;KeyPreview=true;Font=Theme.Ui(9.5f);DoubleBuffered=true;Icon=Program.AppIcon;
            int pad=Theme.S(24),column=Theme.S(400),gap=Theme.S(20),rowHeight=Theme.S(62),rowGap=Theme.S(6),top=pad+Theme.S(34);

            var browse=new FlatButton{Text="Alterar",Kind=ButtonKind.Standard};
            browse.Click+=delegate{string picked=FolderPicker.Pick(this,settings.Folder,"Pasta para salvar os downloads");if(picked!=null){settings.Folder=picked;settings.Remember(picked);folderRow.Description=picked;folderRow.Invalidate();Save();}};
            folderRow=Row(0,"Salvar em",settings.Folder,browse,Theme.S(90),Theme.S(32));
            var ask=new Toggle{Checked=settings.AskFolder};ask.Changed+=delegate{settings.AskFolder=ask.Checked;Save();};
            Row(0,"Perguntar onde salvar","Desligado: salva direto na pasta acima",ask,Theme.S(44),Theme.S(22));
            var organize=new Toggle{Checked=settings.Organize};organize.Changed+=delegate{settings.Organize=organize.Checked;Save();};
            Row(0,"Organizar por tipo","Subpastas Videos, Music, Pictures, Programs…",organize,Theme.S(44),Theme.S(22));
            var active=new Stepper{Values=Enumerable.Range(1,8).Select(i=>i.ToString()).ToArray()};active.Index=settings.MaxActive-1;
            active.Changed+=delegate{settings.MaxActive=active.Index+1;Save();};
            Row(0,"Downloads ao mesmo tempo","Os outros esperam na fila",active,Theme.S(112),Theme.S(32));
            var connections=new Segmented{Items=Settings.ConnectionChoices.Select(i=>i.ToString()).ToArray()};connections.SelectedIndex=Array.IndexOf(Settings.ConnectionChoices,settings.Connections);
            connections.Changed+=delegate{settings.Connections=Settings.ConnectionChoices[connections.SelectedIndex];Save();};
            Row(0,"Conexões por arquivo","Mais conexões costumam baixar mais rápido",connections,Theme.S(170),Theme.S(32));
            var limit=new Stepper{Values=LimitNames};limit.Index=Nearest(settings.SpeedLimit);
            limit.Changed+=delegate{settings.SpeedLimit=Limits[limit.Index];Save();};
            Row(0,"Limite de velocidade","Vale para todos os downloads juntos",limit,Theme.S(150),Theme.S(32));
            var chrome=new FlatButton{Text=ChromeIntegration.Registered?"Reinstalar":"Instalar",Kind=ButtonKind.Standard};
            chrome.Click+=delegate{if(ChromeGuide.Run(this))chrome.Text="Reinstalar";};
            Row(0,"Downloads do Chrome","Manda os downloads do Chrome pra cá",chrome,Theme.S(100),Theme.S(32));

            var theme=new Segmented{Items=new[]{"Escuro","Claro","Sistema"}};theme.SelectedIndex=Math.Max(0,Array.IndexOf(Themes,settings.Theme));
            theme.Changed+=delegate{settings.Theme=Themes[theme.SelectedIndex];Save();Theme.Apply(settings.Theme);};
            Row(1,"Tema","Escuro é o padrão",theme,Theme.S(200),Theme.S(32));
            var clipboard=new Toggle{Checked=settings.WatchClipboard};clipboard.Changed+=delegate{settings.WatchClipboard=clipboard.Checked;Save();};
            Row(1,"Detectar links copiados","Oferece baixar quando você copia um link de arquivo",clipboard,Theme.S(44),Theme.S(22));
            var notify=new Toggle{Checked=settings.Notify};notify.Changed+=delegate{settings.Notify=notify.Checked;Save();};
            Row(1,"Avisar quando terminar","Notificação do Windows ao concluir ou falhar",notify,Theme.S(44),Theme.S(22));
            var tray=new Toggle{Checked=settings.CloseToTray};tray.Changed+=delegate{settings.CloseToTray=tray.Checked;Save();};
            Row(1,"Fechar mantém na bandeja","Os downloads continuam com a janela fechada",tray,Theme.S(44),Theme.S(22));
            var startup=new Toggle{Checked=Startup.Enabled};
            startup.Changed+=delegate{try{Startup.Set(startup.Checked);}catch(Exception){}};
            Row(1,"Iniciar com o Windows","Abre na bandeja e retoma os downloads",startup,Theme.S(44),Theme.S(22));
            var shutdown=new Toggle{Checked=shutdownArmed};shutdown.Changed+=delegate{if(SetShutdown!=null)SetShutdown(shutdown.Checked);};
            Row(1,"Desligar o PC quando tudo terminar","Só nesta sessão; dá para cancelar em 60 s",shutdown,Theme.S(44),Theme.S(22));
            var hash=new Toggle{Checked=settings.HashOnComplete};hash.Changed+=delegate{settings.HashOnComplete=hash.Checked;Save();};
            Row(1,"Calcular SHA-256 ao concluir","Relê o arquivo inteiro depois de baixar",hash,Theme.S(44),Theme.S(22));
            bool installed=Installer.Installed,outdated=installed&&Installer.Outdated;
            var setup=new FlatButton{Text=!installed?"Instalar":outdated?"Atualizar":"Desinstalar",Kind=installed&&!outdated?ButtonKind.Outline:ButtonKind.Standard};
            setup.Click+=delegate{
                if(installed&&!outdated){try{Process.Start(new ProcessStartInfo(Installer.InstalledExe,(AppPaths.Custom?"--data \""+AppPaths.Root+"\" ":"")+"--uninstall"){UseShellExecute=false});}catch(Exception){}Close();return;}
                if(install!=null)install();Close();
            };
            Row(1,"Instalação no Windows",installed?"Instalado em "+Installer.Folder:"Menu Iniciar, área de trabalho e desinstalar pelo Windows",setup,Theme.S(110),Theme.S(32));

            int count=Math.Max(rows.Count(r=>(string)r.Tag=="0"),rows.Count(r=>(string)r.Tag=="1"));
            int footer=top+count*(rowHeight+rowGap)+Theme.S(12);
            about=new FlatButton{Text="Sobre e licenças",Kind=ButtonKind.Subtle,Glyph=Icons.Info,GlyphSize=13};
            about.Size=new Size(about.Preferred(12),Theme.S(34));about.Location=new Point(pad-Theme.S(8),footer);
            about.Click+=delegate{Program.ShowAbout(this);};Controls.Add(about);
            close=new FlatButton{Text="Fechar",Kind=ButtonKind.Primary,Font=Theme.Ui(9.5f,true)};
            close.Size=new Size(Theme.S(110),Theme.S(34));close.Location=new Point(pad+2*column+gap-close.Width,footer);
            close.Click+=delegate{Close();};Controls.Add(close);
            ClientSize=new Size(2*pad+2*column+gap,footer+close.Height+pad);
            Theme.Changed+=OnTheme;ApplyTheme();NoFlash.Attach(this);
        }
        SettingRow Row(int col,string title,string description,Control editor,int width,int height) {
            int pad=Theme.S(24),column=Theme.S(400),gap=Theme.S(20),rowHeight=Theme.S(62),rowGap=Theme.S(6),top=pad+Theme.S(34);
            int index=rows.Count(r=>(r.Tag as string)==col.ToString());
            var row=new SettingRow(title,description,editor,width,height){Tag=col.ToString(),Bounds=new Rectangle(pad+col*(column+gap),top+index*(rowHeight+rowGap),column,rowHeight)};
            rows.Add(row);Controls.Add(row);return row;
        }
        static int Nearest(long value){int best=0;for(int i=1;i<Limits.Length;i++)if(Math.Abs(Limits[i]-value)<Math.Abs(Limits[best]-value))best=i;return best;}
        void Save(){settings.Save(AppPaths.SettingsFile);manager.ApplySettings();if(changed!=null)changed();}
        void OnTheme(object sender,EventArgs e){ApplyTheme();}
        void ApplyTheme(){BackColor=Theme.C.Bg;foreach(var row in rows)row.BackColor=Theme.C.Card;Theme.Frame(this);Invalidate(true);}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Theme.Frame(this);}
        protected override void Dispose(bool disposing){if(disposing)Theme.Changed-=OnTheme;base.Dispose(disposing);}
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData){if(keyData==Keys.Escape){Close();return true;}return base.ProcessCmdKey(ref msg,keyData);}
        protected override void OnPaint(PaintEventArgs e) {
            var c=Theme.C;int pad=Theme.S(24),column=Theme.S(400),gap=Theme.S(20);
            Theme.Text(e.Graphics,"Downloads",Theme.Ui(10.5f,true),new Rectangle(pad,pad,column,Theme.S(26)),c.Text);
            Theme.Text(e.Graphics,"Aplicativo",Theme.Ui(10.5f,true),new Rectangle(pad+column+gap,pad,column,Theme.S(26)),c.Text);
        }
    }
}
