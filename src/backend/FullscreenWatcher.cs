// Which monitors currently show a fullscreen foreground window (YouTube fullscreen, video player, borderless game): the shell chrome must hide there.
// Event-driven: SetWinEventHook (WINEVENT_OUTOFCONTEXT) for EVENT_SYSTEM_FOREGROUND, MINIMIZESTART and EVENT_OBJECT_LOCATIONCHANGE (foreground process) arms a one-shot 40 ms debounce timer (re-arming = debounce)
// plus a settle chain of re-checks (see SettleDelays); while a monitor is fullscreen a 1 s safety-net timer also re-checks. The recompute runs on a thread-pool thread.
// OUTOFCONTEXT hooks deliver through the message loop of the thread that installed them: they live on a dedicated hook thread blocked in GetMessage; Start() may be called from any thread.
// Fullscreen = foreground, visible, non-iconic window (not ours, not shell windows) whose DWM frame or window rect covers its whole monitor and which has no caption (a maximized window keeps its caption).
// Optionally QUNS_RUNNING_D3D_FULL_SCREEN (exclusive D3D) is mapped to the foreground window's monitor. Current = monitor device names (\\.\DISPLAY1), raised through Changed only when the set changes.
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PanneauxGlass;

internal static class FullscreenWatcher
{
    static readonly object gate = new();
    static readonly object computeGate = new();
    static Timer? debounce, settle, watch;
    // Browsers enter fullscreen seconds after the events that announced it (observed up to ~5 s with Edge, no event at the end): settle re-checks at +1.5 s, +3 s, +6 s after the last event.
    static readonly int[] SettleDelays = { 300, 600, 1000, 1500, 3000, 6000 };
    const int SafetyNetMs = 1000;   // only armed while a monitor is fullscreen; events (foreground, minimize, location) drive the normal exit
    static int settleStep;
    static bool started;
    // Hook handles belong to the hook thread that created them (thread-static: an old thread's tail never touches a newer run's handles).
    [ThreadStatic] static IntPtr hookForeground, hookLocation, hookMinimize;
    [ThreadStatic] static int myGen;   // generation of the run this hook thread belongs to
    static int gen;                    // incremented by Start, guarded by gate
    static WinEventProc? proc;   // kept alive for the lifetime of the hooks
    static volatile string[] current = Array.Empty<string>();
    internal static string[] Current => current;
    // Raised (thread-pool thread, outside any lock) only when the set changes; subscribers must not throw (exceptions are swallowed).
    internal static event Action? Changed;
    // Extra process ids to ignore (frontend / backend helpers); this process is always ignored.
    internal static Func<uint[]>? IgnoredPids;
    // Test hook: also consider windows of this process.
    internal static bool IncludeOwnProcessForTest;

    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    // Out-of-context WinEvent callbacks are delivered only while the hooking thread pumps messages, so the hooks live on a
    // dedicated thread blocked in GetMessage (no CPU when idle). Hooking from a non-pumping thread delayed events by seconds.
    static Thread? hookThread;
    static uint hookThreadId;

    internal static void Start()
    {
        var ready = new ManualResetEventSlim(false);
        lock (gate)
        {
            if (started)
                return;
            started = true;
            debounce = new Timer(_ => Recompute(), null, Timeout.Infinite, Timeout.Infinite);
            settle = new Timer(_ => Settle(), null, Timeout.Infinite, Timeout.Infinite);
            watch = new Timer(_ => Recompute(), null, Timeout.Infinite, Timeout.Infinite);
            proc = OnEvent;
            int g = ++gen;
            hookThread = new Thread(() => HookLoop(ready, g)) { IsBackground = true, Name = "FullscreenWatcher" };
            hookThread.Start();
        }
        ready.Wait(2000);
        Task.Run(Recompute);
    }

    static void HookLoop(ManualResetEventSlim ready, int g)
    {
        PeekMessageW(out MSG m, IntPtr.Zero, 0, 0, 0);   // creates this thread's message queue before Stop can post to it
        myGen = g;
        lock (gate)
        {
            if (!started || proc == null || gen != g)
            {
                ready.Set();
                return;
            }
            hookThreadId = GetCurrentThreadId();
            hookForeground = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT);
            hookMinimize = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZESTART, IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT);
            RehookLocation(GetForegroundWindow());
        }
        ready.Set();
        while (GetMessageW(out m, IntPtr.Zero, 0, 0) > 0)
        {
            if (m.hwnd == IntPtr.Zero && m.message == WM_APP_REHOOK)
            {
                lock (gate)
                {
                    if (started && gen == g)
                        RehookLocation(GetForegroundWindow());
                }
                continue;
            }
            TranslateMessage(ref m);
            DispatchMessageW(ref m);
        }
        // Always unhook our own handles; only touch the shared statics if no newer run has taken over.
        if (hookForeground != IntPtr.Zero)
            UnhookWinEvent(hookForeground);
        if (hookMinimize != IntPtr.Zero)
            UnhookWinEvent(hookMinimize);
        if (hookLocation != IntPtr.Zero)
            UnhookWinEvent(hookLocation);
        hookForeground = hookMinimize = hookLocation = IntPtr.Zero;
        lock (gate)
        {
            if (gen == g)
            {
                hookThreadId = 0;
                hookedPid = 0;
            }
        }
    }

    internal static void Stop()
    {
        Thread? t;
        lock (gate)
        {
            if (!started)
                return;
            started = false;
            if (hookThreadId != 0)
                PostThreadMessageW(hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            t = hookThread;
            hookThread = null;
            debounce?.Dispose();
            debounce = null;
            settle?.Dispose();
            settle = null;
            watch?.Dispose();
            watch = null;
        }
        bool joined = t?.Join(2000) ?? true;   // the hook thread unhooks itself (UnhookWinEvent belongs to the installing thread)
        lock (gate)
        {
            if (!started && joined)
                proc = null;
        }   // on timeout the hooks may still be alive: keep the delegate rooted
        lock (computeGate)
        {
            current = Array.Empty<string>();
        }   // waits for an in-flight Recompute; SetSet re-checks `started`. No Changed: subscribers are being torn down.
    }

    // LOCATIONCHANGE is scoped to the foreground window's process so mouse-cursor moves elsewhere never wake us.
    // Hook thread only, caller holds gate. `target` is the window the FOREGROUND event named: GetForegroundWindow() can
    // still return the previous window when the event is processed (seen with a new Edge window), which left the hook on the wrong process.
    static uint hookedPid;
    const uint WM_APP_REHOOK = 0x8000 + 0x46;
    static void RehookLocation(IntPtr target)
    {
        if (target == IntPtr.Zero || proc == null || myGen != gen)
            return;
        GetWindowThreadProcessId(GetAncestor(target, GA_ROOTOWNER) is var r && r != IntPtr.Zero ? r : target, out uint pid);
        if (pid == 0 || (pid == hookedPid && hookLocation != IntPtr.Zero))
            return;
        if (hookLocation != IntPtr.Zero)
        {
            UnhookWinEvent(hookLocation);
            hookLocation = IntPtr.Zero;
        }
        hookedPid = pid;
        hookLocation = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, proc, pid, 0, WINEVENT_OUTOFCONTEXT);   // whole process: browsers resize from another thread
    }

    static void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (evt == EVENT_OBJECT_LOCATIONCHANGE && (idObject != OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero || GetAncestor(hwnd, GA_ROOT) != hwnd))
            return;
        lock (gate)
        {
            if (!started)
                return;
            if (evt == EVENT_SYSTEM_FOREGROUND)
                RehookLocation(hwnd);
            debounce?.Change(40, Timeout.Infinite);
            settleStep = 0;
            settle?.Change(SettleDelays[0], Timeout.Infinite);
        }
    }

    static void Settle()
    {
        Recompute();
        lock (gate)
        {
            if (started && settle != null && ++settleStep < SettleDelays.Length)
                settle.Change(SettleDelays[settleStep] - SettleDelays[settleStep - 1], Timeout.Infinite);
        }
    }

    static void Recompute()
    {
        lock (gate)
        {
            if (!started)
                return;
        }
        bool changed = false;
        lock (computeGate)
        {   // serialized: an older computation can never overwrite a newer one
            try
            {
                changed = SetSet(Compute());
            }
            catch { /* keep the previous value */ }
        }
        // While a monitor is fullscreen, a slow safety net (SafetyNetMs) catches an exit no event announced, e.g. a crash (no cost otherwise).
        lock (gate)
        {
            if (!started)
                return;
            watch?.Change(current.Length > 0 ? SafetyNetMs : Timeout.Infinite, Timeout.Infinite);
            // Self-heal: if the location hook is not on the active window's process, ask the hook thread to move it.
            if (hookThreadId != 0 && ForegroundRootPid() is var fpid && fpid != 0 && fpid != hookedPid)
                PostThreadMessageW(hookThreadId, WM_APP_REHOOK, IntPtr.Zero, IntPtr.Zero);
        }
        if (changed)
        {
            try
            {
                Changed?.Invoke();
            }
            catch (Exception)
            {
                // Un abonné défaillant ne doit pas arrêter la veille plein écran.
            }
        }
    }

    static uint ForegroundRootPid()
    {
        var f = GetForegroundWindow();
        if (f == IntPtr.Zero)
            return 0;
        var r = GetAncestor(f, GA_ROOTOWNER);
        GetWindowThreadProcessId(r != IntPtr.Zero ? r : f, out uint pid);
        return pid;
    }

    // Stores a new set; true when it differs (order-insensitive). Refuses once stopped.
    static bool SetSet(string[] names)
    {
        lock (gate)
        {
            if (!started)
                return false;
        }
        var sorted = names.Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        if (sorted.SequenceEqual(current))
            return false;
        current = sorted;
        return true;
    }
    internal static void RaiseChangedForTest(string[] names)
    {
        if (SetSet(names))
        {
            try
            {
                Changed?.Invoke();
            }
            catch (Exception)
            {
                // Un abonné défaillant ne doit pas arrêter la veille plein écran.
            }
        }
    }

    // Public for the probe test. The active (foreground) window decides its own monitor: fullscreen only if it fills it.
    // Other monitors: the topmost eligible window (EnumWindows is z-ordered) covering at least half of the monitor decides.
    internal static string[] Compute()
    {
        uint[]? ignored = null;   // snapshot once: the delegate must not run per window while computeGate is held
        try
        {
            ignored = IgnoredPids?.Invoke();
        }
        catch (Exception)
        {
            // Fournisseur de PID à ignorer défaillant : aucun PID ignoré à ce tour.
        }
        var decided = new System.Collections.Generic.Dictionary<IntPtr, string?>();
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero)
        {
            var root = GetAncestor(fg, GA_ROOTOWNER);
            if (root != IntPtr.Zero)
                fg = root;   // a menu/popup counts as its owner
            if (Eligible(fg, ignored) && !IsOverlay(fg))
            {
                var mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (mon != IntPtr.Zero && GetMonitorInfoW(mon, ref info))
                    decided[mon] = IsFullscreen(fg, info.rcMonitor) ? info.szDevice : null;
            }
        }
        EnumWindows((h, _) =>
        {
            if (!Eligible(h, ignored) || IsOverlay(h))
                return true;
            var mon = MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero || decided.ContainsKey(mon))
                return true;
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfoW(mon, ref info))
                return true;
            // Small windows floating above (fullscreen "press Esc" bubble, tooltips, PiP, 1x1 helpers, game/chat overlays)
            // must not decide the monitor: only a window covering at least half of it does.
            if (!CoversHalf(h, info.rcMonitor))
                return true;
            decided[mon] = IsFullscreen(h, info.rcMonitor) ? info.szDevice : null;
            return true;
        }, IntPtr.Zero);
        var result = decided.Values.Where(v => v != null).Select(v => v!).ToList();
        fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && SHQueryUserNotificationState(out int state) == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN && Eligible(fg, ignored))
        {
            var mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (mon != IntPtr.Zero && GetMonitorInfoW(mon, ref info))
                result.Add(info.szDevice);
        }
        return result.ToArray();
    }

    static bool Eligible(IntPtr hwnd, uint[]? ignored)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
            return false;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (!IncludeOwnProcessForTest && pid == (uint)Environment.ProcessId)
            return false;
        if (ignored != null && ignored.Contains(pid))
            return false;
        var sb = new StringBuilder(128);
        GetClassName(hwnd, sb, 128);
        var cls = sb.ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow"
            or "MultitaskingViewFrame" or "XamlExplorerHostIslandWindow" or "ForegroundStaging" or "TaskSwitcherWnd")
            return false;
        if (cls.Contains("LockScreen", StringComparison.OrdinalIgnoreCase) || cls.Contains("LockApp", StringComparison.OrdinalIgnoreCase))
            return false;
        if (cls != "CabinetWClass" && IsExplorer(pid))
            return false;
        return true;
    }

    static readonly System.Collections.Generic.Dictionary<uint, bool> explorerCache = new();
    static bool IsExplorer(uint pid)
    {
        lock (explorerCache)
        {
            if (explorerCache.TryGetValue(pid, out bool v))
                return v;
            bool r = false;
            var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h != IntPtr.Zero)
            {
                try
                {
                    var b = new StringBuilder(520);
                    int n = b.Capacity;
                    if (QueryFullProcessImageName(h, 0, b, ref n))
                        r = string.Equals(System.IO.Path.GetFileName(b.ToString()), "explorer.exe", StringComparison.OrdinalIgnoreCase);
                }
                finally { CloseHandle(h); }
            }
            if (explorerCache.Count > 256)
                explorerCache.Clear();
            explorerCache[pid] = r;
            return r;
        }
    }

    // Click-through overlays (layered + transparent) never count, even when they span the screen.
    static bool IsOverlay(IntPtr hwnd)
    {
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        // e.g. Cua.AgentCursorOverlay (layered+transparent), NVIDIA CEF-OSC-WIDGET (layered+noactivate): screen-sized, never an app.
        return (ex & WS_EX_LAYERED) != 0 && (ex & (WS_EX_TRANSPARENT | WS_EX_NOACTIVATE)) != 0;
    }

    static bool CoversHalf(IntPtr hwnd, RECT m)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(hwnd, out r))
            return false;
        long w = Math.Min(r.Right, m.Right) - Math.Max(r.Left, m.Left), h = Math.Min(r.Bottom, m.Bottom) - Math.Max(r.Top, m.Top);
        if (w <= 0 || h <= 0)
            return false;
        long area = (long)(m.Right - m.Left) * (m.Bottom - m.Top);
        return area > 0 && w * h * 2 >= area;
    }

    static bool IsFullscreen(IntPtr hwnd, RECT m)
    {
        bool full;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT d, Marshal.SizeOf<RECT>()) == 0)
            full = Covers(d, m);   // DWM bounds are authoritative: a maximized window's outer rect overhangs
        else
            full = GetWindowRect(hwnd, out RECT r) && Covers(r, m);
        if (!full)
            return false;
        uint style = unchecked((uint)GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64());
        if ((style & WS_CAPTION) == WS_CAPTION)
            return false;   // maximized, not fullscreen
        // Frameless maximized window (e.g. Electron) next to an auto-hidden taskbar: zoomed, unless a pure WS_POPUP (borderless game).
        if (IsZoomed(hwnd) && !((style & WS_POPUP) != 0 && (style & WS_THICKFRAME) == 0))
            return false;
        return true;
    }

    // Chromium fullscreen leaves the bottom row free (3440x1439 on a 3440x1440 monitor): tolerate a few px.
    static bool Covers(RECT r, RECT m) => QuietPolicy.Covers(r.Left, r.Top, r.Right, r.Bottom, m.Left, m.Top, m.Right, m.Bottom);

    const uint EVENT_SYSTEM_FOREGROUND = 3, EVENT_SYSTEM_MINIMIZESTART = 0x16, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, EVENT_OBJECT_LOCATIONCHANGE = 0x800B, WINEVENT_OUTOFCONTEXT = 0;
    const int OBJID_WINDOW = 0, GWL_STYLE = -16, DWMWA_CLOAKED = 14, DWMWA_EXTENDED_FRAME_BOUNDS = 9, QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    const uint WS_CAPTION = 0xC00000, WS_POPUP = 0x80000000, WS_THICKFRAME = 0x40000;
    const long WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x8000000;
    const int GWL_EXSTYLE = -20;
    const uint GA_ROOT = 2, GA_ROOTOWNER = 3;
    const uint WM_QUIT = 0x12;
    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY, lPrivate;
    }
    [DllImport("user32.dll")] static extern int GetMessageW(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PeekMessageW(out MSG m, IntPtr h, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")] static extern bool PostThreadMessageW(uint tid, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfoW(IntPtr mon, ref MONITORINFOEX info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder s, ref int n);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
}
