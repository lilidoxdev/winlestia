// C# 12 / .NET 8 / Windows 11. Native window-preview bubble for the dock (hover an app icon -> one tile per open window).
// Topmost, no-activate tool window drawn with GDI+ in physical pixels; live DWM thumbnails when allowed, title + icon otherwise.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace PanneauxGlass;

internal sealed record PreviewWindow(long Hwnd,string Title);

internal static class DwmThumb {
    [StructLayout(LayoutKind.Sequential)] struct Props { public uint Flags; public Native.Rect Dest; public Native.Rect Src; public byte Opacity; public int Visible; public int ClientOnly; }
    [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest,IntPtr src,out IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb,ref Props props);
    [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb,out Size size);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,uint attribute,ref int value,int size);
    internal static void RoundCorners(IntPtr window){int round=2;try{DwmSetWindowAttribute(window,33,ref round,4);}catch{}}   // DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND
    internal static bool Register(IntPtr dest,IntPtr source,out IntPtr thumb,out Size size){thumb=IntPtr.Zero;size=default;try{if(DwmRegisterThumbnail(dest,source,out thumb)!=0||thumb==IntPtr.Zero){thumb=IntPtr.Zero;return false;}if(DwmQueryThumbnailSourceSize(thumb,out size)!=0||size.Width<=1||size.Height<=1){Unregister(ref thumb);return false;}return true;}catch{thumb=IntPtr.Zero;return false;}}
    internal static void Place(IntPtr thumb,Rectangle destination){var props=new Props{Flags=1|4|8|0x10,Dest=new Native.Rect{Left=destination.Left,Top=destination.Top,Right=destination.Right,Bottom=destination.Bottom},Opacity=255,Visible=1,ClientOnly=0};try{DwmUpdateThumbnailProperties(thumb,ref props);}catch{}}
    internal static void Unregister(ref IntPtr thumb){if(thumb==IntPtr.Zero)return;try{DwmUnregisterThumbnail(thumb);}catch{}thumb=IntPtr.Zero;}
}

internal sealed class DockPreviewForm : Form {
    sealed class Tile { public long Hwnd; public string Title=""; public Bitmap? Icon; public bool Pending; public IntPtr Thumb; public Size Source; public Rectangle Bounds,Area; }
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeoutW(IntPtr window,uint message,IntPtr wp,IntPtr lp,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll",EntryPoint="GetClassLongPtrW")] static extern IntPtr GetClassLongPtr(IntPtr window,int index);
    static readonly Dictionary<long,Bitmap> IconCache=new();   // UI thread only; bitmaps are owned here (borrowed HICONs are copied, never destroyed), cleared when the bubble is destroyed
    static bool faultLogged;
    static void Fault(string where,Exception ex){if(faultLogged)return;faultLogged=true;try{Store.Log("Aperçu du dock ("+where+"): "+ex.Message);}catch{}}
    readonly List<Tile> tiles=new();readonly bool live;readonly int maxWidth;readonly Font font,small;int hover=-1;Rectangle overflowArea;
    internal int OverflowCount{get;private set;}
    internal double Dpi{get;}
    internal event Action<long>? Picked;
    internal event Action? PointerEntered,PointerLeft;
    internal int TileCount=>tiles.Count(t=>!t.Bounds.IsEmpty);
    internal int RegisteredThumbnails=>tiles.Count(t=>t.Thumb!=IntPtr.Zero);
    internal Rectangle TileBounds(int index)=>index>=0&&index<tiles.Count?tiles[index].Bounds:Rectangle.Empty;
    internal long TileHwnd(int index)=>tiles[index].Hwnd;
    internal int PixelsOf(double v)=>(int)Math.Round(v*Dpi);
    internal DockPreviewForm(bool live,double scale,int maxWidth){
        this.live=live;Dpi=scale;this.maxWidth=maxWidth;
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);
        font=new Font("Segoe UI",(float)(12*scale),FontStyle.Regular,GraphicsUnit.Pixel);small=new Font("Segoe UI",(float)(11*scale),FontStyle.Regular,GraphicsUnit.Pixel);
    }
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80|0x08000000|0x8;return p;}}   // TOOLWINDOW | NOACTIVATE | TOPMOST
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);DwmThumb.RoundCorners(Handle);}
    protected override void WndProc(ref Message m){
        try{
            if(m.Msg==0x02E0)return;                                   // WM_DPICHANGED: this window is sized in physical pixels by the host
            if(m.Msg==0x0021){m.Result=new IntPtr(3);return;}          // WM_MOUSEACTIVATE -> MA_NOACTIVATE
        }catch(Exception ex){Fault("WndProc",ex);}
        base.WndProc(ref m);
    }
    static Color C(System.Windows.Media.Color c)=>Color.FromArgb(c.A,c.R,c.G,c.B);
    static Bitmap? CopyIcon(IntPtr icon){try{if(icon==IntPtr.Zero)return null;using var source=Icon.FromHandle(icon);return source.ToBitmap();}catch{return null;}}   // Icon.FromHandle does not own the HICON
    void Remember(long hwnd,Bitmap bitmap){
        if(IconCache.Count>=64)foreach(var key in IconCache.Keys.ToArray())if(!tiles.Any(t=>t.Hwnd==key)){IconCache[key].Dispose();IconCache.Remove(key);}
        if(IconCache.TryGetValue(hwnd,out var old)&&!ReferenceEquals(old,bitmap)&&!tiles.Any(t=>ReferenceEquals(t.Icon,old)))old.Dispose();
        IconCache[hwnd]=bitmap;
    }
    // Non-blocking class icon first; the blocking WM_GETICON fallback runs on a worker thread so a hung window never freezes the UI.
    void LoadIcon(Tile tile){
        if(IconCache.TryGetValue(tile.Hwnd,out var cached)){tile.Icon=cached;return;}
        var window=new IntPtr(tile.Hwnd);var quick=CopyIcon(GetClassLongPtr(window,-34));quick??=CopyIcon(GetClassLongPtr(window,-14));
        if(quick!=null){Remember(tile.Hwnd,quick);tile.Icon=quick;return;}
        if(tile.Pending||!IsHandleCreated)return;
        tile.Pending=true;
        System.Threading.Tasks.Task.Run(()=>{
            IntPtr icon=IntPtr.Zero;
            try{SendMessageTimeoutW(window,0x007F,new IntPtr(2),IntPtr.Zero,0x2,40,out icon);if(icon==IntPtr.Zero)SendMessageTimeoutW(window,0x007F,new IntPtr(1),IntPtr.Zero,0x2,40,out icon);}catch{icon=IntPtr.Zero;}
            try{BeginInvoke((Action)(()=>{
                tile.Pending=false;if(IsDisposed)return;
                var bitmap=CopyIcon(icon)??SystemIcons.Application.ToBitmap();Remember(tile.Hwnd,bitmap);tile.Icon=bitmap;Invalidate();
            }));}catch{}
        });
    }
    // Diff by hwnd: tiles (and their DWM thumbnails) of surviving windows are kept, vanished ones are released.
    internal void SetWindows(PreviewWindow[] windows){
        var wanted=windows.Select(w=>w.Hwnd).ToHashSet();
        foreach(var gone in tiles.Where(t=>!wanted.Contains(t.Hwnd)).ToArray()){DwmThumb.Unregister(ref gone.Thumb);tiles.Remove(gone);}
        var ordered=new List<Tile>();
        foreach(var w in windows){var tile=tiles.FirstOrDefault(t=>t.Hwnd==w.Hwnd);if(tile==null){tile=new Tile{Hwnd=w.Hwnd};ordered.Add(tile);LoadIcon(tile);}else ordered.Add(tile);tile.Title=w.Title;}
        tiles.Clear();tiles.AddRange(ordered);hover=-1;Arrange();
    }
    void Arrange(){
        int pad=PixelsOf(10),gap=PixelsOf(8),tw=PixelsOf(216),header=PixelsOf(26),thumbH=PixelsOf(120),th=header+thumbH+PixelsOf(8),inset=PixelsOf(8);
        int perRow=Math.Max(1,(maxWidth-2*pad+gap)/(tw+gap)),shown=Math.Min(tiles.Count,perRow*3),cols=Math.Max(1,Math.Min(shown,perRow)),rows=Math.Max(1,(shown+cols-1)/cols);
        for(int i=0;i<tiles.Count;i++){
            var tile=tiles[i];
            if(i>=shown){DwmThumb.Unregister(ref tile.Thumb);tile.Bounds=tile.Area=Rectangle.Empty;continue;}
            tile.Bounds=new Rectangle(pad+(i%cols)*(tw+gap),pad+(i/cols)*(th+gap),tw,th);
            tile.Area=new Rectangle(tile.Bounds.X+inset,tile.Bounds.Y+header,tw-2*inset,thumbH);
        }
        OverflowCount=tiles.Count-shown;int gridH=rows*th+(rows-1)*gap,labelH=OverflowCount>0?header:0;
        overflowArea=OverflowCount>0?new Rectangle(pad,pad+gridH+gap/2,cols*tw+(cols-1)*gap,labelH):Rectangle.Empty;
        ClientSize=new Size(2*pad+cols*tw+(cols-1)*gap,2*pad+gridH+(OverflowCount>0?gap/2+labelH:0));
        if(IsHandleCreated)UpdateThumbnails();
        Invalidate();
    }
    internal void UpdateThumbnails(){
        foreach(var tile in tiles){
            if(tile.Bounds.IsEmpty)continue;
            var window=new IntPtr(tile.Hwnd);
            if(!live||TaskbarNative.IsIconic(window)||!Native.IsWindow(window)){DwmThumb.Unregister(ref tile.Thumb);continue;}        // minimized windows give empty thumbnails -> title + icon tile
            if(tile.Thumb==IntPtr.Zero&&!DwmThumb.Register(Handle,window,out tile.Thumb,out tile.Source))continue;
            double fit=Math.Min(Math.Min((double)tile.Area.Width/tile.Source.Width,(double)tile.Area.Height/tile.Source.Height),1);
            int w=Math.Max(1,(int)(tile.Source.Width*fit)),h=Math.Max(1,(int)(tile.Source.Height*fit));
            DwmThumb.Place(tile.Thumb,new Rectangle(tile.Area.X+(tile.Area.Width-w)/2,tile.Area.Y+(tile.Area.Height-h)/2,w,h));
        }
    }
    int Hit(Point p){for(int i=0;i<tiles.Count;i++)if(!tiles[i].Bounds.IsEmpty&&tiles[i].Bounds.Contains(p))return i;return -1;}
    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);PointerEntered?.Invoke();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);if(hover!=-1){hover=-1;Invalidate();}PointerLeft?.Invoke();}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int next=Hit(e.Location);if(next!=hover){hover=next;Cursor=next>=0?Cursors.Hand:Cursors.Default;Invalidate();}}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button!=MouseButtons.Left)return;int index=Hit(e.Location);if(index>=0)Picked?.Invoke(tiles[index].Hwnd);}
    static GraphicsPath Round(Rectangle r,int radius){var path=new GraphicsPath();int d=Math.Max(2,radius*2);path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d-1,r.Y,d,d,270,90);path.AddArc(r.Right-d-1,r.Bottom-d-1,d,d,0,90);path.AddArc(r.X,r.Bottom-d-1,d,d,90,90);path.CloseFigure();return path;}
    Bitmap? placeholder;
    protected override void OnPaint(PaintEventArgs e){
        try{DrawBubble(e);}catch(Exception ex){Fault("OnPaint",ex);}
    }
    void DrawBubble(PaintEventArgs e){
        M3Palette? pal=null;try{pal=Theme.Current?.P;}catch{}
        Color K(Func<M3Palette,System.Windows.Media.Color> pick,Color fallback)=>pal==null?fallback:C(pick(pal));
        var p=new{
            SurfaceContainer=K(x=>x.SurfaceContainer,Color.FromArgb(32,32,36)),OutlineVariant=K(x=>x.OutlineVariant,Color.FromArgb(80,80,88)),
            SecondaryContainer=K(x=>x.SecondaryContainer,Color.FromArgb(64,70,92)),SurfaceHigh=K(x=>x.SurfaceHigh,Color.FromArgb(46,46,52)),
            OnSecondaryContainer=K(x=>x.OnSecondaryContainer,Color.White),OnSurface=K(x=>x.OnSurface,Color.White),
            Surface=K(x=>x.Surface,Color.FromArgb(24,24,28)),OnSurfaceVariant=K(x=>x.OnSurfaceVariant,Color.Gainsboro)};
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;
        g.Clear(p.SurfaceContainer);
        using(var border=new Pen(p.OutlineVariant))g.DrawRectangle(border,0,0,Width-1,Height-1);
        if(OverflowCount>0)TextRenderer.DrawText(g,OverflowCount==1?"+1 autre fenêtre":"+"+OverflowCount+" autres fenêtres",font,overflowArea,p.OnSurfaceVariant,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter);
        int radius=PixelsOf(12),iconSize=PixelsOf(16),inset=PixelsOf(8),header=PixelsOf(26);
        for(int i=0;i<tiles.Count;i++){
            var tile=tiles[i];if(tile.Bounds.IsEmpty)continue;bool hot=i==hover;
            using(var path=Round(tile.Bounds,radius))using(var fill=new SolidBrush(hot?p.SecondaryContainer:p.SurfaceHigh))g.FillPath(fill,path);
            var text=hot?p.OnSecondaryContainer:p.OnSurface;
            var icon=tile.Icon??(placeholder??=SystemIcons.Application.ToBitmap());
            g.DrawImage(icon,new Rectangle(tile.Bounds.X+inset,tile.Bounds.Y+(header-iconSize)/2,iconSize,iconSize));
            int textX=tile.Bounds.X+inset+iconSize+PixelsOf(6);
            TextRenderer.DrawText(g,tile.Title,font,new Rectangle(textX,tile.Bounds.Y,tile.Bounds.Right-inset-textX,header),text,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.Left);
            using(var path=Round(tile.Area,PixelsOf(8)))using(var well=new SolidBrush(p.Surface))g.FillPath(well,path);
            if(tile.Thumb==IntPtr.Zero){   // fallback tile: large icon + wrapped title
                int big=PixelsOf(32);
                g.DrawImage(icon,new Rectangle(tile.Area.X+(tile.Area.Width-big)/2,tile.Area.Y+PixelsOf(18),big,big));
                TextRenderer.DrawText(g,tile.Title,small,new Rectangle(tile.Area.X+inset,tile.Area.Y+PixelsOf(58),tile.Area.Width-2*inset,tile.Area.Height-PixelsOf(62)),p.OnSurfaceVariant,TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.HorizontalCenter|TextFormatFlags.Top);
            }
        }
    }
    protected override void Dispose(bool disposing){
        if(disposing){foreach(var tile in tiles){DwmThumb.Unregister(ref tile.Thumb);tile.Icon=null;}tiles.Clear();foreach(var bitmap in IconCache.Values)bitmap.Dispose();IconCache.Clear();placeholder?.Dispose();font.Dispose();small.Dispose();}
        base.Dispose(disposing);
    }
}

// Owns the (lazily created, destroyed on close) popup: open / retarget / refresh / grace-delayed close. UI thread only.
internal sealed class DockPreviewHost : IDisposable {
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int w,int h,uint flags);
    DockPreviewForm? form;System.Windows.Forms.Timer? closeTimer,safetyTimer;int anchorX,anchorY;DateTime? outsideSince;
    internal string OwnerId{get;private set;}="";
    internal bool IsOpen=>form!=null&&!form.IsDisposed;
    internal DockPreviewForm? Form=>form;
    // anchorX = physical x of the icon centre, anchorY = physical y of the dock top edge.
    internal void Open(string id,PreviewWindow[] windows,int ax,int ay,bool reduced){
        Disarm();
        if(windows.Length==0){Close();return;}
        var screen=Screen.FromPoint(new Point(ax,ay));double scale=TaskbarNative.Scale(screen);
        if(form!=null&&(form.IsDisposed||Math.Abs(form.Dpi-scale)>.01||form.Tag is bool wasLive&&wasLive==reduced))Close();
        OwnerId=id;anchorX=ax;anchorY=ay;
        if(form==null){form=new DockPreviewForm(!reduced,scale,screen.WorkingArea.Width-16){Tag=!reduced};form.Picked+=Pick;form.PointerEntered+=Disarm;form.PointerLeft+=()=>Retire(250);var created=form.Handle;}
        form.SetWindows(windows);Place(screen);
        if(!form.Visible){form.Show();Place(screen);form.UpdateThumbnails();}
        ArmSafety();
    }
    // Safety net while open (frontend died, mouse events lost): a 1 s check that only runs while the bubble exists.
    void ArmSafety(){
        outsideSince=null;
        if(safetyTimer==null){safetyTimer=new System.Windows.Forms.Timer{Interval=1000};safetyTimer.Tick+=(_,_)=>SafetyTick(DateTime.UtcNow);}
        safetyTimer.Enabled=true;
    }
    internal void SafetyTick(DateTime now){
        if(form==null||form.IsDisposed){if(safetyTimer!=null)safetyTimer.Enabled=false;return;}
        var cursor=Cursor.Position;var screen=Screen.FromPoint(new Point(anchorX,anchorY)).Bounds;
        var dock=new Rectangle(screen.Left,anchorY,screen.Width,Math.Max(1,screen.Bottom-anchorY));
        if(form.Bounds.Contains(cursor)||dock.Contains(cursor)){outsideSince=null;return;}
        outsideSince??=now;
        if((now-outsideSince.Value).TotalMilliseconds>1500)Close();
    }
    // Windows list changed while open: diff-update the existing tiles, keep the bubble where it is.
    internal void Update(PreviewWindow[] windows){
        if(!IsOpen)return;if(windows.Length==0){Close();return;}
        form!.SetWindows(windows);Place(Screen.FromPoint(new Point(anchorX,anchorY)));
    }
    void Place(Screen screen){
        if(form==null)return;var area=screen.WorkingArea;int margin=form.PixelsOf(8),w=form.Width,h=form.Height;
        int x=Math.Clamp(anchorX-w/2,area.Left+margin,Math.Max(area.Left+margin,area.Right-margin-w)),y=Math.Max(area.Top+margin,anchorY-margin-h);
        form.SetBounds(x,y,w,h);if(form.Visible)SetWindowPos(form.Handle,new IntPtr(-1),x,y,w,h,0x0010);   // TOPMOST, NOACTIVATE; the first show goes through Form.Show() only
    }
    void Pick(long hwnd){Raise(new IntPtr(hwnd));Close();}
    internal static void Raise(IntPtr window){if(!Native.IsWindow(window))return;if(TaskbarNative.IsIconic(window))TaskbarNative.ShowWindow(window,9);TaskbarNative.SetForegroundWindow(window);}
    void Disarm(){if(closeTimer!=null)closeTimer.Enabled=false;}
    // Armed only for the grace period (not repeating); stays open while the cursor is over the bubble.
    internal void Retire(int milliseconds){
        if(!IsOpen)return;
        closeTimer??=CreateTimer();closeTimer.Interval=Math.Max(1,milliseconds);closeTimer.Enabled=true;
    }
    System.Windows.Forms.Timer CreateTimer(){var t=new System.Windows.Forms.Timer();t.Tick+=(_,_)=>{t.Enabled=false;if(form!=null&&!form.IsDisposed&&form.Bounds.Contains(Cursor.Position))return;Close();};return t;}
    internal void Close(){
        Disarm();OwnerId="";outsideSince=null;if(safetyTimer!=null)safetyTimer.Enabled=false;
        var old=form;form=null;
        if(old!=null&&!old.IsDisposed){old.Hide();old.Dispose();}
    }
    public void Dispose(){Close();closeTimer?.Dispose();closeTimer=null;safetyTimer?.Dispose();safetyTimer=null;}
}
