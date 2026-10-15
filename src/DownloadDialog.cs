using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MyDownload {
    sealed class DownloadChoice { public string Folder,Name,AfterDone;public int Connections;public long SpeedLimit;public bool Remember; }

    // "Novo download" (where to save, name and per-download options) and "Opções do download"
    // for downloads already in the list.
    sealed class DownloadDialog : Form {
        public static readonly string[] AfterKeys={null,"open","folder","shutdown","sleep"};
        static readonly string[] AfterNames={"Nada","Abrir","Abrir pasta","Desligar PC","Suspender"};
        public static readonly long[] Limits={0,512000,1000000,2000000,5000000,10000000,20000000,50000000};
        public static readonly string[] LimitNames={"Sem limite","512 KB/s","1 MB/s","2 MB/s","5 MB/s","10 MB/s","20 MB/s","50 MB/s"};
        readonly Settings settings;readonly bool editing;
        readonly List<Incoming> incoming;readonly List<DownloadItem> downloads;
        readonly CardPanel nameField,folderField;readonly PasteBox nameBox;
        readonly FlatButton folderButton,ok,cancel;readonly Segmented after;readonly Stepper connections,limit;readonly Toggle remember;
        readonly ContextMenuStrip folderMenu=Menus.Create();
        readonly string heading;string info,names,folder;bool infoBad,nameEdited,settingName;
        readonly int yName,yInfo,yFolder,yAfter,yTuning,yRemember;
        public DownloadChoice Choice;

        DownloadDialog(Settings settings,List<Incoming> incoming,List<DownloadItem> downloads) {
            this.settings=settings;this.incoming=incoming;this.downloads=downloads;editing=downloads!=null;
            int count=editing?downloads.Count:incoming.Count;
            Text=editing?"Opções do download":"Novo download";FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
            ShowInTaskbar=!editing;StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.None;KeyPreview=true;Font=Theme.Ui(9.5f);DoubleBuffered=true;Icon=Program.AppIcon;
            int width=Theme.S(540),pad=Theme.S(24),inner=width-2*pad,y=pad;
            heading=editing?(count==1?"Opções do download":"Opções de "+count+" downloads"):(count==1?"Novo download":count+" downloads novos");
            y+=Theme.S(40);yName=y;y+=Theme.S(22);
            bool singleNew=!editing&&count==1;
            if(singleNew) {
                nameField=new CardPanel{Bounds=new Rectangle(pad,y,inner,Theme.S(38)),Underline=true};
                nameBox=new PasteBox();nameField.Controls.Add(nameBox);nameField.FocusSource=nameBox;
                nameBox.Bounds=new Rectangle(Theme.S(12),(nameField.Height-nameBox.Height)/2,nameField.Width-Theme.S(24),nameBox.Height);
                nameBox.GotFocus+=delegate{nameField.Invalidate();};nameBox.LostFocus+=delegate{nameField.Invalidate();};
                nameBox.TextChanged+=delegate{if(!settingName)nameEdited=true;};
                Controls.Add(nameField);y+=nameField.Height+Theme.S(6);
                SetName(incoming[0].Name??NameFromUrl(incoming[0].Url));
            } else {
                var titles=editing?downloads.Select(d=>d.Title).ToList():incoming.Select(i=>i.Name??NameFromUrl(i.Url)).ToList();
                names=String.Join("\n",titles.Take(3))+(titles.Count>3?"\ne mais "+(titles.Count-3):"");
                y+=Theme.S(20)*Math.Min(4,titles.Count)+Theme.S(6);
            }
            yInfo=y;y+=Theme.S(30);
            yFolder=y;y+=Theme.S(22);
            // New downloads start in the last folder used (it may differ from the default one).
            folder=editing?downloads[0].Job.Folder:settings.RecentFolders.FirstOrDefault(Directory.Exists)??settings.Folder;
            folderField=new CardPanel{Bounds=new Rectangle(pad,y,inner,Theme.S(38))};Controls.Add(folderField);
            folderField.Paint+=(sender,e)=>{
                Icons.Draw(e.Graphics,Icons.Folder,new Rectangle(Theme.S(10),0,Theme.S(20),folderField.Height),14,Theme.C.Muted);
                int right=folderButton!=null?folderButton.Left-Theme.S(8):folderField.Width-Theme.S(12);
                Theme.Text(e.Graphics,folder,Font,new Rectangle(Theme.S(36),0,right-Theme.S(36),folderField.Height),editing?Theme.C.Muted:Theme.C.Text,TextFormatFlags.PathEllipsis);
            };
            if(!editing) {
                folderButton=new FlatButton{Text="Alterar",Kind=ButtonKind.Standard,Glyph=Icons.ChevronDown,GlyphSize=10};
                folderButton.Size=new Size(folderButton.Preferred(12),Theme.S(30));
                folderButton.Location=new Point(folderField.Width-folderButton.Width-Theme.S(4),(folderField.Height-folderButton.Height)/2);
                folderButton.Click+=delegate{ShowFolders();};folderField.Controls.Add(folderButton);
            }
            y+=folderField.Height+Theme.S(16);
            yAfter=y;y+=Theme.S(22);
            after=new Segmented{Items=AfterNames,Bounds=new Rectangle(pad,y,inner,Theme.S(34))};Controls.Add(after);y+=after.Height+Theme.S(16);
            yTuning=y;y+=Theme.S(22);
            int half=(inner-Theme.S(16))/2;
            connections=new Stepper{Values=new[]{"Padrão ("+settings.Connections+")"}.Concat(Settings.ConnectionChoices.Select(c=>c+(c==1?" conexão":" conexões"))).ToArray(),Bounds=new Rectangle(pad,y,half,Theme.S(34))};
            limit=new Stepper{Values=LimitNames,Bounds=new Rectangle(pad+half+Theme.S(16),y,half,Theme.S(34))};
            Controls.Add(connections);Controls.Add(limit);y+=Theme.S(34)+Theme.S(14);
            yRemember=y;
            if(!editing){remember=new Toggle{Location=new Point(pad,y+Theme.S(4))};Controls.Add(remember);y+=Theme.S(36);}
            y+=Theme.S(10);
            ok=new FlatButton{Text=editing?"Salvar":"Baixar",Kind=ButtonKind.Primary,Glyph=editing?null:Icons.Download,GlyphSize=13,Font=Theme.Ui(9.5f,true)};
            ok.Size=new Size(Math.Max(Theme.S(110),ok.Preferred(18)),Theme.S(36));ok.Location=new Point(pad+inner-ok.Width,y);ok.Click+=delegate{Accept();};
            cancel=new FlatButton{Text="Cancelar",Kind=ButtonKind.Standard};
            cancel.Size=new Size(Theme.S(110),Theme.S(36));cancel.Location=new Point(ok.Left-Theme.S(8)-cancel.Width,y);cancel.Click+=delegate{DialogResult=DialogResult.Cancel;};
            Controls.Add(ok);Controls.Add(cancel);
            ClientSize=new Size(width,y+ok.Height+pad);
            if(editing) {
                var job=downloads[0].Job;
                after.SelectedIndex=Math.Max(0,Array.IndexOf(AfterKeys,job.AfterDone));
                connections.Index=job.Connections>0?Array.IndexOf(Settings.ConnectionChoices,job.Connections)+1:0;
                limit.Index=Nearest(job.SpeedLimit);
                info=downloads.Count==1?Summary(job.Total,downloads[0].Host):Summary(downloads.Where(d=>d.Job.Total>0).Sum(d=>d.Job.Total),null);
            } else {
                info=Summary(incoming.Sum(i=>Math.Max(0,i.Size)),incoming.Count==1?Host(incoming[0].Url):null);
                if(singleNew&&(incoming[0].Size<=0||incoming[0].Name==null))Inspect(incoming[0]);
            }
            Theme.Changed+=OnTheme;ApplyTheme();NoFlash.Attach(this);
            Shown+=delegate{Foreground.Bring(Handle);Activate();if(nameBox!=null){nameBox.Focus();nameBox.SelectionStart=0;nameBox.SelectionLength=Path.GetFileNameWithoutExtension(nameBox.Text).Length;}else ok.Focus();};
        }
        static int Nearest(long value){int best=0;for(int i=1;i<Limits.Length;i++)if(Math.Abs(Limits[i]-value)<Math.Abs(Limits[best]-value))best=i;return best;}
        static string Host(string url){Uri u;return Uri.TryCreate(url,UriKind.Absolute,out u)?u.Host:"";}
        static string Summary(long size,string host){return (size>0?Units.Size(size):"Tamanho ainda não informado")+(String.IsNullOrEmpty(host)?"":" · "+host);}
        static string NameFromUrl(string url) {
            Uri u;if(!Uri.TryCreate(url,UriKind.Absolute,out u))return "download";
            return Engine.CleanName(u.AbsolutePath.Split('/').Last());
        }
        void SetName(string name){settingName=true;nameBox.Text=name;settingName=false;}
        // Asks the server for the real name and size while the window is open.
        void Inspect(Incoming item) {
            info="Consultando o arquivo…";
            Task.Run(()=>Engine.Inspect(item.Url,item.Referrer,15000)).ContinueWith(task=>{
                if(IsDisposed||!IsHandleCreated)return;
                try {
                    BeginInvoke((Action)(()=>{
                        var found=task.Status==TaskStatus.RanToCompletion?task.Result:null;
                        if(found==null){info="Não deu para consultar o servidor agora. O download tenta de novo ao começar.";infoBad=true;}
                        else {
                            if(!nameEdited&&found.Name!="download")SetName(found.Name);
                            info=Summary(found.Size>0?found.Size:item.Size,Host(item.Url))+(found.Resumable?" · continua de onde parou":" · não continua se cair");
                            if(found.Page){info="Esse link abre uma página, não um arquivo.";infoBad=true;}
                        }
                        Invalidate();
                    }));
                } catch(InvalidOperationException){}
            });
        }
        void ShowFolders() {
            Menus.Clear(folderMenu);folderMenu.ShowCheckMargin=true;
            var folders=new List<string>{folder,settings.Folder};
            folders.AddRange(settings.RecentFolders.Where(Directory.Exists));
            foreach(string f in folders.Distinct(StringComparer.OrdinalIgnoreCase).Take(7)) {
                string path=f;var item=Menus.Item(folderMenu,path,()=>{folder=path;folderField.Invalidate();});
                item.Checked=String.Equals(path,folder,StringComparison.OrdinalIgnoreCase);
            }
            Menus.Separator(folderMenu);
            Menus.Item(folderMenu,"Escolher outra pasta…",()=>{string picked=FolderPicker.Pick(this,folder,"Onde salvar o download");if(picked!=null){folder=picked;folderField.Invalidate();}});
            folderMenu.Show(folderButton,new Point(folderButton.Width-folderMenu.PreferredSize.Width,folderButton.Height+Theme.S(4)));
        }
        void Accept() {
            if(!editing&&(String.IsNullOrWhiteSpace(folder)||!Path.IsPathRooted(folder))){info="Escolha uma pasta válida.";infoBad=true;Invalidate();return;}
            if(nameBox!=null&&nameEdited&&nameBox.Text.Trim().Length==0){info="Digite um nome para o arquivo.";infoBad=true;Invalidate();nameBox.Focus();return;}
            Choice=new DownloadChoice{
                Folder=folder,Name=nameBox!=null&&nameEdited?nameBox.Text.Trim():null,AfterDone=AfterKeys[after.SelectedIndex],
                Connections=connections.Index==0?0:Settings.ConnectionChoices[connections.Index-1],SpeedLimit=Limits[limit.Index],Remember=remember!=null&&remember.Checked};
            DialogResult=DialogResult.OK;
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData) {
            if(keyData==Keys.Enter&&!(ActiveControl is FlatButton)){Accept();return true;}
            if(keyData==Keys.Escape){DialogResult=DialogResult.Cancel;return true;}
            return base.ProcessCmdKey(ref msg,keyData);
        }
        void OnTheme(object sender,EventArgs e){ApplyTheme();}
        void ApplyTheme() {
            var c=Theme.C;BackColor=c.Bg;folderField.BackColor=c.Card;
            if(nameField!=null){nameField.BackColor=c.Card;nameBox.BackColor=c.Card;nameBox.ForeColor=c.Text;}
            Theme.Frame(this);Invalidate(true);
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Theme.Frame(this);}
        protected override void Dispose(bool disposing){if(disposing){Theme.Changed-=OnTheme;folderMenu.Dispose();}base.Dispose(disposing);}
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;int pad=Theme.S(24),inner=ClientSize.Width-2*pad;var label=Theme.Ui(9,true);
            Theme.Text(g,heading,Theme.Ui(13,true),new Rectangle(pad,pad-Theme.S(2),inner,Theme.S(32)),c.Text);
            int count=editing?downloads.Count:incoming.Count;
            Theme.Text(g,!editing&&count==1?"Nome do arquivo":count==1?"Arquivo":"Arquivos",label,new Rectangle(pad,yName,inner,Theme.S(20)),c.Muted);
            if(names!=null)TextRenderer.DrawText(g,names,Font,new Rectangle(pad,yName+Theme.S(22),inner,yInfo-yName-Theme.S(22)),c.Text,TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
            Theme.Text(g,info??"",Theme.Ui(9),new Rectangle(pad,yInfo,inner,Theme.S(22)),infoBad?c.Err:c.Muted);
            Theme.Text(g,"Salvar em",label,new Rectangle(pad,yFolder,inner,Theme.S(20)),c.Muted);
            Theme.Text(g,"Ao terminar",label,new Rectangle(pad,yAfter,inner,Theme.S(20)),c.Muted);
            int half=(inner-Theme.S(16))/2;
            Theme.Text(g,"Conexões",label,new Rectangle(pad,yTuning,half,Theme.S(20)),c.Muted);
            Theme.Text(g,"Velocidade",label,new Rectangle(pad+half+Theme.S(16),yTuning,half,Theme.S(20)),c.Muted);
            if(remember!=null)Theme.Text(g,"Salvar sempre nesta pasta, sem perguntar",Font,new Rectangle(remember.Right+Theme.S(10),yRemember,inner,Theme.S(30)),c.Text);
        }
        public static DownloadChoice Ask(IWin32Window owner,Settings settings,List<Incoming> items) {
            using(var dialog=new DownloadDialog(settings,items,null))return dialog.ShowDialog(owner)==DialogResult.OK?dialog.Choice:null;
        }
        public static DownloadChoice Edit(IWin32Window owner,Settings settings,List<DownloadItem> items) {
            using(var dialog=new DownloadDialog(settings,null,items))return dialog.ShowDialog(owner)==DialogResult.OK?dialog.Choice:null;
        }
    }
}
