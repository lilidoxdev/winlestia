// C# 12 / WindowTiler.cs / Windows 11. Tuilage réversible par moniteur ; aucun hook global d'entrée.
using System.Diagnostics;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
namespace PanneauxGlass;

internal sealed partial class WindowTiler : IDisposable
{
    // Bouton de souris encore enfoncé : on repousse le tuilage jusqu'au relâchement.
    const int DragRetryMs = 150;
    // Filet de sécurité : resynchronise les fenêtres si un événement système a été manqué.
    const int ResyncMs = 2000;
    [StructLayout(LayoutKind.Sequential)]
    internal struct Placement
    {
        public int Length, Flags, Show; public Native.Point Min, Max; public Native.Rect Normal;
    }
    sealed class Managed
    {
        internal Placement Original; internal uint Pid; internal Native.Rect Last; internal bool Placed; internal Native.Rect? Constrained;
    }
    [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr h, ref Placement placement);
    [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr h, ref Placement placement);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h, int command);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr Style(IntPtr h, int index);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint command);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out Native.Rect rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref Native.Point point);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out int value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] static extern int ExtendedBounds(IntPtr h, int attribute,
        out Native.Rect value, int size);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid,
        uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr BeginDeferWindowPos(int count);
    [DllImport("user32.dll")] static extern IntPtr DeferWindowPos(IntPtr hdwp, IntPtr h, IntPtr after, int x, int y, int cx, int cy,
        uint flags);
    [DllImport("user32.dll")] static extern bool EndDeferWindowPos(IntPtr hdwp);
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr h, int idObject, int idChild, uint thread, uint time);
    const int AnimationMs = 120;
    readonly Dictionary<IntPtr, Managed> managed = new(); readonly DispatcherTimer timer = new() {
        Interval = TimeSpan.FromMilliseconds(ResyncMs) }; readonly Func<IEnumerable<IntPtr>> source; readonly bool fixtureSource;
        bool enabled, disposed;
    sealed class Motion
    {
        internal Native.Rect From, To, Shadow; internal IntPtr Monitor; internal long Started; internal Native.Rect? Applied;
    }
    readonly Dictionary<IntPtr, Motion> motions = new();
    readonly DispatcherTimer animation = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    readonly DispatcherTimer coalesce = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(25) };
    readonly HashSet<IntPtr> failing = new();
    readonly WinEventProc hookProc, nameProc; readonly List<IntPtr> hooks = new();
    internal uint ExcludedProcess;
    // Cadre externe moins les bornes DWM visibles, mesuré une fois quand une cible est fixée (pas à chaque image d'animation).
    static Native.Rect Shadow(IntPtr h)
    {
        var visible = Visible(h);
        TaskbarNative.GetWindowRect(h, out var outer);
        return new()
        {
            Left = outer.Left - visible.Left,
            Top = outer.Top - visible.Top,
            Right = outer.Right - visible.Right,
            Bottom = outer.Bottom - visible.Bottom
        };
    }
    static (int x, int y, int w, int h) Box((IntPtr h, Native.Rect wanted, Native.Rect shadow) m) => (m.wanted.Left + m.shadow.Left,
        m.wanted.Top + m.shadow.Top, m.wanted.Right + m.shadow.Right - m.wanted.Left - m.shadow.Left, m.wanted.Bottom
        + m.shadow.Bottom - m.wanted.Top - m.shadow.Top);
    // Un lot DeferWindowPos par image ; retombe sur SetWindowPos fenêtre par fenêtre (0x4014) si le lot échoue. 0x4000 garde le lot non
    // bloquant sur les fenêtres figées, comme l'ancien chemin.
    void Apply(List<(IntPtr h, Native.Rect wanted, Native.Rect shadow)> list)
    {
        if (list.Count == 0)
            return;
        void Place((IntPtr h, Native.Rect wanted, Native.Rect shadow) m)
        {
            var b = Box(m);
            if (TaskbarNative.SetWindowPos(m.h, IntPtr.Zero, b.x, b.y, b.w, b.h, 0x4014))
            {
                failing.Remove(m.h);
                if (managed.TryGetValue(m.h, out var item))
                {
                    item.Last = m.wanted;
                    item.Placed = true;
                }
            }
            else
                failing.Add(m.h);
        }
        // Les fenêtres qui ont déjà échoué (élevées/UIPI) sont déplacées seules pour qu'elles ne fassent pas couler tout le lot ;
        // si elles
        // échouent encore, elles sont ignorées.
        var good = list.Where(m => !failing.Contains(m.h)).ToList();
        foreach (var m in list.Where(m => failing.Contains(m.h)))
            Place(m);
        if (good.Count == 0)
            return;
        var hdwp = BeginDeferWindowPos(good.Count);
        bool ok = hdwp != IntPtr.Zero;
        if (ok)
            foreach (var m in good)
            {
                var b = Box(m);
                hdwp = DeferWindowPos(hdwp, m.h, IntPtr.Zero, b.x, b.y, b.w, b.h, 0x4 | 0x10 | 0x200 | 0x4000);
                if (hdwp == IntPtr.Zero)
                {
                    ok = false;
                    break;
                }
            }
        if (ok)
            ok = EndDeferWindowPos(hdwp);
        if (ok)
        {
            foreach (var m in good)
                if (managed.TryGetValue(m.h, out var item))
                {
                    item.Last = m.wanted;
                    item.Placed = true;
                }
        }
        else
            foreach (var m in good)
                Place(m);
    }
    void Animate()
    {
        bool dragging = (GetAsyncKeyState(1) & 0x8000) != 0;
        var batch = new List<(IntPtr h, Native.Rect wanted, Native.Rect shadow)>();
        var finished = new List<IntPtr>();
        foreach (var pair in motions)
        {
            var h = pair.Key;
            var motion = pair.Value;
            GetWindowThreadProcessId(h, out uint pid);
            if (dragging || !managed.TryGetValue(h, out var item) || pid != item.Pid || !Native.IsWindow(h) || TaskbarNative.IsIconic(h)
                || IsZoomed(h))
            {
                finished.Add(h);
                continue;
            }
            double t = Math.Clamp(Stopwatch.GetElapsedTime(motion.Started).TotalMilliseconds / AnimationMs, 0, 1),
                ease = 1 - Math.Pow(1 - t, 3);
            if (motion.Monitor != IntPtr.Zero)
            {
                var mon = MonitorFromWindow(h, 2);
                if (mon != motion.Monitor)
                {
                    motion.Monitor = mon;
                    motion.Shadow = Shadow(h);
                }
            }
            int Lerp(int a, int b) => (int)Math.Round(a + (b - a) * ease);
            var frame = t >= 1 ? motion.To : new Native.Rect { Left = Lerp(motion.From.Left, motion.To.Left), Top = Lerp(motion.From.Top,
                motion.To.Top), Right = Lerp(motion.From.Right, motion.To.Right), Bottom = Lerp(motion.From.Bottom, motion.To.Bottom) };
            if (!(motion.Applied is Native.Rect applied && applied.Left == frame.Left && applied.Top == frame.Top
                && applied.Right == frame.Right && applied.Bottom == frame.Bottom))
            {
                motion.Applied = frame;
                batch.Add((h, frame, motion.Shadow));
            }
            if (t >= 1)
                finished.Add(h);
        }
        Apply(batch);
        foreach (var h in finished)
            motions.Remove(h);
        if (motions.Count == 0)
            animation.Stop();
    }

    internal WindowTiler(Func<IEnumerable<IntPtr>>? source = null)
    {
        fixtureSource = source != null;
        this.source = source ?? Collect;
        hookProc = OnWinEvent;
        nameProc = OnNameChange;
        timer.Tick += (_, _) => Tick();
        animation.Tick += (_, _) => Animate();
        coalesce.Tick += (_, _) => { coalesce.Stop(); if ((GetAsyncKeyState(1) & 0x8000) != 0) {
            coalesce.Interval = TimeSpan.FromMilliseconds(DragRetryMs); coalesce.Start(); return;
            } coalesce.Interval = TimeSpan.FromMilliseconds(25); Tick(); };
    }
    // Hooks WinEvent (hors contexte, thread UI) : seuls les événements de fenêtres de premier niveau déclenchent un Tick regroupé
    // environ 25 ms plus tard.
    void OnWinEvent(IntPtr hook, uint ev, IntPtr h, int idObject, int idChild, uint thread, uint time)
    {
        if (enabled && ev == 0x0B && h != IntPtr.Zero && idObject == 0 && idChild == 0)
        {
            try
            {
                Dropped(h);
            }
            catch (Exception ex) { Store.Log("Tuilage · dépôt : " + ex.Message); }
            return;
        }
        if (!enabled || h == IntPtr.Zero || idObject != 0 || idChild != 0 || coalesce.IsEnabled)
            return;
        if (ev is 3 or 0x16 or 0x17 or 0x8001 or 0x8002 or 0x8017 or 0x8018)
            coalesce.Start();
    }
    // EVENT_OBJECT_NAMECHANGE a son propre hook (ne jamais élargir la plage au-dessus) ; sorties anticipées quasi gratuites.
    void OnNameChange(IntPtr hook, uint ev, IntPtr h, int idObject, int idChild, uint thread, uint time)
    {
        if (!enabled || idObject != 0 || idChild != 0 || coalesce.IsEnabled || h == IntPtr.Zero || managed.ContainsKey(h) || !Eligible(h))
            return;
        coalesce.Start();
    }
    // La source de fixtures vit dans notre propre processus, donc SKIPOWNPROCESS n'est utilisé que sur le chemin du vrai bureau.
    void Hook()
    {
        if (hooks.Count > 0)
            return;
        uint flags = (uint)(fixtureSource ? 0 : 2);
        foreach (var (min, max) in new[] { (3u, 0x17u), (0x8001u, 0x8002u), (0x8017u, 0x8018u) })
        {
            var hook = SetWinEventHook(min, max, IntPtr.Zero, hookProc, 0, 0, flags);
            if (hook != IntPtr.Zero)
                hooks.Add(hook);
        }
        var nameHook = SetWinEventHook(0x800C, 0x800C, IntPtr.Zero, nameProc, 0, 0, flags);
        if (nameHook != IntPtr.Zero)
            hooks.Add(nameHook);
    }
    void Unhook()
    {
        foreach (var hook in hooks)
            UnhookWinEvent(hook);
        hooks.Clear();
        coalesce.Stop();
    }
    internal void SetEnabled(bool value)
    {
        if (disposed || enabled == value)
            return;
        enabled = value;
        if (value)
        {
            Hook();
            timer.Start();
            Keys(true);
        }
        else
        {
            Unhook();
            timer.Stop();
            Keys(false);
            Restore();
        }
    }
    IEnumerable<IntPtr> Collect()
    {
        var list = new List<IntPtr>();
        TaskbarNative.EnumWindows((h, _) => { if (Eligible(h)) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    bool Eligible(IntPtr h)
    {
        if (floating.Contains(h))
            return false;
        if (!Native.IsWindow(h) || !TaskbarNative.IsWindowVisible(h) || TaskbarNative.IsIconic(h) || (!fixtureSource && Native.Owns(h)))
            return false;
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == ExcludedProcess || Excluded(pid))
            return false;
        long style = Style(h, -16).ToInt64(), ex = Style(h, -20).ToInt64();
        if ((style & 0x40000) == 0 || (style & 0x40000000) != 0 || (!fixtureSource && (ex & 0x80) != 0) || GetWindow(h, 4) != IntPtr.Zero)
            return false;
        if (DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0)
            return false;
        return !string.IsNullOrWhiteSpace(Native.Title(h))
            && TaskbarNative.ClassName(h) is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }
    static Native.Rect Visible(IntPtr h)
    {
        TaskbarNative.GetWindowRect(h, out var rect);
        return ExtendedBounds(h, 9, out var frame, Marshal.SizeOf<Native.Rect>()) == 0 ? frame : rect;
    }
    static bool Same(Native.Rect a, Native.Rect b) => Math.Abs(a.Left - b.Left) <= 2 && Math.Abs(a.Top - b.Top) <= 2
        && Math.Abs(a.Right - b.Right) <= 2 && Math.Abs(a.Bottom - b.Bottom) <= 2;
    static bool Fullscreen(IntPtr h, Forms.Screen screen)
    {
        var point = new Native.Point();
        return GetClientRect(h, out var client) && ClientToScreen(h, ref point) && Math.Abs(point.X - screen.Bounds.Left) <= 2
            && Math.Abs(point.Y - screen.Bounds.Top) <= 2 && Math.Abs(point.X + client.Right - screen.Bounds.Right) <= 2
                && Math.Abs(point.Y
            + client.Bottom - screen.Bounds.Bottom) <= 2;
    }
    internal static Native.Rect[] Layout(Native.Rect area, int count, int gap) => TileLogic.Layout(area, count, gap);
    internal void Tick()
    {
        if (!enabled || disposed || (GetAsyncKeyState(1) & 0x8000) != 0)
            return;
        try
        {
            processNames.Clear();
            foreach (var h in managed.Keys.ToArray())
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (!Native.IsWindow(h) || pid != managed[h].Pid)
                {
                    managed.Remove(h);
                    motions.Remove(h);
                    failing.Remove(h);
                }
            }
            var handles = source().Where(Eligible).ToArray();
            var added = new HashSet<IntPtr>();
            foreach (var h in handles)
                if (!managed.ContainsKey(h))
                {
                    var place = new Placement { Length = Marshal.SizeOf<Placement>() };
                    if (GetWindowPlacement(h, ref place))
                    {
                        GetWindowThreadProcessId(h, out uint pid);
                        managed[h] = new()
                        {
                            Original = place,
                            Pid = pid
                        };
                        added.Add(h);
                    }
                }
            order.Sync(managed.Keys.Where(h => !added.Contains(h)).Concat(handles.Where(added.Contains)));
            var laid = new Dictionary<IntPtr, (string screen, int index, int count, Native.Rect slot)>();
            var immediate = new List<(IntPtr h, Native.Rect wanted, Native.Rect shadow)>();
            bool moving = false;
            foreach (var group in order.Items.Where(handles.Contains).GroupBy(h => Forms.Screen.FromHandle(h).DeviceName))
            {
                var windows = group.ToArray();
                var screen = Forms.Screen.FromHandle(windows[0]);
                if (windows.Any(h => Fullscreen(h, screen)))
                    continue;
                bool fresh = windows.Any(added.Contains);
                if (windows.Any(IsZoomed))
                {
                    if (fresh)
                        foreach (var h in windows.Where(IsZoomed))
                            ShowWindowAsync(h, 9);
                    continue;
                }
                var work = screen.WorkingArea;
                int gap = Math.Max(4, (int)Math.Round(8 * TaskbarNative.Scale(screen)));
                int capacity = Math.Min(16, Math.Max(1, work.Width / 240) * Math.Max(1, work.Height / 140));
                windows = windows.TakeLast(capacity).ToArray();
                var slots = TileLogic.Layout(new()
                {
                    Left = work.Left + gap,
                    Top = work.Top + gap,
                    Right = work.Right - gap,
                    Bottom = work.Bottom - gap
                }, windows.Length, gap, path => Ratio(screen.DeviceName, path));
                for (int i = 0; i < windows.Length && i < slots.Length; i++)
                    laid[windows[i]] = (screen.DeviceName, i, windows.Length, slots[i]);
                for (int i = 0; i < windows.Length; i++)
                {
                    var h = windows[i];
                    var wanted = slots[i];
                    var current = Visible(h);
                    var item = managed[h];
                    if (motions.TryGetValue(h, out var active) && Same(active.To, wanted))
                        continue;
                    if (Same(current, wanted))
                        continue;
                    if (item.Constrained is Native.Rect constrained && Same(constrained, wanted))
                        continue;
                    if (item.Placed && Same(item.Last, wanted) && ((current.Right - current.Left) > (wanted.Right - wanted.Left) + 2
                        || (current.Bottom - current.Top) > (wanted.Bottom - wanted.Top) + 2))
                    {
                        item.Constrained = wanted;
                        item.Last = current;
                        continue;
                    }
                    item.Constrained = null;
                    var shadow = Shadow(h);
                    if (added.Contains(h))
                    {
                        motions.Remove(h);
                        immediate.Add((h, wanted, shadow));
                    }
                    else
                    {
                        motions[h] = new()
                        {
                            From = current,
                            To = wanted,
                            Shadow = shadow,
                            Monitor = MonitorFromWindow(h, 2),
                            Started = Stopwatch.GetTimestamp()
                        };
                        moving = true;
                    }
                }
            }
            tiles = laid;
            Apply(immediate);
            if (moving)
                animation.Start();
        }
        catch (Exception ex) { Store.Log("MosaÃ¯que: " + ex.Message); }
    }
    void Restore()
    {
        order.Clear();
        tiles = new();
        floating.Clear();
        animation.Stop();
        coalesce.Stop();
        motions.Clear();
        failing.Clear();
        foreach (var item in managed)
        {
            GetWindowThreadProcessId(item.Key, out uint pid);
            if (!Native.IsWindow(item.Key) || pid != item.Value.Pid || TaskbarNative.IsIconic(item.Key) || IsZoomed(item.Key))
                continue;
            if (item.Value.Placed && Same(Visible(item.Key), item.Value.Last))
            {
                var placement = item.Value.Original;
                SetWindowPlacement(item.Key, ref placement);
            }
        }
        managed.Clear();
    }
    public void Dispose()
    {
        if (disposed)
            return;
        Unhook();
        timer.Stop();
        Keys(false);
        Restore();
        disposed = true;
    }
}
