// language: C# 12, file: TrayIcons.cs, target: Windows 11 / .NET 8, observation des icônes de zone de notification
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PanneauxGlass;

internal sealed class TrayIcon
{
    public IntPtr Window;
    public uint Id, Callback, Version, Pid;
    public string Tip = "";
    public Guid Guid;
}

// Logique pure : lecture d'un SHELLTRAYDATA (NOTIFYICONDATAW à poignées 32 bits) et messages de menu contextuel.
internal static class TrayProtocol
{
    internal const uint Signature = 0x34753423;
    internal const int Add = 0, Modify = 1, Delete = 2, SetVersion = 4;
    internal const uint FlagMessage = 1, FlagTip = 4, FlagGuid = 0x20;
    internal const uint WmContextMenu = 0x007B, WmRButtonDown = 0x204, WmRButtonUp = 0x205;
    internal const int MinimumSize = 32;
    internal static bool TryParse(ReadOnlySpan<byte> data, out int message, out TrayIcon icon, out uint flags)
    {
        message = 0;
        icon = new TrayIcon();
        flags = 0;
        if (data.Length < MinimumSize || BitConverter.ToUInt32(data[..4]) != Signature)
            return false;
        message = BitConverter.ToInt32(data.Slice(4, 4));
        icon.Window = (IntPtr)BitConverter.ToInt32(data.Slice(12, 4));
        icon.Id = BitConverter.ToUInt32(data.Slice(16, 4));
        flags = BitConverter.ToUInt32(data.Slice(20, 4));
        icon.Callback = BitConverter.ToUInt32(data.Slice(24, 4));
        if (data.Length >= 288)
            icon.Tip = Encoding.Unicode.GetString(data.Slice(32, 256)).Split('\0')[0];
        if (data.Length >= 812)
            icon.Version = BitConverter.ToUInt32(data.Slice(808, 4));
        if (data.Length >= 960 && (flags & FlagGuid) != 0)
            icon.Guid = new Guid(data.Slice(944, 16));
        return icon.Window != IntPtr.Zero;
    }
    internal static bool Apply(Dictionary<(long, uint), TrayIcon> icons, int message, TrayIcon parsed, uint flags)
    {
        var key = (parsed.Window.ToInt64(), parsed.Id);
        switch (message)
        {
            case Add:
                if ((flags & FlagMessage) == 0)
                    parsed.Callback = 0;
                parsed.Version = 0;
                icons[key] = parsed;
                return true;
            case Modify:
                if (!icons.TryGetValue(key, out var known))
                    return false;
                if ((flags & FlagMessage) != 0)
                    known.Callback = parsed.Callback;
                if ((flags & FlagTip) != 0)
                    known.Tip = parsed.Tip;
                return true;
            case Delete:
                return icons.Remove(key);
            case SetVersion:
                if (!icons.TryGetValue(key, out var versioned))
                    return false;
                versioned.Version = parsed.Version;
                return true;
        }
        return false;
    }
    // NOTIFYICON_VERSION_4 : un seul WM_CONTEXTMENU ; sinon la paire de clics droits historique (comme l'explorateur).
    internal static (uint Message, IntPtr WParam, IntPtr LParam)[] ContextMessages(TrayIcon icon, int x, int y)
    {
        if (icon.Version >= 4)
            return new[] { (icon.Callback, (IntPtr)(nint)(((uint)(ushort)x) | ((uint)(ushort)y << 16)),
                (IntPtr)(nint)((icon.Id << 16) | WmContextMenu)) };
        return new[]
        {
            (icon.Callback, (IntPtr)(nint)icon.Id, (IntPtr)(nint)WmRButtonDown),
            (icon.Callback, (IntPtr)(nint)icon.Id, (IntPtr)(nint)WmRButtonUp)
        };
    }
}

// Fenêtre "Shell_TrayWnd" cachée : observe les WM_COPYDATA des icônes et relaie tout, inchangé, à la vraie barre de l'explorateur.
internal sealed class TrayHost : IDisposable
{
    delegate IntPtr WndProcDelegate(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WndClass
    {
        public uint Size, Style;
        public IntPtr Proc;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? Menu, Name;
        public IntPtr SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Msg
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public int X, Y;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct CopyData
    {
        public UIntPtr Kind;
        public uint Size;
        public IntPtr Data;
    }
    const string ClassName = "Shell_TrayWnd";
    const uint WmDestroy = 2, WmClose = 0x10, WmMouseActivate = 0x21, WmWindowPosChanging = 0x46, WmWindowPosChanged = 0x47,
        WmCopyData = 0x4A, WmCommand = 0x111, WmUser = 0x400;
    static readonly WndProcDelegate proc = WindowProc;
    static TrayHost? current;
    [ThreadStatic] static TrayHost? threadHost;
    static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
    volatile bool cancelled;
    internal static TrayHost? Current => current;
    readonly Dictionary<(long, uint), TrayIcon> icons = new();
    readonly object gate = new();
    readonly ManualResetEventSlim started = new(false);
    Thread? thread;
    IntPtr window, explorer;
    bool asserting, disposed;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WndClass data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool UnregisterClass(string name, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(uint exStyle, string name, string title, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetMessage(out Msg message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref Msg message);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref Msg message);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? name, string? title);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout,
        out IntPtr result);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    // Échec = fonctionnalité absente : le bureau et l'explorateur ne sont jamais touchés.
    internal static TrayHost? Start()
    {
        try
        {
            var host = new TrayHost();
            host.thread = new Thread(host.Run) { IsBackground = true, Name = "TrayHost" };
            host.thread.SetApartmentState(ApartmentState.STA);
            host.thread.Start();
            if (!host.started.Wait(3000) || host.window == IntPtr.Zero)
            {
                host.cancelled = true;
                host.Dispose();
                Store.Log("Icônes de notification : fenêtre indisponible.");
                return null;
            }
            return host;
        }
        catch (Exception ex)
        {
            Store.Log("Icônes de notification : " + ex);
            return null;
        }
    }
    void Run()
    {
        IntPtr instance = GetModuleHandle(null);
        bool registered = false;
        // L'instance est publiée avant que la fenêtre existe : aucun WM_COPYDATA ne voit un hôte absent.
        threadHost = this;
        current = this;
        try
        {
            if (cancelled)
                return;
            var data = new WndClass
            {
                Size = (uint)Marshal.SizeOf<WndClass>(),
                Proc = Marshal.GetFunctionPointerForDelegate(proc),
                Instance = instance,
                Name = ClassName
            };
            registered = RegisterClassEx(ref data) != 0;
            if (!registered || cancelled)
                return;
            // Fenêtre jamais affichée : WS_POPUP sans WS_VISIBLE, outil, sans activation, topmost pour passer avant l'explorateur.
            window = CreateWindowEx(0x80 | 0x08000000 | 0x8, ClassName, "", 0x80000000, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero,
                instance, IntPtr.Zero);
            if (window == IntPtr.Zero)
                return;
            Assert();
            if (FindWindow(ClassName, null) != window)
                Store.Log("Icônes de notification : notre fenêtre n'est pas la première Shell_TrayWnd.");
        }
        catch (Exception ex)
        {
            Store.Log("Icônes de notification : " + ex);
        }
        finally
        {
            if (window == IntPtr.Zero || cancelled)
                ClearCurrent();
            started.Set();
        }
        if (window == IntPtr.Zero || cancelled)
        {
            if (window != IntPtr.Zero)
                DestroyWindow(window);
            if (registered)
                UnregisterClass(ClassName, instance);
            return;
        }
        // Les applications déjà lancées réenregistrent leurs icônes ; elles passent par notre fenêtre puis par l'explorateur.
        PostMessage((IntPtr)0xFFFF, RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
        UnregisterClass(ClassName, instance);
    }
    void ClearCurrent()
    {
        if (ReferenceEquals(current, this))
            current = null;
    }
    void Assert()
    {
        if (asserting || window == IntPtr.Zero)
            return;
        asserting = true;
        try
        {
            // HWND_TOPMOST, sans déplacer, redimensionner, activer ni afficher.
            SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x1 | 0x2 | 0x10 | 0x400);
        }
        finally { asserting = false; }
    }
    IntPtr ExplorerWindow()
    {
        if (explorer != IntPtr.Zero && IsWindow(explorer) && TaskbarNative.ClassName(explorer) == ClassName
            && explorer != window && !Native.Owns(explorer))
            return explorer;
        explorer = IntPtr.Zero;
        foreach (var bar in TaskbarNative.ExplorerBars())
            if (bar != window && TaskbarNative.ClassName(bar) == ClassName)
            {
                explorer = bar;
                break;
            }
        return explorer;
    }
    // Deux essais : le handle de l'explorateur est redécouvert après un échec. absent = aucune barre explorateur trouvée.
    bool TryForward(uint message, IntPtr wParam, IntPtr lParam, out IntPtr result, out bool absent)
    {
        result = IntPtr.Zero;
        absent = false;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var target = ExplorerWindow();
                if (target == IntPtr.Zero)
                {
                    absent = true;
                    continue;
                }
                absent = false;
                // SMTO_ABORTIFHUNG : un explorateur bloqué ne fige pas les applications qui notifient.
                if (SendMessageTimeout(target, message, wParam, lParam, 0x2, 1000, out result) != IntPtr.Zero)
                    return true;
            }
            catch (Exception) { /* Réessayé une fois. */ }
            explorer = IntPtr.Zero;
            result = IntPtr.Zero;
        }
        return false;
    }
    bool Parse(IntPtr lParam, out int message, out TrayIcon icon, out uint flags)
    {
        message = 0;
        icon = null!;
        flags = 0;
        try
        {
            var copy = Marshal.PtrToStructure<CopyData>(lParam);
            if (copy.Kind != (UIntPtr)1 || copy.Size < TrayProtocol.MinimumSize || copy.Size > 4096 || copy.Data == IntPtr.Zero)
                return false;
            var bytes = new byte[copy.Size];
            Marshal.Copy(copy.Data, bytes, 0, bytes.Length);
            if (!TrayProtocol.TryParse(bytes, out message, out icon, out flags))
                return false;
            GetWindowThreadProcessId(icon.Window, out uint pid);
            icon.Pid = pid;
            return true;
        }
        catch (Exception ex)
        {
            Store.Log("Icônes de notification : " + ex.Message);
            return false;
        }
    }
    // Seul WM_COPYDATA est relayé ; l'icône n'est enregistrée qu'après un relais réussi (ou sans explorateur).
    IntPtr HandleCopyData(uint message, IntPtr wParam, IntPtr lParam)
    {
        bool parsed = Parse(lParam, out int nim, out var icon, out uint flags);
        bool forwarded = TryForward(message, wParam, lParam, out var result, out bool absent);
        if (parsed && ((forwarded && result != IntPtr.Zero) || absent))
            lock (gate)
                TrayProtocol.Apply(icons, nim, icon, flags);
        if (forwarded)
            return result;
        if (!absent)
            return IntPtr.Zero;
        // Sans explorateur : barre autonome, les messages d'icônes réussissent (comme ManagedShell).
        try { return Marshal.ReadIntPtr(lParam) == (IntPtr)1 ? (IntPtr)1 : IntPtr.Zero; }
        catch (Exception) { return IntPtr.Zero; }
    }
    static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        var host = threadHost ?? current;
        try
        {
            if (message == TaskbarCreated && TaskbarCreated != 0)
            {
                host?.Assert();
                return DefWindowProc(hwnd, message, wParam, lParam);
            }
            switch (message)
            {
                case WmClose:
                    DestroyWindow(hwnd);
                    return IntPtr.Zero;
                case WmDestroy:
                    PostQuitMessage(0);
                    return IntPtr.Zero;
                case WmMouseActivate:
                    return (IntPtr)3;
                case WmWindowPosChanging when lParam != IntPtr.Zero:
                    int flagsAt = IntPtr.Size * 2 + 16;
                    Marshal.WriteInt32(lParam, flagsAt, Marshal.ReadInt32(lParam, flagsAt) & ~0x40);
                    break;
                case WmWindowPosChanged:
                    host?.Assert();
                    break;
                case WmCopyData when host != null:
                    return host.HandleCopyData(message, wParam, lParam);
            }
        }
        catch (Exception) { /* La procédure de fenêtre ne doit jamais lever. */ }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }
    // Premier menu d'icône vivant d'un programme ; faux si aucune icône n'est connue.
    internal bool ShowMenu(string path, int x, int y)
    {
        TrayIcon[] snapshot;
        lock (gate)
            snapshot = icons.Values.ToArray();
        foreach (var icon in snapshot)
        {
            if (icon.Callback == 0 || !IsWindow(icon.Window))
                continue;
            if (!string.Equals(TaskbarNative.ProcessImagePath(icon.Pid), path, StringComparison.OrdinalIgnoreCase))
                continue;
            AllowSetForegroundWindow(icon.Pid);
            TaskbarNative.SetForegroundWindow(icon.Window);
            foreach (var m in TrayProtocol.ContextMessages(icon, x, y))
                PostMessage(icon.Window, m.Message, m.WParam, m.LParam);
            return true;
        }
        return false;
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        try
        {
            if (window != IntPtr.Zero)
                PostMessage(window, WmClose, IntPtr.Zero, IntPtr.Zero);
            thread?.Join(2000);
        }
        catch (Exception) { /* Arrêt best effort : le thread est en arrière-plan. */ }
        ClearCurrent();
    }
}
