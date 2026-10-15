using System;
using System.Drawing;
using System.Windows.Forms;

namespace MyDownload {
    // Segoe Fluent Icons ships with Windows 11; Windows 10 has the same code points in
    // Segoe MDL2 Assets. Every glyph below was checked against the installed font.
    static class Icons {
        public const string Download="", Link="", Folder="", FolderOpen="", Pause="", Play="",
            Cancel="", Close="", Minimize="", Maximize="", Restore="", Settings="", More="",
            Search="", Check="", Completed="", Refresh="", Copy="", Delete="", Warning="",
            Error="", Info="", Open="", Document="", Video="", Music="", Picture="",
            Archive="", Program="", Disc="", Sun="", Moon="", ChevronDown="", Pin="",
            Mini="", Expand="", Speed="", Paste="", Fingerprint="", Sort="", Add="", Power="";
        public static string ForKind(FileKind kind) {
            switch(kind){
                case FileKind.Archive:return Archive;case FileKind.Disc:return Disc;case FileKind.Program:return Program;
                case FileKind.Video:return Video;case FileKind.Music:return Music;case FileKind.Picture:return Picture;
                default:return Document;
            }
        }
        public static void Draw(Graphics g,string glyph,Rectangle area,float pixels,Color color) {
            TextRenderer.DrawText(g,glyph,Theme.IconFont(pixels),area,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine);
        }
    }
}
