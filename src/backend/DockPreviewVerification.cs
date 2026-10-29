// C# 12 / .NET 8. --verify-dock-preview [OUT_DIR] : opens the dock preview bubble for three harmless windows owned by this process,
// captures it (screen BitBlt, short-lived), clicks tile 2 with a real mouse event and checks activation, diff refresh and cleanup.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
namespace PanneauxGlass;

internal static class DockPreviewVerification {
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,int dx,int dy,uint data,UIntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
    static void Pump(int milliseconds){var end=DateTime.UtcNow.AddMilliseconds(milliseconds);while(DateTime.UtcNow<end){System.Windows.Forms.Application.DoEvents();Thread.Sleep(10);}}
    static bool Capture(Rectangle r,string path,out int distinct){
        using var bmp=new Bitmap(r.Width,r.Height);using(var g=Graphics.FromImage(bmp))g.CopyFromScreen(r.Location,Point.Empty,r.Size);bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png);
        var seen=new HashSet<int>();for(int y=0;y<bmp.Height;y+=4)for(int x=0;x<bmp.Width;x+=4)seen.Add(bmp.GetPixel(x,y).ToArgb());distinct=seen.Count;return distinct>8;
    }
    internal static int Run(string[] args){
        bool passed=true;
        void Check(string name,bool ok,string detail=""){Console.WriteLine((ok?"PASS ":"FAIL ")+name+(detail.Length>0?" -- "+detail:""));passed&=ok;}
        int at=Array.IndexOf(args,"--verify-dock-preview");
        string outDir=at>=0&&at+1<args.Length&&!args[at+1].StartsWith("--")?args[at+1]:Path.Combine(Path.GetTempPath(),"dock-preview");
        Directory.CreateDirectory(outDir);
        System.Windows.Forms.Application.EnableVisualStyles();
        var multi=TaskbarNative.Applications().Where(w=>!string.IsNullOrEmpty(w.Path)).GroupBy(w=>w.Path,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>=2).Select(g=>Path.GetFileName(g.Key)+"="+g.Count()).ToArray();
        Console.WriteLine("INFO apps with >=2 real windows on this machine: "+(multi.Length==0?"none":string.Join(", ",multi))+" (test uses 3 own harmless windows instead)");
        var forms=new List<Form>();
        Form Make(string title,Color color,int x){
            var f=new Form{Text=title,BackColor=color,StartPosition=FormStartPosition.Manual,Location=new Point(x,80),Size=new Size(360,240),ShowInTaskbar=false};
            f.Controls.Add(new Label{Text=title,AutoSize=true,Font=new Font("Segoe UI",20),Location=new Point(20,20)});forms.Add(f);f.Show();return f;
        }
        var wa=Screen.PrimaryScreen!.WorkingArea;
        var a=Make("Preview test A",Color.SteelBlue,wa.Left+40);var b=Make("Preview test B",Color.IndianRed,wa.Left+420);var c=Make("Preview test C (minimise)",Color.SeaGreen,wa.Left+800);
        Pump(400);c.WindowState=FormWindowState.Minimized;Pump(400);
        var windows=forms.Select(f=>new PreviewWindow(f.Handle.ToInt64(),f.Text)).ToArray();
        using var host=new DockPreviewHost();
        int ax=(wa.Left+wa.Right)/2,ay=wa.Bottom;
        try{
            a.Activate();Pump(200);
            host.Open("test",windows,ax,ay,false);Pump(700);
            var popup=host.Form;
            Check("popup opened",host.IsOpen&&popup!=null&&popup.Visible);
            if(popup==null)return 1;
            Check("tile count equals window count (3)",popup.TileCount==3,"tiles="+popup.TileCount);
            Check("live thumbnails for the 2 normal windows, minimised one falls back to title+icon",popup.RegisteredThumbnails==2,"thumbnails="+popup.RegisteredThumbnails);
            long ex=Native.ExtendedStyle(popup.Handle);
            Check("ex-style TOOLWINDOW|NOACTIVATE|TOPMOST",(ex&0x08000088)==0x08000088,"0x"+ex.ToString("X"));
            Check("popup did not take the foreground",Native.GetForegroundWindow()!=popup.Handle);
            Check("popup inside the work area, above the anchor",popup.Bounds.Bottom<=wa.Bottom&&popup.Left>=wa.Left&&popup.Right<=wa.Right,popup.Bounds.ToString());
            string live=Path.Combine(outDir,"dock-preview-live.png");
            Check("PNG capture of the popup is not blank",Capture(popup.Bounds,live,out int distinct),"colours="+distinct+" -> "+live);
            host.Update(windows.Where(w=>w.Hwnd!=b.Handle.ToInt64()).ToArray());Pump(150);
            Check("window list shrinks to 2 tiles / 1 thumbnail, bubble stays open",host.IsOpen&&popup.TileCount==2&&popup.RegisteredThumbnails==1,"tiles="+popup.TileCount+" thumbs="+popup.RegisteredThumbnails);
            host.Update(windows);Pump(300);
            Check("window list back to 3 tiles / 2 thumbnails",popup.TileCount==3&&popup.RegisteredThumbnails==2,"tiles="+popup.TileCount+" thumbs="+popup.RegisteredThumbnails);
            a.Activate();Pump(200);
            Check("A is foreground before the click",Native.GetForegroundWindow()==a.Handle);
            Native.GetCursorPos(out var saved);
            var tile=popup.TileBounds(1);var target=popup.PointToScreen(new Point(tile.X+tile.Width/2,tile.Y+tile.Height/2));
            SetCursorPos(target.X,target.Y);Pump(150);
            bool overPopup=WindowFromPoint(target)==popup.Handle;
            Check("cursor really over the popup (else the click is skipped)",overPopup);
            if(overPopup){mouse_event(0x2,0,0,0,UIntPtr.Zero);Pump(40);mouse_event(0x4,0,0,0,UIntPtr.Zero);for(int i=0;i<40&&Native.GetForegroundWindow()!=b.Handle;i++)Pump(25);}
            SetCursorPos(saved.X,saved.Y);Pump(100);
            Check("click on tile 2 -> GetForegroundWindow() == window 2",Native.GetForegroundWindow()==b.Handle,"fg=0x"+Native.GetForegroundWindow().ToString("X")+" b=0x"+b.Handle.ToString("X"));
            Check("popup closed after the click, every thumbnail unregistered",!host.IsOpen&&popup.IsDisposed&&popup.RegisteredThumbnails==0,"open="+host.IsOpen+" disposed="+popup.IsDisposed+" thumbs="+popup.RegisteredThumbnails);
            host.Open("test",windows,ax,ay,true);Pump(500);
            var flat=host.Form;
            Check("reduced effects: 3 tiles, 0 thumbnails",flat!=null&&flat.TileCount==3&&flat.RegisteredThumbnails==0,flat==null?"":"tiles="+flat.TileCount+" thumbs="+flat.RegisteredThumbnails);
            string reduced=Path.Combine(outDir,"dock-preview-reduced.png");
            if(flat!=null)Check("PNG capture (reduced) is not blank",Capture(flat.Bounds,reduced,out int d2),"colours="+d2+" -> "+reduced);
            host.Retire(100);Pump(400);
            Check("Retire() closes the bubble when the cursor is not over it",!host.IsOpen&&flat!=null&&flat.IsDisposed);
            host.Open("test",windows,ax,ay,false);Pump(300);var last=host.Form;host.Close();
            Check("Close() hides and destroys the window",!host.IsOpen&&last!=null&&last.IsDisposed&&last.RegisteredThumbnails==0);
            // Cap: 2 tiles per row x 3 rows = 6 shown, 8 windows (3 above + 5 tiny extra) -> "+2" indicator.
            for(int i=0;i<5;i++){var extra=new Form{Text="Preview extra "+i,StartPosition=FormStartPosition.Manual,Location=new Point(wa.Left+20+i*30,wa.Top+20),Size=new Size(120,80),ShowInTaskbar=false};forms.Add(extra);extra.Show();}
            Pump(200);
            var many=forms.Select(f=>new PreviewWindow(f.Handle.ToInt64(),f.Text)).ToArray();
            using(var capped=new DockPreviewForm(false,1.0,460)){
                _=capped.Handle;capped.SetWindows(many);
                Check("more windows than the cap: 6 tiles shown, '+2' indicator",many.Length==8&&capped.TileCount==6&&capped.OverflowCount==2,"tiles="+capped.TileCount+" overflow="+capped.OverflowCount);
                capped.SetWindows(many.Take(6).ToArray());
                Check("no indicator when everything fits",capped.TileCount==6&&capped.OverflowCount==0);
            }
            // Safety timer: cursor outside bubble and dock, bubble opened without any pointer event -> closed by itself after ~1.5-3 s.
            Native.GetCursorPos(out var before);
            host.Open("safety",windows,ax,ay,true);SetCursorPos(wa.Left+2,wa.Top+2);Pump(300);
            bool openedFirst=host.IsOpen;Pump(3600);
            SetCursorPos(before.X,before.Y);
            Check("safety timer closes a stuck bubble when the cursor stays outside",openedFirst&&!host.IsOpen,"openedFirst="+openedFirst+" stillOpen="+host.IsOpen);
        }finally{host.Close();foreach(var f in forms)f.Dispose();}
        Console.WriteLine(passed?"ALL PASS":"SOME FAILED");
        return passed?0:1;
    }
}
