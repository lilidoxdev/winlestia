// Icônes et miniatures du shell. Les requêtes n'existent que pour les conteneurs réalisés (ShellImage se lie au DataContext, annule au
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PanneauxGlass;

namespace WinlestiaFiles;

internal static partial class Thumbs
{
    static readonly Dictionary<int, IntPtr> imageLists = new();

    static BitmapSource? Factory(string path, int px, int flags)
    {
        IShellItemImageFactory? factory = null;
        IntPtr hbm = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            int hr0 = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            if (hr0 != 0 || factory == null)
            {
                if (Perf.Enabled)
                    Perf.Log($"thumb create hr {hr0:X}");
                return null;
            }
            var size = new SIZE { cx = px, cy = px };
            int hr = factory.GetImage(size, flags, out hbm);
            if (hr != 0 || hbm == IntPtr.Zero)
            {
                if (Perf.Enabled)
                    Perf.Log($"thumb getimage hr {hr:X} flags {flags}");
                return null;
            }
            var bmp = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) { if (Perf.Enabled) Perf.Log("thumb exception " + ex.GetType().Name + " " + ex.Message); return null; }
        finally
        {
            if (hbm != IntPtr.Zero)
                DeleteObject(hbm);
            if (factory != null)
                Marshal.ReleaseComObject(factory);
        }
    }

    // Une extension dont les 3 premières tentatives de miniature ont toutes échoué (txt, log, ...) passe directement à son icône pour
    // le reste de la session.
    static readonly Dictionary<string, int> thumbFail = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> thumbOk = new(StringComparer.OrdinalIgnoreCase);
    static bool ThumbBlocked(string ext)
    {
        lock (thumbFail)
            return !thumbOk.Contains(ext) && thumbFail.TryGetValue(ext, out int n) && n >= 3;
    }
    static void NoteThumb(string ext, bool ok)
    {
        lock (thumbFail)
        {
            if (ok)
                thumbOk.Add(ext);
            else
                thumbFail[ext] = thumbFail.TryGetValue(ext, out int n) ? n + 1 : 1;
        }
    }

    static long Bytes(BitmapSource b) => (long)b.PixelWidth * b.PixelHeight * 4;

    // ---- caches ------------------------------------------------------------------------------------------------
    static readonly Lru thumbCache = new(64L << 20, 4096), iconCache = new(12L << 20, 30000);

    sealed class Lru
    {
        readonly long budget; readonly int maxCount; long bytes;
        readonly Dictionary<string, LinkedListNode<(string key, BitmapSource bmp, long size)>> map = new();
        readonly LinkedList<(string key, BitmapSource bmp, long size)> order = new();
        internal Lru(long budget, int maxCount)
        {
            this.budget = budget;
            this.maxCount = maxCount;
        }

        internal BitmapSource? Get(string key)
        {
            lock (map)
            {
                if (!map.TryGetValue(key, out var node))
                    return null;
                order.Remove(node);
                order.AddFirst(node);
                return node.Value.bmp;
            }
        }
        // size < 0 (défaut) : compté à la taille réelle du bitmap ; 0 : alias d'une entrée stockée ailleurs.
        internal void Put(string key, BitmapSource bmp, long size = -1)
        {
            if (size < 0)
                size = Bytes(bmp);
            lock (map)
            {
                if (map.TryGetValue(key, out var old))
                {
                    bytes -= old.Value.size;
                    order.Remove(old);
                }
                map[key] = order.AddFirst((key, bmp, size));
                bytes += size;
                while ((bytes > budget || map.Count > maxCount) && order.Count > 1)
                {
                    var last = order.Last!;
                    order.RemoveLast();
                    map.Remove(last.Value.key);
                    bytes -= last.Value.size;
                }
            }
        }
        internal void Clear()
        {
            lock (map)
            {
                map.Clear();
                order.Clear();
                bytes = 0;
            }
        }
    }
}

// L'image d'une ligne/tuile. Se place sur le glyphe de substitution (un TextBlock frère nommé "Ic") et se remplit quand le shell
// répond.
public sealed class ShellImage : Image
{
    public bool Thumb
    {
        get; set;
    }
    public double Cell { get; set; } = 22;
    FileItem? current; Thumbs.Req? pending; TextBlock? glyph;

    public ShellImage()
    {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;
        // avant EndInit, les propriétés XAML (Thumb, Cell) ne sont pas encore appliquées
        DataContextChanged += (_, e) => { if (IsInitialized) Bind(e.NewValue as FileItem); };
        Unloaded += (_, _) => Cancel();
        Loaded += (_, _) => { if (Source == null && pending == null && DataContext is FileItem item) Bind(item); };
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        if (DataContext is FileItem item)
            Bind(item);
    }

    void Cancel()
    {
        if (pending != null)
        {
            pending.Cancelled = true;
            pending = null;
        }
    }

    void Bind(FileItem? item)
    {
        Cancel();
        current = item;
        if (Source != null)
        {
            BeginAnimation(OpacityProperty, null);
            Source = null;
            Opacity = 1;
            Glyph(true);
        }
        if (item == null || !Path.IsPathRooted(item.FullPath))
            return;
        string ext = item.IsDir ? "" : Path.GetExtension(item.Name).ToLowerInvariant();
        int px = Thumb ? Thumbs.ThumbPx(Cell) : Thumbs.IconBucket(Cell);
        string key = Thumbs.KeyOf(item, ext, Thumb, px);
        var hit = Thumbs.TryGet(key);
        if (hit != null)
        {
            Source = hit;
            Glyph(false);
            return;
        }
        pending = Thumbs.Request(item, ext, Thumb, px, key, bmp =>
        {
            pending = null;
            if (bmp == null || current != item)
                return;
            Source = bmp;
            if (M3.ReducedEffects)
            {
                Glyph(false);
                return;
            }
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(M3.AnimFast));
            fade.Completed += (_, _) => { if (current == item) Glyph(false); };
            BeginAnimation(OpacityProperty, fade);
        });
    }

    void Glyph(bool visible)
    {
        glyph ??= (Parent as Panel)?.Children is { } kids ? FindGlyph(kids) : null;
        if (glyph != null)
            glyph.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }
    static TextBlock? FindGlyph(UIElementCollection kids)
    {
        foreach (var k in kids)
            if (k is TextBlock { Name: "Ic" } t)
                return t;
        return null;
    }
}
