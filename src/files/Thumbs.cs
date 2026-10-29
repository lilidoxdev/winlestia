// Shell icons and thumbnails. Requests exist only for realized containers (ShellImage binds on DataContext, cancels on rebind/unload).
// A small pool of STA threads (BelowNormal) serves them newest-batch-first; results become frozen BitmapSources posted to the UI at Background priority.
// Two byte-budgeted LRU caches (thumbnails, icons). No timer: idle workers block on a Monitor. Reset() (resident hide) clears caches and ends the threads.
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

internal static class Thumbs {
    // ---- native ----------------------------------------------------------------------------------------------
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr hbitmap); }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint attrs; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string type; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("shell32.dll", PreserveSig = true)] static extern int SHGetImageList(int list, ref Guid riid, out IntPtr imageList);
    [DllImport("comctl32.dll")] static extern IntPtr ImageList_GetIcon(IntPtr list, int index, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    const int SIIGBF_BIGGERSIZEOK = 1, SIIGBF_ICONONLY = 4, SIIGBF_THUMBNAILONLY = 8;
    const uint SHGFI_SYSICONINDEX = 0x4000, SHGFI_USEFILEATTRIBUTES = 0x10;

    // ---- public surface ----------------------------------------------------------------------------------------
    internal static double Dpi = 1.0;                        // set by the window (DPI aware sizes)
    internal static int Pending;                              // live requests, for measurement
    internal static int Shown;

    internal sealed class Req {
        internal string Path = "", Key = "", Ext = ""; internal bool Dir, Thumb; internal int Px;
        internal long Batch, Seq; internal volatile bool Cancelled; internal Action<BitmapSource?> Done = null!;
    }

    // Sizes: SHIL bucket for icons (16/32/48/256), requested pixel size for thumbnails.
    internal static int IconBucket(double dips) { double px = dips * Dpi; return px <= 16 ? 16 : px <= 32 ? 32 : px <= 48 ? 48 : 256; }
    internal static int ThumbPx(double dips) => (int)Math.Clamp(Math.Round(dips * Dpi), 96, 128);

    static bool PerPath(string ext) => ext is ".exe" or ".ico" or ".lnk" or ".url" or ".scr" or ".msi" or ".appref-ms" or ".cur" or ".ani";

    // Cache key of the image an item would show; the same key is what the worker stores under.
    internal static string KeyOf(FileItem item, string ext, bool thumb, int px) =>
        item.IsDir || PerPath(ext) ? "p:" + item.FullPath + "|" + px
        : thumb && !ThumbBlocked(ext) ? "t:" + item.FullPath + "|" + px
        : "e:" + ext + "|" + px;

    internal static BitmapSource? TryGet(string key) => (key[0] == 't' ? thumbCache : iconCache).Get(key);

    internal static Req? Request(FileItem item, string ext, bool thumb, int px, string key, Action<BitmapSource?> done) {
        var r = new Req { Path = item.FullPath, Ext = ext, Dir = item.IsDir, Thumb = thumb && key[0] == 't', Px = px, Key = key, Done = done,
            Batch = Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 50), Seq = Interlocked.Increment(ref seq) };
        Interlocked.Increment(ref Pending);
        lock (queue) {
            if (queue.Count > 512) queue.RemoveAll(q => { if (!q.Cancelled) return false; Interlocked.Decrement(ref Pending); return true; });
            queue.Add(r);
            if (workers == null) StartWorkers();
            Monitor.Pulse(queue);
        }
        return r;
    }

    // Resident hide / tab drop: forget everything and let the threads end (they restart on the next request).
    internal static void Reset() {
        lock (queue) {
            foreach (var r in queue) { r.Cancelled = true; Interlocked.Decrement(ref Pending); }
            queue.Clear();
            if (workers != null) { workers.Stop = true; workers = null; }
            Monitor.PulseAll(queue);
        }
        thumbCache.Clear(); iconCache.Clear();
        lock (thumbFail) { thumbFail.Clear(); thumbOk.Clear(); }
    }

    // ---- thread pool -------------------------------------------------------------------------------------------
    sealed class WorkerSet { internal volatile bool Stop; }
    static readonly List<Req> queue = new();
    static WorkerSet? workers;
    static long seq;

    static void StartWorkers() {
        var set = workers = new WorkerSet();
        int n = Math.Clamp(Environment.ProcessorCount / 4, 2, 3);
        for (int i = 0; i < n; i++) {
            var t = new Thread(() => Work(set)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Winlestia thumbs" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }
    }

    // Newest batch first (what the user just scrolled to), oldest first inside one batch (top to bottom).
    static Req? Next() {
        Req? best = null;
        for (int i = queue.Count - 1; i >= 0; i--) {
            if (i >= queue.Count) continue;                  // defensive: never index past a queue that shrank (callers hold the lock, this is belt and braces)
            var q = queue[i];
            if (q.Cancelled) { queue.RemoveAt(i); Interlocked.Decrement(ref Pending); continue; }
            if (best == null || q.Batch > best.Batch || (q.Batch == best.Batch && q.Seq < best.Seq)) best = q;
        }
        if (best != null) queue.Remove(best);
        return best;
    }

    static void Work(WorkerSet set) {
        while (true) {
            Req? r = null;
            lock (queue) {
                while (!set.Stop && (r = Next()) == null) Monitor.Wait(queue);
                if (set.Stop) return;
            }
            if (r == null) continue;
            BitmapSource? bmp = null;
            try { if (!r.Cancelled) bmp = Load(r); } catch { }
            var req = r; var result = bmp;
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => {
                int left = Interlocked.Decrement(ref Pending);
                if (!req.Cancelled) { Interlocked.Increment(ref Shown); req.Done(result); }
                if (left == 0 && Perf.Enabled) Perf.Log($"icons settled {Perf.SinceProcess():F0} ms since process start (shown {Shown})");
            });
        }
    }

    // ---- loading (worker thread) -------------------------------------------------------------------------------
    static BitmapSource? Load(Req r) {
        BitmapSource? bmp = null;
        if (r.Thumb) {
            long t0 = Stopwatch.GetTimestamp();
            bmp = Factory(r.Path, r.Px, SIIGBF_THUMBNAILONLY);
            if (Perf.Enabled) Perf.Log($"thumb {r.Path} ok={bmp != null} {Perf.Ms(t0):F1} ms");
            NoteThumb(r.Ext, bmp != null);
            if (bmp != null) { thumbCache.Put(r.Key, bmp); return bmp; }
        }
        int bucket = r.Thumb ? IconBucketFor(r.Px) : r.Px;
        bool unique = r.Dir ? HasDesktopIni(r.Path) : PerPath(r.Ext);
        if (unique) bmp = Factory(r.Path, bucket, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK);
        if (bmp == null) bmp = Generic(r.Dir, r.Ext, bucket);
        if (bmp != null) (r.Key[0] == 't' ? thumbCache : iconCache).Put(r.Key, bmp, unique ? -1 : 0);   // generic images are counted once, under their icon index
        return bmp;
    }

    static int IconBucketFor(int thumbPx) => thumbPx <= 48 ? 48 : 256;

    static bool HasDesktopIni(string dir) {
        try { return File.Exists(Path.Combine(dir, "desktop.ini")); } catch { return false; }
    }

    // Stock image of an extension or of a plain folder: no disk access (USEFILEATTRIBUTES), one bitmap per system icon index.
    static BitmapSource? Generic(bool dir, string ext, int bucket) {
        var info = new SHFILEINFO();
        string probe = dir ? "folder" : "file" + (ext.Length > 0 ? ext : ".");
        if (SHGetFileInfo(probe, dir ? 0x10u : 0x80u, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX | SHGFI_USEFILEATTRIBUTES) == IntPtr.Zero) return null;
        string key = "i:" + info.iIcon + "|" + bucket;
        var hit = iconCache.Get(key);
        if (hit != null) return hit;
        int shil = bucket <= 16 ? 1 : bucket <= 32 ? 0 : bucket <= 48 ? 2 : 4;   // SMALL, LARGE, EXTRALARGE, JUMBO
        var iid = new Guid("46eb5926-582e-4017-9fdf-e8998daa0950");              // IImageList
        if (!imageLists.TryGetValue(shil, out var list)) {
            if (SHGetImageList(shil, ref iid, out list) != 0 || list == IntPtr.Zero) return null;
            lock (imageLists) imageLists[shil] = list;
        }
        IntPtr icon = ImageList_GetIcon(list, info.iIcon, 0);
        if (icon == IntPtr.Zero) return null;
        try {
            var bmp = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            iconCache.Put(key, bmp);
            return bmp;
        } finally { DestroyIcon(icon); }
    }
    static readonly Dictionary<int, IntPtr> imageLists = new();

    static BitmapSource? Factory(string path, int px, int flags) {
        IShellItemImageFactory? factory = null; IntPtr hbm = IntPtr.Zero;
        try {
            var iid = typeof(IShellItemImageFactory).GUID;
            int hr0 = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            if (hr0 != 0 || factory == null) { if (Perf.Enabled) Perf.Log($"thumb create hr {hr0:X}"); return null; }
            var size = new SIZE { cx = px, cy = px };
            int hr = factory.GetImage(size, flags, out hbm);
            if (hr != 0 || hbm == IntPtr.Zero) { if (Perf.Enabled) Perf.Log($"thumb getimage hr {hr:X} flags {flags}"); return null; }
            var bmp = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        } catch (Exception ex) { if (Perf.Enabled) Perf.Log("thumb exception " + ex.GetType().Name + " " + ex.Message); return null; }
        finally {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }

    // An extension whose first 3 thumbnail attempts all failed (txt, log, ...) goes straight to its icon for the rest of the session.
    static readonly Dictionary<string, int> thumbFail = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> thumbOk = new(StringComparer.OrdinalIgnoreCase);
    static bool ThumbBlocked(string ext) { lock (thumbFail) return !thumbOk.Contains(ext) && thumbFail.TryGetValue(ext, out int n) && n >= 3; }
    static void NoteThumb(string ext, bool ok) {
        lock (thumbFail) { if (ok) thumbOk.Add(ext); else thumbFail[ext] = thumbFail.TryGetValue(ext, out int n) ? n + 1 : 1; }
    }

    static long Bytes(BitmapSource b) => (long)b.PixelWidth * b.PixelHeight * 4;

    // ---- caches ------------------------------------------------------------------------------------------------
    static readonly Lru thumbCache = new(64L << 20, 4096), iconCache = new(12L << 20, 30000);

    sealed class Lru {
        readonly long budget; readonly int maxCount; long bytes;
        readonly Dictionary<string, LinkedListNode<(string key, BitmapSource bmp, long size)>> map = new();
        readonly LinkedList<(string key, BitmapSource bmp, long size)> order = new();
        internal Lru(long budget, int maxCount) { this.budget = budget; this.maxCount = maxCount; }

        internal BitmapSource? Get(string key) {
            lock (map) {
                if (!map.TryGetValue(key, out var node)) return null;
                order.Remove(node); order.AddFirst(node);
                return node.Value.bmp;
            }
        }
        // size < 0 (default): counted at the bitmap's real size; 0: an alias of an entry stored elsewhere.
        internal void Put(string key, BitmapSource bmp, long size = -1) {
            if (size < 0) size = Bytes(bmp);
            lock (map) {
                if (map.TryGetValue(key, out var old)) { bytes -= old.Value.size; order.Remove(old); }
                map[key] = order.AddFirst((key, bmp, size)); bytes += size;
                while ((bytes > budget || map.Count > maxCount) && order.Count > 1) {
                    var last = order.Last!; order.RemoveLast(); map.Remove(last.Value.key); bytes -= last.Value.size;
                }
            }
        }
        internal void Clear() { lock (map) { map.Clear(); order.Clear(); bytes = 0; } }
    }
}

// The image of one row/tile. Sits over the glyph placeholder (a sibling TextBlock named "Ic") and fills in when the shell answers.
public sealed class ShellImage : Image {
    public bool Thumb { get; set; }
    public double Cell { get; set; } = 22;
    FileItem? current; Thumbs.Req? pending; TextBlock? glyph;

    public ShellImage() {
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Stretch = Stretch.Uniform; IsHitTestVisible = false;
        DataContextChanged += (_, e) => { if (IsInitialized) Bind(e.NewValue as FileItem); };   // before EndInit the XAML properties (Thumb, Cell) are not applied yet
        Unloaded += (_, _) => Cancel();
        Loaded += (_, _) => { if (Source == null && pending == null && DataContext is FileItem item) Bind(item); };
    }

    protected override void OnInitialized(EventArgs e) { base.OnInitialized(e); if (DataContext is FileItem item) Bind(item); }

    void Cancel() { if (pending != null) { pending.Cancelled = true; pending = null; } }

    void Bind(FileItem? item) {
        Cancel();
        current = item;
        if (Source != null) { BeginAnimation(OpacityProperty, null); Source = null; Opacity = 1; Glyph(true); }
        if (item == null || !Path.IsPathRooted(item.FullPath)) return;
        string ext = item.IsDir ? "" : Path.GetExtension(item.Name).ToLowerInvariant();
        int px = Thumb ? Thumbs.ThumbPx(Cell) : Thumbs.IconBucket(Cell);
        string key = Thumbs.KeyOf(item, ext, Thumb, px);
        var hit = Thumbs.TryGet(key);
        if (hit != null) { Source = hit; Glyph(false); return; }
        pending = Thumbs.Request(item, ext, Thumb, px, key, bmp => {
            pending = null;
            if (bmp == null || current != item) return;
            Source = bmp;
            if (M3.ReducedEffects) { Glyph(false); return; }
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(M3.AnimFast));
            fade.Completed += (_, _) => { if (current == item) Glyph(false); };
            BeginAnimation(OpacityProperty, fade);
        });
    }

    void Glyph(bool visible) {
        glyph ??= (Parent as Panel)?.Children is { } kids ? FindGlyph(kids) : null;
        if (glyph != null) glyph.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }
    static TextBlock? FindGlyph(UIElementCollection kids) { foreach (var k in kids) if (k is TextBlock { Name: "Ic" } t) return t; return null; }
}
