using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace MyDownload {
    enum RowAction { None, Check, Primary, Folder, More }

    // What each state looks like and which action it offers, shared by the list, the menus and the mini window.
    static class Describe {
        static readonly CultureInfo pt=CultureInfo.GetCultureInfo("pt-BR");
        public static bool Expired(DownloadItem i){return i.Phase=="Error"&&i.Detail!=null&&i.Detail.Contains("Atualizar link");}
        public static bool Attention(DownloadItem i){string p=i.Phase;return p=="Error"||p=="Missing"||p=="Changed"||i.Job.HashCheck=="mismatch";}
        public static bool InProgress(DownloadItem i){string p=i.Phase;return p=="Downloading"||p=="Connecting"||p=="Finalizing"||p=="Queued"||p=="Paused"||p=="New";}
        public static int Order(DownloadItem i) {
            switch(i.Phase){case "Downloading":case "Connecting":case "Finalizing":return 0;case "Queued":return 1;case "Paused":case "New":return 2;default:return 3;}
        }
        public static Color Tint(DownloadItem i) {
            var c=Theme.C;
            switch(i.Phase){
                case "Completed":return i.Job.HashCheck=="mismatch"?c.Err:c.Ok;
                case "Error":return c.Err;
                case "Missing":case "Changed":case "Paused":return c.Warn;
                case "Queued":case "New":return c.Muted;
                default:return c.Accent;
            }
        }
        public static string Glyph(DownloadItem i) {
            if(i.Phase=="Completed")return i.Job.HashCheck=="mismatch"?Icons.Warning:Icons.Completed;
            if(i.Phase=="Error")return Icons.Error;
            return Icons.ForKind(Categories.Of(i.Title));
        }
        // Glyph and tooltip of the main button of a row, or null when there is nothing to do.
        public static string[] Primary(DownloadItem i) {
            if(i.Replacing)return null;
            switch(i.Phase){
                case "Downloading":case "Connecting":case "Queued":return new[]{Icons.Pause,"Pausar"};
                case "Finalizing":return null;
                case "Completed":return new[]{Icons.Open,Categories.IsExecutable(i.Title)?"Mostrar na pasta (programas não são abertos direto daqui)":"Abrir arquivo"};
                case "Missing":case "Changed":return new[]{Icons.Refresh,"Baixar novamente"};
                default:return Expired(i)?new[]{Icons.Refresh,"Atualizar link"}:new[]{Icons.Play,"Retomar"};
            }
        }
        public static string Group(DownloadItem i) {
            if(i.Phase!="Completed")return "Agora";
            var day=i.When.Date;var today=DateTime.Today;
            if(day==today)return "Hoje";if(day==today.AddDays(-1))return "Ontem";
            if(day>today.AddDays(-7))return "Últimos 7 dias";
            return "Mais antigos";
        }
        public static string When(DateTime d) {
            if(d==DateTime.MinValue)return "concluído";
            var today=DateTime.Today;
            if(d.Date==today)return "concluído às "+d.ToString("HH:mm");
            if(d.Date==today.AddDays(-1))return "concluído ontem às "+d.ToString("HH:mm");
            if(d.Date>today.AddDays(-7))return "concluído "+pt.DateTimeFormat.GetDayName(d.DayOfWeek);
            return "concluído em "+d.ToString(d.Year==today.Year?"dd/MM":"dd/MM/yyyy");
        }
        // Appends the per-download options that change what happens: limit and what to do at the end.
        static object[] WithOptions(DownloadItem i,object[] parts) {
            var c=Theme.C;var list=parts.ToList();
            if(i.Job.SpeedLimit>0){list.Add(" · até "+Units.Size(i.Job.SpeedLimit)+"/s");list.Add(c.Muted);}
            switch(i.Job.AfterDone){
                case "open":list.Add(" · abre ao terminar");list.Add(c.Muted);break;
                case "folder":list.Add(" · abre a pasta ao terminar");list.Add(c.Muted);break;
                case "shutdown":list.Add(" · desliga o PC ao terminar");list.Add(c.Warn);break;
                case "sleep":list.Add(" · suspende o PC ao terminar");list.Add(c.Warn);break;
            }
            return list.ToArray();
        }
        static string Of(DownloadItem i){return i.Job.Total>0?Units.Size(i.Bytes)+" de "+Units.Size(i.Job.Total):Units.Size(i.Bytes)+" baixados";}
        // Status line plus the folder the download is saved in.
        public static object[] Meta(DownloadItem i) {
            var parts=Status(i).ToList();
            if(i.Phase!="Error"&&!i.Replacing){parts.Add(" · em "+Place(i.Job.Folder));parts.Add(Theme.C.Muted);}
            return parts.ToArray();
        }
        // Folders inside the user's profile are shown without it: "Downloads", "Desktop\Jogos".
        public static string Place(string folder) {
            string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\')+"\\";
            folder=(folder??"").TrimEnd('\\');
            return folder.StartsWith(profile,StringComparison.OrdinalIgnoreCase)?folder.Substring(profile.Length):folder;
        }
        static object[] Status(DownloadItem i) {
            var c=Theme.C;const string sep=" · ";
            if(i.Replacing)return new object[]{"Conferindo o link novo…",c.Accent};
            switch(i.Phase) {
                case "Downloading": {
                    var parts=new List<object>{Of(i),c.Muted};
                    if(i.Rate>0){parts.Add(sep+Units.Size(i.Rate)+"/s");parts.Add(c.Text);}
                    if(i.Job.Total>i.Bytes&&i.Rate>1024){parts.Add(sep+"falta "+Units.Duration((i.Job.Total-i.Bytes)/i.Rate));parts.Add(c.Muted);}
                    if(i.Connections>1){parts.Add(sep+i.Connections+" conexões");parts.Add(c.Muted);}
                    if(i.Detail!=null&&i.Detail.StartsWith("Conexão interrompida")){parts.Add(sep+i.Detail);parts.Add(c.Warn);}
                    return WithOptions(i,parts.ToArray());
                }
                case "Connecting":return new object[]{i.Detail!=null&&i.Detail.StartsWith("Conexão")?i.Detail:"Conectando…",c.Accent};
                case "Finalizing":return new object[]{"Finalizando…",c.Accent};
                case "Queued":return WithOptions(i,new object[]{"Na fila",c.Muted,i.Bytes>0?sep+Of(i):"",c.Muted});
                case "Paused":return WithOptions(i,new object[]{"Pausado",c.Warn,sep+Of(i),c.Muted});
                case "New":return new object[]{"Aguardando",c.Muted};
                case "Error":return new object[]{i.Detail??"Não foi possível concluir.",c.Err};
                case "Missing":return new object[]{"Arquivo apagado do destino",c.Warn,sep+"baixe novamente se precisar",c.Muted};
                case "Changed":return new object[]{"Arquivo alterado no destino",c.Warn,sep+"baixe de novo para salvar outra cópia",c.Muted};
                case "Completed": {
                    var parts=new List<object>{Units.Size(Math.Max(0,i.Job.Total))+sep+When(i.When),c.Muted};
                    if(i.Hashing){parts.Add(sep+"calculando SHA-256 "+(i.Job.Total>0?(100.0*System.Threading.Interlocked.Read(ref i.HashDone)/i.Job.Total).ToString("0")+"%":""));parts.Add(c.Accent);}
                    else if(i.Job.HashCheck=="match"){parts.Add(sep+"SHA-256 confere");parts.Add(c.Ok);}
                    else if(i.Job.HashCheck=="mismatch"){parts.Add(sep+"SHA-256 diferente do informado");parts.Add(c.Err);}
                    return parts.ToArray();
                }
                default:return new object[]{i.Detail??"",c.Muted};
            }
        }
    }

    sealed class ListRow { public string Header; public DownloadItem Item; public int Top, Height; }

    sealed class DownloadList : Painted {
        public event Action<DownloadItem,RowAction,Point> ActionClicked;
        public event Action<Point> MenuRequested;
        public event Action<DownloadItem> Activated;
        public event Action<Keys> Command;
        public Tips Tips;
        public string EmptyTitle="",EmptyText="";
        readonly List<ListRow> rows=new List<ListRow>();
        readonly HashSet<string> selected=new HashSet<string>();
        string anchor,focusId;
        int scroll,content,hoverRow=-1,dragOffset;
        RowAction hoverAction,downAction;int downRow=-1;
        bool thumbHover,dragging;
        public DownloadList(){TabStop=true;}
        int Side{get{return Theme.S(28);}}
        int RowHeight{get{return Theme.S(60);}}
        public List<DownloadItem> Selected {get{return rows.Where(r=>r.Item!=null&&selected.Contains(r.Item.Job.Id)).Select(r=>r.Item).ToList();}}
        public DownloadItem FocusItem {get{var row=rows.FirstOrDefault(r=>r.Item!=null&&r.Item.Job.Id==focusId);return row==null?null:row.Item;}}
        List<ListRow> Items {get{return rows.Where(r=>r.Item!=null).ToList();}}

        public event Action SelectionChanged;
        void Changed(){Invalidate();var handler=SelectionChanged;if(handler!=null)handler();}
        public void SetRows(List<ListRow> list) {
            rows.Clear();rows.AddRange(list);
            int y=Theme.S(2);
            foreach(var r in rows){r.Top=y;r.Height=r.Header!=null?Theme.S(30):RowHeight;y+=r.Height+(r.Header!=null?0:Theme.S(6));}
            content=y+Theme.S(10);
            var ids=new HashSet<string>(rows.Where(r=>r.Item!=null).Select(r=>r.Item.Job.Id));
            bool lost=selected.RemoveWhere(id=>!ids.Contains(id))>0;
            if(focusId!=null&&!ids.Contains(focusId))focusId=null;
            if(hoverRow>=rows.Count)hoverRow=-1;
            ClampScroll();Invalidate();
            if(lost)Changed();
        }
        public void Select(DownloadItem item) {
            selected.Clear();
            if(item!=null){selected.Add(item.Job.Id);anchor=focusId=item.Job.Id;EnsureVisible(item.Job.Id);}
            Changed();
        }
        public void SelectAll(){foreach(var r in Items)selected.Add(r.Item.Job.Id);Changed();}
        public void ClearSelection(){selected.Clear();Changed();}
        public void EnsureVisible(string id) {
            var row=rows.FirstOrDefault(r=>r.Item!=null&&r.Item.Job.Id==id);if(row==null)return;
            if(row.Top<scroll)scroll=row.Top-Theme.S(34);
            else if(row.Top+row.Height>scroll+Height)scroll=row.Top+row.Height-Height+Theme.S(8);
            ClampScroll();Invalidate();
        }
        void ClampScroll(){scroll=Math.Max(0,Math.Min(scroll,Math.Max(0,content-Height)));}
        protected override void OnResize(EventArgs e){ClampScroll();base.OnResize(e);}

        Rectangle Card(ListRow r){return new Rectangle(Side,r.Top-scroll,Width-2*Side,r.Height);}
        void Buttons(Rectangle card,out Rectangle primary,out Rectangle folder,out Rectangle more) {
            int size=Theme.S(32),y=card.Y+(card.Height-size)/2;
            more=new Rectangle(card.Right-Theme.S(10)-size,y,size,size);
            folder=new Rectangle(more.X-Theme.S(2)-size,y,size,size);
            primary=new Rectangle(folder.X-Theme.S(2)-size,y,size,size);
        }
        Rectangle CheckBox(Rectangle card){int size=Theme.S(30);return new Rectangle(card.X+Theme.S(10),card.Y+(card.Height-size)/2,size,size);}
        int HitRow(Point p) {
            for(int i=0;i<rows.Count;i++){var r=rows[i];if(r.Item==null)continue;if(Card(r).Contains(p))return i;}
            return -1;
        }
        RowAction HitAction(int index,Point p) {
            if(index<0)return RowAction.None;
            var card=Card(rows[index]);
            if(CheckBox(card).Contains(p))return RowAction.Check;
            Rectangle primary,folder,more;Buttons(card,out primary,out folder,out more);
            if(more.Contains(p))return RowAction.More;
            if(folder.Contains(p))return RowAction.Folder;
            if(primary.Contains(p)&&Describe.Primary(rows[index].Item)!=null)return RowAction.Primary;
            return RowAction.None;
        }
        Rectangle Thumb() {
            if(content<=Height)return Rectangle.Empty;
            int h=Math.Max(Theme.S(36),Height*Height/content);
            int y=(int)((long)scroll*(Height-h)/Math.Max(1,content-Height));
            int w=thumbHover||dragging?Theme.S(8):Theme.S(4);
            return new Rectangle(Width-Theme.S(8)-w/2,y,w,h);
        }
        bool OnScrollbar(Point p){return content>Height&&p.X>=Width-Theme.S(16);}

        protected override void OnMouseMove(MouseEventArgs e) {
            if(dragging){var t=Thumb();scroll=(int)((long)(e.Y-dragOffset)*(content-Height)/Math.Max(1,Height-t.Height));ClampScroll();Invalidate();return;}
            bool onBar=OnScrollbar(e.Location);
            int row=onBar?-1:HitRow(e.Location);var action=HitAction(row,e.Location);
            if(row!=hoverRow||action!=hoverAction||onBar!=thumbHover) {
                hoverRow=row;hoverAction=action;thumbHover=onBar;Invalidate();
                Cursor=action!=RowAction.None?Cursors.Hand:Cursors.Default;
                if(Tips!=null) {
                    string tip="";
                    if(action==RowAction.Primary){var p=Describe.Primary(rows[row].Item);tip=p==null?"":p[1];}
                    else if(action==RowAction.Folder)tip="Abrir a pasta";else if(action==RowAction.More)tip="Mais opções";
                    else if(action==RowAction.Check)tip="Marcar para selecionar vários";
                    else if(row>=0)tip=rows[row].Item.Title+"\n"+rows[row].Item.Job.Folder+"\n"+rows[row].Item.Host;
                    Tips.SetToolTip(this,tip);
                }
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e){hoverRow=-1;hoverAction=RowAction.None;thumbHover=false;base.OnMouseLeave(e);}
        protected override void OnMouseDown(MouseEventArgs e) {
            Focus();
            if(e.Button==MouseButtons.Left&&OnScrollbar(e.Location)) {
                var t=Thumb();
                if(t.Contains(new Point(t.X+1,e.Y))){dragging=true;dragOffset=e.Y-t.Y;Capture=true;}
                else{scroll+=e.Y<t.Y?-Height:Height;ClampScroll();}
                Invalidate();return;
            }
            int row=HitRow(e.Location);downRow=row;downAction=HitAction(row,e.Location);
            if(row>=0) {
                string id=rows[row].Item.Job.Id;
                if(e.Button==MouseButtons.Right){if(!selected.Contains(id)){selected.Clear();selected.Add(id);anchor=id;}focusId=id;}
                else if(downAction==RowAction.Check){if(!selected.Remove(id))selected.Add(id);anchor=focusId=id;}
                else if(downAction==RowAction.None) {
                    if((ModifierKeys&Keys.Control)!=0){if(!selected.Remove(id))selected.Add(id);anchor=id;}
                    else if((ModifierKeys&Keys.Shift)!=0&&anchor!=null)SelectRange(anchor,id);
                    else{selected.Clear();selected.Add(id);anchor=id;}
                    focusId=id;
                }
            } else if(e.Button==MouseButtons.Left&&(ModifierKeys&(Keys.Control|Keys.Shift))==0)selected.Clear();
            base.OnMouseDown(e);Changed();
        }
        protected override void OnMouseUp(MouseEventArgs e) {
            if(dragging){dragging=false;Capture=false;Invalidate();return;}
            int row=HitRow(e.Location);
            if(e.Button==MouseButtons.Left&&row>=0&&row==downRow&&downAction!=RowAction.None&&downAction!=RowAction.Check&&HitAction(row,e.Location)==downAction) {
                var handler=ActionClicked;if(handler!=null)handler(rows[row].Item,downAction,PointToScreen(new Point(e.X,e.Y)));
            }
            if(e.Button==MouseButtons.Right&&row>=0){var handler=MenuRequested;if(handler!=null)handler(PointToScreen(e.Location));}
            downRow=-1;downAction=RowAction.None;
            base.OnMouseUp(e);
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e) {
            int row=HitRow(e.Location);
            if(e.Button==MouseButtons.Left&&row>=0&&HitAction(row,e.Location)==RowAction.None){var handler=Activated;if(handler!=null)handler(rows[row].Item);}
            base.OnMouseDoubleClick(e);
        }
        protected override void OnMouseWheel(MouseEventArgs e) {
            scroll-=e.Delta*Math.Max(1,SystemInformation.MouseWheelScrollLines)*Theme.S(22)/120;ClampScroll();Invalidate();
            base.OnMouseWheel(e);
        }
        void SelectRange(string from,string to) {
            var items=Items;int a=items.FindIndex(r=>r.Item.Job.Id==from),b=items.FindIndex(r=>r.Item.Job.Id==to);
            if(a<0||b<0)return;selected.Clear();
            for(int i=Math.Min(a,b);i<=Math.Max(a,b);i++)selected.Add(items[i].Item.Job.Id);
        }
        protected override bool IsInputKey(Keys key) {
            switch(key&Keys.KeyCode){case Keys.Up:case Keys.Down:case Keys.Home:case Keys.End:case Keys.PageUp:case Keys.PageDown:case Keys.Space:case Keys.Enter:return true;}
            return base.IsInputKey(key);
        }
        protected override void OnKeyDown(KeyEventArgs e) {
            var items=Items;int index=items.FindIndex(r=>r.Item.Job.Id==focusId);int page=Math.Max(1,Height/(RowHeight+Theme.S(6)));
            int target=-2;
            switch(e.KeyCode){
                case Keys.Up:target=index<=0?0:index-1;break;
                case Keys.Down:target=index<0?0:Math.Min(items.Count-1,index+1);break;
                case Keys.Home:target=0;break;
                case Keys.End:target=items.Count-1;break;
                case Keys.PageUp:target=Math.Max(0,index-page);break;
                case Keys.PageDown:target=Math.Min(items.Count-1,Math.Max(0,index)+page);break;
            }
            if(target>-2&&items.Count>0&&target>=0) {
                string id=items[target].Item.Job.Id;
                if(e.Shift&&anchor!=null)SelectRange(anchor,id);else{selected.Clear();selected.Add(id);anchor=id;}
                focusId=id;EnsureVisible(id);e.Handled=true;Changed();return;
            }
            if(e.Control&&e.KeyCode==Keys.A){SelectAll();e.Handled=true;return;}
            if(e.KeyCode==Keys.Apps||(e.Shift&&e.KeyCode==Keys.F10)) {
                var row=rows.FirstOrDefault(r=>r.Item!=null&&r.Item.Job.Id==focusId);
                if(row!=null){var card=Card(row);var handler=MenuRequested;if(handler!=null)handler(PointToScreen(new Point(card.X+Theme.S(60),card.Bottom-Theme.S(10))));}
                e.Handled=true;return;
            }
            if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Delete||e.KeyCode==Keys.Enter||(e.Control&&e.KeyCode==Keys.C)) {
                if(selected.Count==0&&focusId!=null)selected.Add(focusId);
                var handler=Command;if(handler!=null)handler(e.KeyData);e.Handled=true;return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics;var c=Theme.C;g.Clear(Back);
            if(rows.Count==0){PaintEmpty(g);return;}
            var nameFont=Theme.Ui(10,true);var metaFont=Theme.Ui(9);var percentFont=Theme.Ui(10,true);var headerFont=Theme.Ui(8.5f,true);
            for(int i=0;i<rows.Count;i++) {
                var r=rows[i];var card=Card(r);
                if(card.Bottom<0||card.Y>Height)continue;
                if(r.Header!=null){Theme.Text(g,r.Header,headerFont,new Rectangle(card.X+Theme.S(2),card.Y+Theme.S(8),card.Width,card.Height-Theme.S(8)),c.Muted);continue;}
                var item=r.Item;bool isSelected=selected.Contains(item.Job.Id),isHover=i==hoverRow;
                float radius=Theme.S(8);
                Theme.Fill(g,card,isHover&&!isSelected?Theme.Blend(c.Card,c.Text,c.Dark?.03f:.015f):c.Card,radius);
                double fraction=item.Fraction;
                if(item.Phase!="Completed"&&fraction>0) {
                    bool live=item.Active;
                    using(var path=Theme.Round(card,radius)) {
                        var state=g.Save();g.SetClip(path);
                        using(var b=new SolidBrush(live?c.AccentSoft:Theme.Blend(c.Card,c.AccentSoft,.38f)))g.FillRectangle(b,card.X,card.Y,(float)(card.Width*Math.Min(1,fraction)),card.Height);
                        g.Restore(state);
                    }
                }
                if(isSelected)Theme.Stroke(g,card,c.Accent,radius,Math.Max(1.5f,Theme.F*1.5f));
                else Theme.Stroke(g,card,isHover?c.Strong:c.Line,radius);
                if(isSelected&&Focused&&item.Job.Id==focusId&&ShowFocusCues)Theme.Stroke(g,Rectangle.Inflate(card,-Theme.S(3),-Theme.S(3)),Theme.Blend(c.Accent,c.Card,.5f),radius-2);
                Color color=Describe.Tint(item);
                // Check boxes appear on hover and while several downloads are selected.
                if(isHover||selected.Count>=2) {
                    var box=CheckBox(card);var mark=new Rectangle(box.X+(box.Width-Theme.S(18))/2,box.Y+(box.Height-Theme.S(18))/2,Theme.S(18),Theme.S(18));
                    if(i==hoverRow&&hoverAction==RowAction.Check)Theme.Fill(g,box,c.Hover,Theme.S(6));
                    if(isSelected){Theme.Fill(g,mark,c.Accent,Theme.S(4));Icons.Draw(g,Icons.Check,mark,11,c.OnAccent);}
                    else Theme.Stroke(g,mark,c.Muted,Theme.S(4),Math.Max(1f,Theme.F));
                }
                else Icons.Draw(g,Describe.Glyph(item),new Rectangle(card.X+Theme.S(14),card.Y,Theme.S(22),card.Height),19,color);
                Rectangle primary,folder,more;Buttons(card,out primary,out folder,out more);
                var action=Describe.Primary(item);
                int textRight=(action!=null?primary.X:folder.X)-Theme.S(8);
                if(item.Phase!="Completed"&&item.Job.Total>0) {
                    string percent=Math.Floor(Math.Min(1,fraction)*100)+"%";
                    var box=new Rectangle(textRight-Theme.S(48),card.Y,Theme.S(48),card.Height);
                    Theme.Text(g,percent,percentFont,box,color,TextFormatFlags.Right);textRight=box.X-Theme.S(8);
                }
                int x=card.X+Theme.S(48);
                Theme.Text(g,item.Title,nameFont,new Rectangle(x,card.Y+Theme.S(9),Math.Max(10,textRight-x),Theme.S(22)),c.Text);
                Theme.Pieces(g,new Rectangle(x,card.Y+Theme.S(31),Math.Max(10,textRight-x),Theme.S(20)),metaFont,Describe.Meta(item));
                if(action!=null)PaintButton(g,primary,action[0],i==hoverRow&&hoverAction==RowAction.Primary);
                PaintButton(g,folder,Icons.FolderOpen,i==hoverRow&&hoverAction==RowAction.Folder);
                PaintButton(g,more,Icons.More,i==hoverRow&&hoverAction==RowAction.More);
            }
            var thumb=Thumb();
            if(!thumb.IsEmpty)Theme.Fill(g,thumb,thumbHover||dragging?c.Muted:c.Strong,thumb.Width/2f);
        }
        void PaintButton(Graphics g,Rectangle r,string glyph,bool hot) {
            if(hot)Theme.Fill(g,r,Theme.C.Hover,Theme.S(6));
            Icons.Draw(g,glyph,r,15,hot?Theme.C.Text:Theme.C.Muted);
        }
        void PaintEmpty(Graphics g) {
            var c=Theme.C;int mid=Math.Max(Theme.S(120),Height/2-Theme.S(30));
            Icons.Draw(g,Icons.Download,new Rectangle(0,mid-Theme.S(56),Width,Theme.S(44)),34,c.Faint);
            Theme.Text(g,EmptyTitle,Theme.Ui(11.5f,true),new Rectangle(Side,mid,Width-2*Side,Theme.S(28)),c.Text,TextFormatFlags.HorizontalCenter);
            Theme.Text(g,EmptyText,Theme.Ui(9.5f),new Rectangle(Side,mid+Theme.S(28),Width-2*Side,Theme.S(24)),c.Muted,TextFormatFlags.HorizontalCenter);
        }
    }
}
