// Quels moniteurs affichent actuellement une fenêtre de premier plan en plein écran (YouTube en plein écran, lecteur vidéo, jeu sans
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PanneauxGlass;

internal static partial class FullscreenWatcher
{
    // Public pour le test de sonde. La fenêtre active (premier plan) décide de son propre moniteur : plein écran seulement si elle le
    // remplit. Autres moniteurs : la fenêtre éligible la plus haute (EnumWindows suit l'ordre Z) couvrant au moins la moitié du
    // moniteur décide.
    internal static string[] Compute()
    {
        // instantané unique : le délégué ne doit pas s'exécuter par fenêtre pendant que computeGate est tenu
        uint[]? ignored = null;
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
                fg = root;   // un menu/popup compte comme son propriétaire
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
            // Les petites fenêtres flottantes (bulle « Échap » du plein écran, infobulles, PiP, assistants 1x1, surcouches de
            // jeu/discussion) ne doivent pas décider du moniteur : seule une fenêtre qui en couvre au moins la moitié le fait.
            if (!CoversHalf(h, info.rcMonitor))
                return true;
            decided[mon] = IsFullscreen(h, info.rcMonitor) ? info.szDevice : null;
            return true;
        }, IntPtr.Zero);
        var result = decided.Values.Where(v => v != null).Select(v => v!).ToList();
        fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && SHQueryUserNotificationState(out int state) == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN && Eligible(fg,
            ignored))
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

    // Les surcouches transparentes aux clics (superposées + transparentes) ne comptent jamais, même quand elles couvrent l'écran.
    static bool IsOverlay(IntPtr hwnd)
    {
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        // p. ex. Cua.AgentCursorOverlay (superposée+transparente), NVIDIA CEF-OSC-WIDGET (superposée+sans activation) : de la taille de
        // l'écran, jamais une application.
        return (ex & WS_EX_LAYERED) != 0 && (ex & (WS_EX_TRANSPARENT | WS_EX_NOACTIVATE)) != 0;
    }

    static bool CoversHalf(IntPtr hwnd, RECT m)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(hwnd,
            out r))
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
        // Fenêtre maximisée sans cadre (p. ex. Electron) à côté d'une barre des tâches masquée automatiquement : zoomée, sauf si
        // c'est un vrai WS_POPUP (jeu sans bordure).
        if (IsZoomed(hwnd) && !((style & WS_POPUP) != 0 && (style & WS_THICKFRAME) == 0))
            return false;
        return true;
    }

    // Le plein écran de Chromium laisse la dernière ligne libre (3440x1439 sur un moniteur 3440x1440) : tolérer quelques pixels.
    static bool Covers(RECT r, RECT m) => QuietPolicy.Covers(r.Left, r.Top, r.Right, r.Bottom, m.Left, m.Top, m.Right, m.Bottom);

    const uint EVENT_SYSTEM_FOREGROUND = 3, EVENT_SYSTEM_MINIMIZESTART = 0x16, PROCESS_QUERY_LIMITED_INFORMATION = 0x1000,
        EVENT_OBJECT_LOCATIONCHANGE = 0x800B, WINEVENT_OUTOFCONTEXT = 0;
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
        public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
    }

    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid,
        uint thread,
        uint flags);
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, uint flags,
        StringBuilder s, ref int n);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
}
