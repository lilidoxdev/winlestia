// language: C# 12, file: TaskbarNative.cs, target: Windows 11 / .NET 8, reversible shell appbar
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;

internal static class TaskbarNative
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct BarData
    {
        public uint Size; public IntPtr Window; public uint Callback, Edge; public Native.Rect Rect; public IntPtr Param;
    }
    internal sealed class Lease
    {
        public int State
        {
            get; set;
        }
        public List<long> Visible { get; set; } = new(); public string Ready { get; set; } = ""; public string Release { get; set; } = ""; public long ParentStart
        {
            get; set;
        }
    }
    internal sealed record AppWindow(IntPtr Handle, string Title, string Path)
    {
        internal string RealPath => CatalogueWatchRules.IsPseudoPath(Path) ? "" : Path;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder buffer, ref uint size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    // PROCESS_QUERY_LIMITED_INFORMATION also opens most elevated processes, which Process.MainModule refuses.
    internal static string ProcessImagePath(uint pid)
    {
        IntPtr process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero)
            return "";
        try
        {
            var buffer = new StringBuilder(1024);
            uint size = (uint)buffer.Capacity;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString(0, (int)size) : "";
        }
        catch { return ""; }
        finally { CloseHandle(process); }
    }
    // Packaged (MSIX/UWP) windows: the AppUserModelID of the window gives the same "shell:AppsFolder\<id>" key as the launcher catalogue, whatever process hosts the window.
    [StructLayout(LayoutKind.Sequential, Size = 24)]
    struct PropVariant
    {
        public ushort Type, R1, R2, R3; public IntPtr A, B;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PropKey
    {
        public Guid Format; public uint Id;
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count); [PreserveSig] int GetAt(uint index, out PropKey key); [PreserveSig] int GetValue(ref PropKey key, out PropVariant value); [PreserveSig] int SetValue(ref PropKey key, ref PropVariant value); [PreserveSig] int Commit();
    }
    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? store);
    [DllImport("propsys.dll", CharSet = CharSet.Unicode)] static extern int PropVariantToStringAlloc(ref PropVariant value, out IntPtr text);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);
    internal static string AppUserModelId(IntPtr window)
    {
        IPropertyStore? store = null;
        try
        {
            Guid iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(window, ref iid, out store) != 0 || store == null)
                return "";
            var key = new PropKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 5 };
            if (store.GetValue(ref key, out var value) != 0)
                return "";
            try
            {
                if (value.Type != 31)
                    return "";
                if (PropVariantToStringAlloc(ref value, out IntPtr text) != 0 || text == IntPtr.Zero)
                    return "";
                try
                {
                    return Marshal.PtrToStringUni(text) ?? "";
                }
                finally { Marshal.FreeCoTaskMem(text); }
            }
            finally { PropVariantClear(ref value); }
        }
        catch { return ""; }
        finally { if (store != null) Marshal.ReleaseComObject(store); }
    }
    // Packaged = AppsFolder id with a package family ("Name_hash!App"); plain desktop ids (a path or a bare name) keep the process path.
    static bool NeedsPackageKey(string path) => path.Length == 0 || path.EndsWith(@"\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
    internal delegate bool EnumProc(IntPtr window, IntPtr data);
    internal delegate void WinEvent(IntPtr hook, uint kind, IntPtr window, int obj, int child, uint thread, uint time);
    [DllImport("shell32.dll")] internal static extern UIntPtr SHAppBarMessage(uint message, ref BarData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string name, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Native.Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out int value, int size);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEvent callback, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Native.Point point, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    internal static BarData Data(IntPtr window = default) => new() { Size = (uint)Marshal.SizeOf<BarData>(), Window = window };
    internal static string LeasePath => Path.Combine(Store.Root, "taskbar-session.json");
    static readonly Dictionary<string, string> linkTargets = new(StringComparer.OrdinalIgnoreCase);
    internal static string LaunchPath(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return path;
        if (linkTargets.TryGetValue(path, out var cached))
            return cached;
        string target = path;
        object? shell = null, link = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type != null)
            {
                shell = Activator.CreateInstance(type);
                if (shell != null)
                {
                    link = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
                    if (link != null)
                        target = Convert.ToString(link.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, link, null)) ?? path;
                }
            }
        }
        catch (Exception ex)
        {
            Store.Log("TaskbarNative.LaunchPath : " + ex.GetType().Name + ": " + ex.Message);
        }
        finally { if (link != null && Marshal.IsComObject(link)) Marshal.ReleaseComObject(link); if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell); }
        linkTargets[path] = target;
        return target;
    }
    internal static string ClassName(IntPtr window)
    {
        var text = new StringBuilder(128);
        GetClassName(window, text, 128);
        return text.ToString();
    }
    internal static List<IntPtr> ExplorerBars()
    {
        var result = new List<IntPtr>();
        EnumWindows((h, _) => { if (ClassName(h) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") result.Add(h); return true; }, IntPtr.Zero);
        return result;
    }
    internal static double Scale(Forms.Screen screen)
    {
        try
        {
            var p = new Native.Point { X = screen.Bounds.Left + 10, Y = screen.Bounds.Top + 10 };
            if (GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out var x, out _) == 0)
                return x / 96d;
        }
        catch (Exception)
        {
            // Repli sur le DPI par défaut si le moniteur est introuvable.
        }
        return 1;
    }
    internal static Lease Snapshot()
    {
        var data = Data(FindWindow("Shell_TrayWnd", null));
        return new()
        {
            State = (int)SHAppBarMessage(4, ref data).ToUInt64(),
            Visible = ExplorerBars().Where(IsWindowVisible).Select(h => h.ToInt64()).ToList(),
            ParentStart = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks
        };
    }
    internal static void HideExplorer()
    {
        var data = Data(FindWindow("Shell_TrayWnd", null));
        var state = (int)SHAppBarMessage(4, ref data).ToUInt64();
        data.Param = new IntPtr(state | 1);
        SHAppBarMessage(10, ref data);
        foreach (var bar in ExplorerBars())
            ShowWindow(bar, 0);
    }
    internal static bool ExplorerReady() => FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;
    internal static void EnsureHidden()
    {
        foreach (var bar in ExplorerBars())
            if (IsWindowVisible(bar))
                ShowWindow(bar, 0);
    }
    internal static void Restore(Lease? lease)
    {
        if (lease == null)
            return;
        var data = Data(FindWindow("Shell_TrayWnd", null));
        data.Param = new IntPtr(lease.State);
        SHAppBarMessage(10, ref data);
        foreach (var bar in ExplorerBars())
            ShowWindow(bar, 5);
    }
    internal static Lease? ReadLease(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Lease>(File.ReadAllText(path), Store.Json);
        }
        catch { return null; }
    }
    internal static void RestoreSaved()
    {
        // Fichier de session absent ou illisible : même repli que RETABLIR_BARRE_WINDOWS.exe (toujours visible, 2 = ABS_ALWAYSONTOP).
        Restore(ReadLease(LeasePath) ?? new Lease { State = 2 });
    }
    sealed class ProcessWait : WaitHandle
    {
        internal ProcessWait(Process process)
        {
            SafeWaitHandle = new SafeWaitHandle(process.Handle, false);
        }
    }
    internal static int Guard(int parentId, string path)
    {
        var lease = ReadLease(path);
        if (lease == null)
            return 2;
        try
        {
            using var parent = Process.GetProcessById(parentId);
            if (parent.StartTime.ToUniversalTime().Ticks != lease.ParentStart)
            {
                Restore(lease);
                return 0;
            }
            using var ready = EventWaitHandle.OpenExisting(lease.Ready);
            using var release = EventWaitHandle.OpenExisting(lease.Release);
            using var ended = new ProcessWait(parent);
            ready.Set();
            WaitHandle.WaitAny(new WaitHandle[] { ended, release });
            Restore(lease);
            return 0;
        }
        catch { Restore(lease); return 1; }
    }
    internal static List<AppWindow> Applications()
    {
        var result = new List<AppWindow>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h) || Native.Owns(h) || ClassName(h) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return true;
            long style = Native.ExtendedStyle(h);
            if ((style & 0x80) != 0 || GetWindow(h, 4) != IntPtr.Zero && (style & 0x40000) == 0)
                return true;
            if (DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0)
                return true;
            string title = Native.Title(h);
            if (string.IsNullOrWhiteSpace(title))
                return true;
            string path = "";
            try
            {
                GetWindowThreadProcessId(h, out uint pid);
                path = ProcessImagePath(pid);
            }
            catch (Exception)
            {
                // Processus fermé ou inaccessible : chemin d'image vide.
            }
            if (NeedsPackageKey(path))
            {
                string aumid = AppUserModelId(h);
                if (aumid.Contains('!'))
                    path = @"shell:AppsFolder\" + aumid;
            }
            if (path.EndsWith(@"\ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                path = "";
            if (string.IsNullOrEmpty(path))
                path = CatalogueWatchRules.PseudoPath(h.ToInt64());
            result.Add(new(h, title, path));
            return true;
        }, IntPtr.Zero);
        return result;
    }
    internal static void Activate(AppWindow app)
    {
        if (!Native.IsWindow(app.Handle))
            return;
        if (Native.GetForegroundWindow() == app.Handle)
        {
            ShowWindow(app.Handle, 6);
            return;
        }
        if (IsIconic(app.Handle))
            ShowWindow(app.Handle, 9);
        SetForegroundWindow(app.Handle);
    }
    internal static void WindowsKey(byte key)
    {
        const byte win = 0x5B;
        keybd_event(win, 0, 0, UIntPtr.Zero);
        if (key != 0)
            keybd_event(key, 0, 0, UIntPtr.Zero);
        if (key != 0)
            keybd_event(key, 0, 2, UIntPtr.Zero);
        keybd_event(win, 0, 2, UIntPtr.Zero);
    }
}
