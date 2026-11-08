// C# 12 / .NET 8 / Windows 11. Bulle native d'aperçu des fenêtres pour le dock (survol d'une icône d'application -> une vignette par
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace PanneauxGlass;



internal sealed partial class DockPreviewForm : Form
{
    internal void UpdateThumbnails()
    {
        foreach (var tile in tiles)
        {
            if (tile.Bounds.IsEmpty)
                continue;
            var window = new IntPtr(tile.Hwnd);
            if (!live || TaskbarNative.IsIconic(window) || !Native.IsWindow(window))
            {
                DwmThumb.Unregister(ref tile.Thumb);
                continue;
            }        // minimized windows give empty thumbnails -> title + icon tile
            if (tile.Thumb == IntPtr.Zero && !DwmThumb.Register(Handle, window, out tile.Thumb, out tile.Source))
                continue;
            double fit = Math.Min(Math.Min((double)tile.Area.Width / tile.Source.Width, (double)tile.Area.Height / tile.Source.Height), 1);
            int w = Math.Max(1, (int)(tile.Source.Width * fit)), h = Math.Max(1, (int)(tile.Source.Height * fit));
            DwmThumb.Place(tile.Thumb, new Rectangle(tile.Area.X + (tile.Area.Width - w) / 2, tile.Area.Y + (tile.Area.Height - h) / 2, w,
                h));
        }
    }
    int Hit(Point p)
    {
        for (int i = 0; i < tiles.Count; i++)
            if (!tiles[i].Bounds.IsEmpty && tiles[i].Bounds.Contains(p))
                return i;
        return -1;
    }
    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        PointerEntered?.Invoke();
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hover != -1)
        {
            hover = -1;
            Invalidate();
        }
        PointerLeft?.Invoke();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int next = Hit(e.Location);
        if (next != hover)
        {
            hover = next;
            Cursor = next >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left)
            return;
        int index = Hit(e.Location);
        if (index >= 0)
            Picked?.Invoke(tiles[index].Hwnd);
    }
    static GraphicsPath Round(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(2, radius * 2);
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
    Bitmap? placeholder;
    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            DrawBubble(e);
        }
        catch (Exception ex) { Fault("OnPaint", ex); }
    }
    void DrawBubble(PaintEventArgs e)
    {
        M3Palette? pal = null;
        try
        {
            pal = Theme.Current?.P;
        }
        catch (Exception)
        {
            // Palette indisponible : couleurs de repli.
        }
        Color K(Func<M3Palette, System.Windows.Media.Color> pick, Color fallback) => pal == null ? fallback : C(pick(pal));
        var p = new
        {
            SurfaceContainer = K(x => x.SurfaceContainer, Color.FromArgb(32, 32, 36)),
            OutlineVariant = K(x => x.OutlineVariant, Color.FromArgb(80, 80, 88)),
            SecondaryContainer = K(x => x.SecondaryContainer, Color.FromArgb(64, 70, 92)),
            SurfaceHigh = K(x => x.SurfaceHigh, Color.FromArgb(46, 46, 52)),
            OnSecondaryContainer = K(x => x.OnSecondaryContainer, Color.White),
            OnSurface = K(x => x.OnSurface, Color.White),
            Surface = K(x => x.Surface, Color.FromArgb(24, 24, 28)),
            OnSurfaceVariant = K(x => x.OnSurfaceVariant, Color.Gainsboro)
        };
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(p.SurfaceContainer);
        using (var border = new Pen(p.OutlineVariant))
            g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        if (OverflowCount > 0)
            TextRenderer.DrawText(g, OverflowCount == 1 ? "+1 autre fenêtre" : "+" + OverflowCount + " autres fenêtres", font,
                overflowArea, p.OnSurfaceVariant,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.HorizontalCenter);
        int radius = PixelsOf(12), iconSize = PixelsOf(16), inset = PixelsOf(8), header = PixelsOf(26);
        for (int i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            if (tile.Bounds.IsEmpty)
                continue;
            bool hot = i == hover;
            using (var path = Round(tile.Bounds, radius))
            using (var fill = new SolidBrush(hot ? p.SecondaryContainer : p.SurfaceHigh))
                g.FillPath(fill, path);
            var text = hot ? p.OnSecondaryContainer : p.OnSurface;
            var icon = tile.Icon ?? (placeholder ??= SystemIcons.Application.ToBitmap());
            g.DrawImage(icon, new Rectangle(tile.Bounds.X + inset, tile.Bounds.Y + (header - iconSize) / 2, iconSize, iconSize));
            int textX = tile.Bounds.X + inset + iconSize + PixelsOf(6);
            TextRenderer.DrawText(g, tile.Title, font, new Rectangle(textX, tile.Bounds.Y, tile.Bounds.Right - inset - textX, header),
                text,
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.Left);
            using (var path = Round(tile.Area, PixelsOf(8)))
            using (var well = new SolidBrush(p.Surface))
                g.FillPath(well, path);
            if (tile.Thumb == IntPtr.Zero)
            {   // fallback tile: large icon + wrapped title
                int big = PixelsOf(32);
                g.DrawImage(icon, new Rectangle(tile.Area.X + (tile.Area.Width - big) / 2, tile.Area.Y + PixelsOf(18), big, big));
                TextRenderer.DrawText(g, tile.Title, small, new Rectangle(tile.Area.X + inset, tile.Area.Y + PixelsOf(58),
                    tile.Area.Width - 2 * inset, tile.Area.Height - PixelsOf(62)), p.OnSurfaceVariant,
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter
                        | TextFormatFlags.Top);
            }
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var tile in tiles)
            {
                DwmThumb.Unregister(ref tile.Thumb);
                tile.Icon = null;
            }
            tiles.Clear();
            foreach (var bitmap in IconCache.Values)
                bitmap.Dispose();
            IconCache.Clear();
            placeholder?.Dispose();
            font.Dispose();
            small.Dispose();
        }
        base.Dispose(disposing);
    }
}

// Possède la bulle (créée à la demande, détruite à la fermeture) : ouverture / changement de cible / rafraîchissement / fermeture
// après délai de grâce. Thread UI uniquement.
internal sealed class DockPreviewHost : IDisposable
{
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
    DockPreviewForm? form; System.Windows.Forms.Timer? closeTimer, safetyTimer; int anchorX, anchorY; DateTime? outsideSince;
    internal string OwnerId { get; private set; } = "";
    internal bool IsOpen => form != null && !form.IsDisposed;
    internal DockPreviewForm? Form => form;
    // anchorX = x physique du centre de l'icône, anchorY = y physique du bord haut du dock.
    internal void Open(string id, PreviewWindow[] windows, int ax, int ay, bool reduced)
    {
        Disarm();
        if (windows.Length == 0)
        {
            Close();
            return;
        }
        var screen = Screen.FromPoint(new Point(ax, ay));
        double scale = TaskbarNative.Scale(screen);
        if (form != null && (form.IsDisposed || Math.Abs(form.Dpi - scale) > .01 || form.Tag is bool wasLive && wasLive == reduced))
            Close();
        OwnerId = id;
        anchorX = ax;
        anchorY = ay;
        if (form == null)
        {
            form = new DockPreviewForm(!reduced, scale, screen.WorkingArea.Width - 16) { Tag = !reduced };
            form.Picked += Pick;
            form.PointerEntered += Disarm;
            form.PointerLeft += () => Retire(250);
            var created = form.Handle;
        }
        form.SetWindows(windows);
        Place(screen);
        if (!form.Visible)
        {
            form.Show();
            Place(screen);
            form.UpdateThumbnails();
        }
        ArmSafety();
    }
    // Filet de sécurité pendant l'ouverture (frontend mort, événements souris perdus) : une vérification à 1 s qui ne tourne que
    // tant que la bulle existe.
    void ArmSafety()
    {
        outsideSince = null;
        if (safetyTimer == null)
        {
            safetyTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            safetyTimer.Tick += (_, _) => SafetyTick(DateTime.UtcNow);
        }
        safetyTimer.Enabled = true;
    }
    internal void SafetyTick(DateTime now)
    {
        if (form == null || form.IsDisposed)
        {
            if (safetyTimer != null)
                safetyTimer.Enabled = false;
            return;
        }
        var cursor = Cursor.Position;
        var screen = Screen.FromPoint(new Point(anchorX, anchorY)).Bounds;
        var dock = new Rectangle(screen.Left, anchorY, screen.Width, Math.Max(1, screen.Bottom - anchorY));
        if (form.Bounds.Contains(cursor) || dock.Contains(cursor))
        {
            outsideSince = null;
            return;
        }
        outsideSince ??= now;
        if ((now - outsideSince.Value).TotalMilliseconds > 1500)
            Close();
    }
    // La liste des fenêtres a changé pendant l'ouverture : mise à jour différentielle des vignettes existantes, la bulle reste en
    // place.
    internal void Update(PreviewWindow[] windows)
    {
        if (!IsOpen)
            return;
        if (windows.Length == 0)
        {
            Close();
            return;
        }
        form!.SetWindows(windows);
        Place(Screen.FromPoint(new Point(anchorX, anchorY)));
    }
    void Place(Screen screen)
    {
        if (form == null)
            return;
        var area = screen.WorkingArea;
        int margin = form.PixelsOf(8), w = form.Width, h = form.Height;
        int x = Math.Clamp(anchorX - w / 2, area.Left + margin, Math.Max(area.Left + margin, area.Right - margin - w)),
            y = Math.Max(area.Top + margin, anchorY - margin - h);
        form.SetBounds(x, y, w, h);
        if (form.Visible)
            // TOPMOST, NOACTIVATE ; le premier affichage passe uniquement par Form.Show()
            SetWindowPos(form.Handle, new IntPtr(-1), x, y, w, h, 0x0010);
    }
    void Pick(long hwnd)
    {
        Raise(new IntPtr(hwnd));
        Close();
    }
    internal static void Raise(IntPtr window)
    {
        if (!Native.IsWindow(window))
            return;
        if (TaskbarNative.IsIconic(window))
            TaskbarNative.ShowWindow(window, 9);
        TaskbarNative.SetForegroundWindow(window);
    }
    void Disarm()
    {
        if (closeTimer != null)
            closeTimer.Enabled = false;
    }
    // Armé seulement pendant le délai de grâce (non répétitif) ; la bulle reste ouverte tant que le curseur la survole.
    internal void Retire(int milliseconds)
    {
        if (!IsOpen)
            return;
        closeTimer ??= CreateTimer();
        closeTimer.Interval = Math.Max(1, milliseconds);
        closeTimer.Enabled = true;
    }
    System.Windows.Forms.Timer CreateTimer()
    {
        var t = new System.Windows.Forms.Timer();
        t.Tick += (_, _) => { t.Enabled = false; if (form != null && !form.IsDisposed && form.Bounds.Contains(Cursor.Position)) return;
            Close(); };
        return t;
    }
    internal void Close()
    {
        Disarm();
        OwnerId = "";
        outsideSince = null;
        if (safetyTimer != null)
            safetyTimer.Enabled = false;
        var old = form;
        form = null;
        if (old != null && !old.IsDisposed)
        {
            old.Hide();
            old.Dispose();
        }
    }
    public void Dispose()
    {
        Close();
        closeTimer?.Dispose();
        closeTimer = null;
        safetyTimer?.Dispose();
        safetyTimer = null;
    }
}
