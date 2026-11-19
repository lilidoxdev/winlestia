// C# 12 / .NET 8 / Windows 11. Bulle native d'aperçu des fenêtres pour le dock (survol d'une icône d'application -> une vignette par
// fenêtre ouverte). Fenêtre outil au premier plan, sans activation, dessinée en GDI+ en pixels physiques ; miniatures DWM en direct
// quand c'est permis, titre + icône sinon.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace PanneauxGlass;

internal sealed record PreviewWindow(long Hwnd, string Title);

internal static class DwmThumb
{
    [StructLayout(LayoutKind.Sequential)]
    struct Props
    {
        public uint Flags; public Native.Rect Dest; public Native.Rect Src; public byte Opacity; public int Visible; public int ClientOnly;
    }
    [DllImport("dwmapi.dll")] static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUnregisterThumbnail(IntPtr thumb);
    [DllImport("dwmapi.dll")] static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref Props props);
    [DllImport("dwmapi.dll")] static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out Size size);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref int value, int size);
    internal static void RoundCorners(IntPtr window)
    {
        int round = 2;
        try
        {
            DwmSetWindowAttribute(window, 33, ref round, 4);
        }
        catch (Exception ex)
        {
            Store.Log("DockPreviewPopup.RoundCorners : " + ex.GetType().Name + ": " + ex.Message);
        }
    }   // DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND
    internal static bool Register(IntPtr dest, IntPtr source, out IntPtr thumb, out Size size)
    {
        thumb = IntPtr.Zero;
        size = default;
        try
        {
            if (DwmRegisterThumbnail(dest, source, out thumb) != 0 || thumb == IntPtr.Zero)
            {
                thumb = IntPtr.Zero;
                return false;
            }
            if (DwmQueryThumbnailSourceSize(thumb, out size) != 0 || size.Width <= 1 || size.Height <= 1)
            {
                Unregister(ref thumb);
                return false;
            }
            return true;
        }
        catch { thumb = IntPtr.Zero; return false; }
    }
    internal static void Place(IntPtr thumb, Rectangle destination)
    {
        var props = new Props
        {
            Flags = 1 | 4 | 8 | 0x10,
            Dest = new Native.Rect
            {
                Left = destination.Left,
                Top = destination.Top,
                Right = destination.Right,
                Bottom = destination.Bottom
            },
            Opacity = 255,
            Visible = 1,
            ClientOnly = 0
        };
        try
        {
            DwmUpdateThumbnailProperties(thumb, ref props);
        }
        catch (Exception)
        {
            // Miniature DWM rafraîchie à chaque cycle : un échec transitoire est sans conséquence.
        }
    }
    internal static void Unregister(ref IntPtr thumb)
    {
        if (thumb == IntPtr.Zero)
            return;
        IntPtr handle = thumb;
        Safe.Try(() => DwmUnregisterThumbnail(handle));
        thumb = IntPtr.Zero;
    }
}

internal sealed partial class DockPreviewForm : Form
{
    sealed class Tile
    {
        public long Hwnd; public string Title = ""; public Bitmap? Icon; public bool Pending; public IntPtr Thumb; public Size Source;
        public Rectangle Bounds, Area;
    }
    [DllImport("user32.dll")]
    static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wp, IntPtr lp, uint flags,
        uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr GetClassLongPtr(IntPtr window, int index);
    // Thread UI uniquement ; les bitmaps appartiennent à cette classe (les HICON empruntés sont copiés, jamais détruits) et sont
    // libérés à la destruction de la bulle
    static readonly Dictionary<long, Bitmap> IconCache = new();
    static bool faultLogged;
    static void Fault(string where, Exception ex)
    {
        if (faultLogged)
            return;
        faultLogged = true;
        try
        {
            Store.Log("Aperçu du dock (" + where + "): " + ex.Message);
        }
        catch (Exception)
        {
            // Le journal ne peut pas se journaliser lui-même.
        }
    }
    readonly List<Tile> tiles = new(); readonly bool live; readonly int maxWidth; readonly Font font, small; int hover = -1;
    Rectangle overflowArea;
    internal int OverflowCount
    {
        get; private set;
    }
    internal double Dpi
    {
        get;
    }
    internal event Action<long>? Picked;
    internal event Action? PointerEntered, PointerLeft;
    internal int TileCount => tiles.Count(t => !t.Bounds.IsEmpty);
    internal int RegisteredThumbnails => tiles.Count(t => t.Thumb != IntPtr.Zero);
    internal Rectangle TileBounds(int index) => index >= 0 && index < tiles.Count ? tiles[index].Bounds : Rectangle.Empty;
    internal long TileHwnd(int index) => tiles[index].Hwnd;
    internal int PixelsOf(double v) => (int)Math.Round(v * Dpi);
    internal DockPreviewForm(bool live, double scale, int maxWidth)
    {
        this.live = live;
        Dpi = scale;
        this.maxWidth = maxWidth;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        font = new Font("Segoe UI", (float)(12 * scale), FontStyle.Regular, GraphicsUnit.Pixel);
        small = new Font("Segoe UI", (float)(11 * scale), FontStyle.Regular, GraphicsUnit.Pixel);
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x80 | 0x08000000 | 0x8;
            return p;
        }
    }   // TOOLWINDOW | NOACTIVATE | TOPMOST
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DwmThumb.RoundCorners(Handle);
    }
    protected override void WndProc(ref Message m)
    {
        try
        {
            if (m.Msg == 0x02E0)
                // WM_DPICHANGED : cette fenêtre est dimensionnée en pixels physiques par l'hôte
                return;
            if (m.Msg == 0x0021)
            {
                m.Result = new IntPtr(3);
                return;
            }          // WM_MOUSEACTIVATE -> MA_NOACTIVATE
        }
        catch (Exception ex) { Fault("WndProc", ex); }
        base.WndProc(ref m);
    }
    static Color C(System.Windows.Media.Color c) => Color.FromArgb(c.A, c.R, c.G, c.B);
    static Bitmap? CopyIcon(IntPtr icon)
    {
        try
        {
            if (icon == IntPtr.Zero)
                return null;
            using var source = Icon.FromHandle(icon);
            return source.ToBitmap();
        }
        catch { return null; }
    }   // Icon.FromHandle ne possède pas le HICON
    void Remember(long hwnd, Bitmap bitmap)
    {
        if (IconCache.Count >= 64)
            foreach (var key in IconCache.Keys.ToArray())
                if (!tiles.Any(t => t.Hwnd == key))
                {
                    IconCache[key].Dispose();
                    IconCache.Remove(key);
                }
        if (IconCache.TryGetValue(hwnd, out var old) && !ReferenceEquals(old, bitmap) && !tiles.Any(t => ReferenceEquals(t.Icon, old)))
            old.Dispose();
        IconCache[hwnd] = bitmap;
    }
    // Icône de classe non bloquante d'abord ; le repli bloquant WM_GETICON tourne sur un thread de travail pour qu'une fenêtre figée ne
    // gèle jamais l'interface.
    void LoadIcon(Tile tile)
    {
        if (IconCache.TryGetValue(tile.Hwnd, out var cached))
        {
            tile.Icon = cached;
            return;
        }
        var window = new IntPtr(tile.Hwnd);
        var quick = CopyIcon(GetClassLongPtr(window, -34));
        quick ??= CopyIcon(GetClassLongPtr(window, -14));
        if (quick != null)
        {
            Remember(tile.Hwnd, quick);
            tile.Icon = quick;
            return;
        }
        if (tile.Pending || !IsHandleCreated)
            return;
        tile.Pending = true;
        System.Threading.Tasks.Task.Run(() =>
        {
            IntPtr icon = IntPtr.Zero;
            try
            {
                SendMessageTimeoutW(window, 0x007F, new IntPtr(2), IntPtr.Zero, 0x2, 40, out icon);
                if (icon == IntPtr.Zero)
                    SendMessageTimeoutW(window, 0x007F, new IntPtr(1), IntPtr.Zero, 0x2, 40, out icon);
            }
            catch { icon = IntPtr.Zero; }
            try
            {
                BeginInvoke((Action)(() =>
                {
                    tile.Pending = false;
                    if (IsDisposed)
                        return;
                    var bitmap = CopyIcon(icon) ?? SystemIcons.Application.ToBitmap();
                    Remember(tile.Hwnd, bitmap);
                    tile.Icon = bitmap;
                    Invalidate();
                }));
            }
            catch (Exception ex)
            {
                Store.Log("DockPreviewPopup.LoadIcon : " + ex.GetType().Name + ": " + ex.Message);
            }
        });
    }
    // Différentiel par hwnd : les vignettes (et leurs miniatures DWM) des fenêtres qui subsistent sont conservées, celles des fenêtres
    // disparues sont libérées.
    internal void SetWindows(PreviewWindow[] windows)
    {
        var wanted = windows.Select(w => w.Hwnd).ToHashSet();
        foreach (var gone in tiles.Where(t => !wanted.Contains(t.Hwnd)).ToArray())
        {
            DwmThumb.Unregister(ref gone.Thumb);
            tiles.Remove(gone);
        }
        var ordered = new List<Tile>();
        foreach (var w in windows)
        {
            var tile = tiles.FirstOrDefault(t => t.Hwnd == w.Hwnd);
            if (tile == null)
            {
                tile = new Tile { Hwnd = w.Hwnd };
                ordered.Add(tile);
                LoadIcon(tile);
            }
            else
                ordered.Add(tile);
            tile.Title = w.Title;
        }
        tiles.Clear();
        tiles.AddRange(ordered);
        hover = -1;
        Arrange();
    }
    void Arrange()
    {
        int pad = PixelsOf(10), gap = PixelsOf(8), tw = PixelsOf(216), header = PixelsOf(26), thumbH = PixelsOf(120), th = header + thumbH
            + PixelsOf(8), inset = PixelsOf(8);
        int perRow = Math.Max(1, (maxWidth - 2 * pad + gap) / (tw + gap)), shown = Math.Min(tiles.Count, perRow * 3), cols = Math.Max(1,
            Math.Min(shown, perRow)), rows = Math.Max(1, (shown + cols - 1) / cols);
        for (int i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            if (i >= shown)
            {
                DwmThumb.Unregister(ref tile.Thumb);
                tile.Bounds = tile.Area = Rectangle.Empty;
                continue;
            }
            tile.Bounds = new Rectangle(pad + (i % cols) * (tw + gap), pad + (i / cols) * (th + gap), tw, th);
            tile.Area = new Rectangle(tile.Bounds.X + inset, tile.Bounds.Y + header, tw - 2 * inset, thumbH);
        }
        OverflowCount = tiles.Count - shown;
        int gridH = rows * th + (rows - 1) * gap, labelH = OverflowCount > 0 ? header : 0;
        overflowArea = OverflowCount > 0 ? new Rectangle(pad, pad + gridH + gap / 2, cols * tw + (cols - 1) * gap, labelH)
            : Rectangle.Empty;
        ClientSize = new Size(2 * pad + cols * tw + (cols - 1) * gap, 2 * pad + gridH + (OverflowCount > 0 ? gap / 2 + labelH : 0));
        if (IsHandleCreated)
            UpdateThumbnails();
        Invalidate();
    }
}
