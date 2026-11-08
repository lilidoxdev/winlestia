// language: C# 12, file: Native.cs, target: Windows 11 / .NET 8, verre natif et icônes du shell
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;

internal static partial class Native
{
    internal static bool Layered;
    [DllImport("GlassBackdrop.dll")] static extern int BackdropAttach(IntPtr content);
    [DllImport("GlassBackdrop.dll")] static extern int BackdropUpdate(IntPtr content, double radius, bool visible, bool topmost);
    [DllImport("GlassBackdrop.dll")] internal static extern void BackdropDetach(IntPtr content);
    internal static void SyncBackdrop(Window window, int radius)
    {
        if (!window.AllowsTransparency || Theme.Current.Opaque)
            return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
        {
            int hr = BackdropUpdate(hwnd, Math.Min(radius, Math.Min(window.ActualWidth, window.ActualHeight) / 2), window.IsVisible,
                window.Topmost);
            if (hr < 0)
                Store.Log("Backdrop update: 0x" + hr.ToString("X"));
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    struct BlurRegion
    {
        public uint Flags; public int Enabled; public IntPtr Region; public int Transition;
    }
    [DllImport("dwmapi.dll")] static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref BlurRegion blur);
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X, Y;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Margins
    {
        public int Left, Right, Top, Bottom;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Accent
    {
        public int State, Flags, Gradient, Animation;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct CompositionData
    {
        public int Attribute; public IntPtr Data; public UIntPtr Size;
    }
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    internal delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookExW(int id, LowLevelMouseProc procedure,
        IntPtr module, uint thread);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll")] internal static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int length);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hwnd, StringBuilder name, int length);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
    internal static bool Owns(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }
    internal static string Title(IntPtr hwnd)
    {
        var text = new StringBuilder(256);
        GetWindowTextW(hwnd, text, 256);
        return text.ToString();
    }
    // Utilisé par la touche globale Toujours au premier plan : une fenêtre étrangère voit seulement son style étendu basculé.
    internal static bool ToggleTopmost(IntPtr hwnd, out bool topmost)
    {
        topmost = false;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
            return false;
        long style = GetWindowLongPtrW(hwnd, -20).ToInt64();
        topmost = (style & 0x8) == 0;
        return SetWindowPos(hwnd, topmost ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }
    [DllImport("user32.dll")] static extern bool SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers,
        uint key);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    internal static void Raise(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;
        SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        window.Activate();
        SetForegroundWindow(handle);
    }
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    internal static void ClearRegion(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
            SetWindowRgn(handle, IntPtr.Zero, true);
    }
    [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
    [DllImport("gdi32.dll")] static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int width, int height);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);
    // WS_EX_TRANSPARENT fait de la feuille une image et non une cible : chaque clic atteint les panneaux dessous.
    internal static long ExtendedStyle(IntPtr hwnd) => hwnd == IntPtr.Zero ? 0 : GetWindowLongPtrW(hwnd, -20).ToInt64();
    internal static void ClickThrough(IntPtr hwnd, bool through)
    {
        if (hwnd == IntPtr.Zero)
            return;
        long style = GetWindowLongPtrW(hwnd, -20).ToInt64();
        long next = through ? (style | 0x20 | 0x08000000) : (style & ~(0x20L | 0x08000000L));
        if (next != style)
            SetWindowLongPtrW(hwnd, -20, new IntPtr(next));
    }
    internal static void PlaceBelow(IntPtr hwnd, IntPtr anchor)
    {
        if (hwnd == IntPtr.Zero || anchor == IntPtr.Zero)
            return;
        SetWindowPos(hwnd, anchor, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }
    internal static void KeepAbove(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out Rect value, int size);
    internal static object Describe(Window window)
    {
        var h = new WindowInteropHelper(window).Handle;
        GetWindowRect(h, out var r);
        DwmGetWindowAttribute(h, 9, out var frame, 16);
        var region = CreateRoundRectRgn(0, 0, 1, 1, 0, 0);
        int kind = GetWindowRgn(h, region);
        bool corner = PtInRegion(region, 1, 1);
        DeleteObject(region);
        return new
        {
            window.Title,
            Hwnd = h.ToInt64(),
            Visible = IsWindowVisible(h),
            WpfVisible = window.IsVisible,
            window.Left,
            window.Top,
            window.Width,
            window.Height,
            window.ActualWidth,
            window.ActualHeight,
            window.Opacity,
            RegionKind = kind,
            CornerIncluded = corner,
            Style = GetWindowLongPtrW(h, -16).ToInt64(),
            ExStyle = GetWindowLongPtrW(h, -20).ToInt64(),
            Rect = new[] { r.Left, r.Top, r.Right, r.Bottom },
            DwmFrame = new[] { frame.Left, frame.Top, frame.Right, frame.Bottom }
        };
    }
    internal static void Glass(Window window, double tint, bool light, int radius = 32)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget != null)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
        var margins = window.AllowsTransparency || Theme.Current.Opaque ? new Margins() : new Margins { Left = -1, Right = -1, Top = -1,
            Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int dark = light ? 0 : 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
        // Une seule surface acrylique, découpée par notre région. Sans cela, le cadre/fond arrondi séparé de DWM
        // laisse derrière elle un second rectangle moins arrondi.
        int corner = 1;
        DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
        int border = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(hwnd, 34, ref border, 4);
        int backdrop = 1;
        DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4);
        // L'acrylique d'accent garde les quatre surfaces indépendantes translucides quand le focus passe de l'une à l'autre.
        var policy = new Accent { State = window.AllowsTransparency || Theme.Current.Opaque ? 0 : 4, Flags = window.AllowsTransparency
            || Theme.Current.Opaque ? 0 : 2, Gradient = unchecked((int)(((uint)(tint * 255) << 24) | (light ? 0x00F8F3ECu
            : 0x003C2418u))) };
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<Accent>());
        try
        {
            Marshal.StructureToPtr(policy, memory, false);
            var data = new CompositionData { Attribute = 19, Data = memory, Size = (UIntPtr)Marshal.SizeOf<Accent>() };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(memory); }
        if (window.AllowsTransparency)
        {
            var blur = new BlurRegion { Flags = 1, Enabled = 0 };
            DwmEnableBlurBehindWindow(hwnd, ref blur);
            if (Theme.Current.Opaque)
            {
                BackdropDetach(hwnd);
            }
            else if (window is not GlassWindow glass || glass.MaterialVisible)
            {
                int hr = BackdropAttach(hwnd);
                if (hr < 0)
                    Store.Log("Backdrop attach: 0x" + hr.ToString("X"));
            }
        }
        Round(window, radius);
        SyncBackdrop(window, radius);
    }
    internal static void Round(Window w, int radius)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero || w.ActualWidth < 1 || w.ActualHeight < 1)
            return;
        double scale = GetDpiForWindow(hwnd) / 96d;
        IntPtr region = CreateRoundRectRgn(0, 0, (int)Math.Ceiling(w.ActualWidth * scale) + 1, (int)Math.Ceiling(w.ActualHeight * scale)
            + 1, (int)(radius * 2 * scale), (int)(radius * 2 * scale));
        if (SetWindowRgn(hwnd, region, true) == 0)
            DeleteObject(region);
        SyncBackdrop(w, radius);
    }
    internal static bool Fullscreen(Forms.Screen screen, IEnumerable<Window> own)
    {
        var hwnd = GetForegroundWindow();
        foreach (var window in own)
            if (new WindowInteropHelper(window).Handle == hwnd)
                return false;
        var cls = new StringBuilder(128);
        GetClassName(hwnd, cls, 128);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd")
            return false;
        if (!GetWindowRect(hwnd, out var rect))
            return false;
        var b = screen.Bounds;
        return rect.Left <= b.Left && rect.Top <= b.Top && rect.Right >= b.Right && rect.Bottom >= b.Bottom;
    }
    static bool RunPresent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return Convert.ToString(key?.GetValue("Panneaux", ""))?.Contains("Glass-v5", StringComparison.OrdinalIgnoreCase) == true;
    }
    static void DropRun()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        key.DeleteValue("Panneaux", false);
    }
    static readonly object autostartGate = new(); static int autostartDesired = -1; // -1 : aucune demande en cours
    // Pendant une demande, l'état visible est celui demandé ; sinon Run ou tâche (l'état de la tâche est attendu au plus 3 s
    // au premier appel).
    internal static bool Autostart
    {
        get
        {
            int want = System.Threading.Volatile.Read(ref autostartDesired);
            return want >= 0 ? want == 1 : RunPresent() || AutostartTask.Present;
        }
    }
    // Au lancement, hors du thread d'interface : une valeur Run (installeur, ancienne version) devient la tâche de session. Ne réactive
    // jamais un démarrage désactivé : sans valeur Run, rien n'est créé, et le verrou sérialise avec SetAutostart.
    internal static void MigrateAutostart()
    {
        System.Threading.Tasks.Task.Run(() => { lock (autostartGate) { try { AutostartTask.Refresh(); if (AutostartPolicy.Plan(RunPresent(
            ), Environment.ProcessPath ?? "") == AutostartPolicy.Step.CreateTaskThenDropRun) { if (AutostartPolicy.DropRunAfter(
            AutostartTask.Create())) { DropRun(); Store.Log(
            "Démarrage migré de la clé Run vers la tâche WinlestiaShell."); } else Store.Log(
            "Démarrage: clé Run conservée (tâche indisponible)."); } } catch (Exception ex) { Store.Log("Migration du démarrage: "
            + ex.Message); } } });
    }
}
