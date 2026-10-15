using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace MyDownload {
    enum SetupResult { Cancel, Installed, OpenInstalled }

    // Shown when the exe runs from outside the install folder: installs it (choosing where), or,
    // when another build is already installed, updates it.
    sealed class SetupWindow : Form {
        readonly bool update;
        readonly CardPanel folderField;readonly FlatButton change,ok,cancel;readonly Toggle desktop,startup;
        string folder,status;bool statusBad;
        readonly int yFolder,yOptions,yNote;
        public SetupResult Result=SetupResult.Cancel;
        SetupWindow(bool update) {
            this.update=update;
            Text=(update?"Atualizar ":"Instalar ")+AppPaths.Name;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=true;
            StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.None;KeyPreview=true;Font=Theme.Ui(9.5f);DoubleBuffered=true;Icon=Program.AppIcon;
            int width=Theme.S(560),pad=Theme.S(28),inner=width-2*pad,y=pad+Theme.S(84);
            folder=Installer.Folder;
            yFolder=y;y+=Theme.S(22);
            folderField=new CardPanel{Bounds=new Rectangle(pad,y,inner,Theme.S(40))};Controls.Add(folderField);
            folderField.Paint+=(sender,e)=>{
                Icons.Draw(e.Graphics,Icons.Folder,new Rectangle(Theme.S(10),0,Theme.S(20),folderField.Height),14,Theme.C.Muted);
                int right=change!=null?change.Left-Theme.S(8):folderField.Width-Theme.S(12);
                Theme.Text(e.Graphics,folder,Font,new Rectangle(Theme.S(36),0,right-Theme.S(36),folderField.Height),update?Theme.C.Muted:Theme.C.Text,TextFormatFlags.PathEllipsis);
            };
            if(!update) {
                change=new FlatButton{Text="Alterar",Kind=ButtonKind.Standard};
                change.Size=new Size(change.Preferred(14),Theme.S(30));change.Location=new Point(folderField.Width-change.Width-Theme.S(5),(folderField.Height-change.Height)/2);
                change.Click+=delegate{Choose();};folderField.Controls.Add(change);
            }
            y+=folderField.Height+Theme.S(18);
            yOptions=y;
            desktop=new Toggle{Location=new Point(pad,y),Checked=!update||Installer.DesktopShortcut};Controls.Add(desktop);y+=Theme.S(34);
            startup=new Toggle{Location=new Point(pad,y),Checked=Startup.Enabled};Controls.Add(startup);y+=Theme.S(40);
            yNote=y;y+=Theme.S(48);
            ok=new FlatButton{Text=update?"Atualizar":"Instalar",Kind=ButtonKind.Primary,Font=Theme.Ui(9.5f,true)};
            ok.Size=new Size(Math.Max(Theme.S(120),ok.Preferred(18)),Theme.S(36));ok.Location=new Point(pad+inner-ok.Width,y);ok.Click+=delegate{Install();};
            cancel=new FlatButton{Text=update?"Abrir a instalada":"Cancelar",Kind=ButtonKind.Standard};
            cancel.Size=new Size(Math.Max(Theme.S(120),cancel.Preferred(18)),Theme.S(36));cancel.Location=new Point(ok.Left-Theme.S(8)-cancel.Width,y);
            cancel.Click+=delegate{Result=update?SetupResult.OpenInstalled:SetupResult.Cancel;Close();};
            Controls.Add(ok);Controls.Add(cancel);
            ClientSize=new Size(width,y+ok.Height+pad);
            Theme.Changed+=OnTheme;ApplyTheme();NoFlash.Attach(this);
            Shown+=delegate{Foreground.Bring(Handle);ok.Focus();};
        }
        void Choose() {
            string picked=FolderPicker.Pick(this,Directory.Exists(folder)?folder:Path.GetDirectoryName(folder),"Onde instalar o "+AppPaths.Name);
            if(picked==null)return;
            // A parent folder gets its own MyDownload folder, like other installers do.
            folder=String.Equals(Path.GetFileName(picked.TrimEnd('\\')),AppPaths.Name,StringComparison.OrdinalIgnoreCase)?picked:Path.Combine(picked,AppPaths.Name);
            status=null;folderField.Invalidate();Invalidate();
        }
        void Install() {
            status=update?"Atualizando…":"Instalando…";statusBad=false;Invalidate();Update();
            try {
                Installer.Install(folder,desktop.Checked,startup.Checked);
                Result=SetupResult.Installed;Close();
            } catch(UnauthorizedAccessException) {
                status="Essa pasta pede permissão de administrador e o Windows não deu. Permita no aviso do Windows ou escolha outra pasta.";statusBad=true;Invalidate();
            } catch(Exception e) {
                status="Não foi possível instalar: "+e.Message;statusBad=true;Invalidate();
            }
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData) {
            if(keyData==Keys.Escape){Close();return true;}
            return base.ProcessCmdKey(ref msg,keyData);
        }
        void OnTheme(object sender,EventArgs e){ApplyTheme();}
        void ApplyTheme(){BackColor=Theme.C.Bg;folderField.BackColor=Theme.C.Card;Theme.Frame(this);Invalidate(true);}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Theme.Frame(this);}
        protected override void Dispose(bool disposing){if(disposing)Theme.Changed-=OnTheme;base.Dispose(disposing);}
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;int pad=Theme.S(28),inner=ClientSize.Width-2*pad;
            using(var image=Program.IconImage(Theme.S(56)))g.DrawImage(image,pad,pad);
            int x=pad+Theme.S(72);
            Theme.Text(g,(update?"Atualizar o ":"Instalar o ")+AppPaths.Name,Theme.Ui(14,true),new Rectangle(x,pad,inner-Theme.S(72),Theme.S(32)),c.Text);
            Theme.Text(g,update?"Esta versão é diferente da que está instalada.":"Downloads com várias conexões, que continuam de onde pararam.",Theme.Ui(9.5f),new Rectangle(x,pad+Theme.S(32),inner-Theme.S(72),Theme.S(24)),c.Muted);
            Theme.Text(g,update?"Instalado em":"Instalar em",Theme.Ui(9,true),new Rectangle(pad,yFolder,inner,Theme.S(20)),c.Muted);
            Theme.Text(g,"Criar atalho na área de trabalho",Font,new Rectangle(desktop.Right+Theme.S(12),desktop.Top-Theme.S(4),inner,desktop.Height+Theme.S(8)),c.Text);
            Theme.Text(g,"Iniciar com o Windows (abre na bandeja e retoma os downloads)",Font,new Rectangle(startup.Right+Theme.S(12),startup.Top-Theme.S(4),inner-Theme.S(56),startup.Height+Theme.S(8)),c.Text);
            string note=status??"Também fica no menu Iniciar e em Configurações do Windows > Aplicativos, onde dá para desinstalar. Não precisa de administrador.";
            TextRenderer.DrawText(g,note,Theme.Ui(9),new Rectangle(pad,yNote,inner,Theme.S(42)),statusBad?c.Err:c.Muted,TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
        }
        public static SetupResult Run(bool update) {
            using(var window=new SetupWindow(update)){window.ShowDialog();return window.Result;}
        }
        // Renders the window to a PNG (screenshots and checks).
        public static void Preview(string path,bool update) {
            using(var window=new SetupWindow(update)) {
                window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-20000,-20000);window.ShowInTaskbar=false;window.Show();Application.DoEvents();
                using(var image=new Bitmap(window.Width,window.Height)){window.DrawToBitmap(image,new Rectangle(0,0,window.Width,window.Height));image.Save(path);}
            }
        }
    }
}
