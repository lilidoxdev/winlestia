// Step 4b: the native shell context menu (IContextMenu/2/3 through IShellFolder.GetUIObjectOf) and the shell data object used for outgoing drags.
// Everything runs on the UI thread (STA) because shell extensions require it; every COM object, PIDL and HMENU is released in Dispose.
// Native menus cannot take our rounded M3 style: the best we can do is the system dark/light popup theme (uxtheme ordinals 135/136).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;

namespace WinlestiaFiles;

[StructLayout(LayoutKind.Sequential)]
internal struct NPoint { public int X, Y; internal NPoint(int x, int y) { X = x; Y = y; } }

[ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellFolder {
    [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out IntPtr pidl, ref uint attrs);
    [PreserveSig] int EnumObjects(IntPtr hwnd, uint flags, out IntPtr enumIdList);
    [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
    [PreserveSig] int CreateViewObject(IntPtr hwnd, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] apidl, ref uint attrs);
    [PreserveSig] int GetUIObjectOf(IntPtr hwnd, uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl, ref Guid riid, IntPtr reserved, out IntPtr ppv);
    [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr strret);
    [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr newPidl);
}

[StructLayout(LayoutKind.Sequential)]
internal struct CmInvokeInfoEx {
    public uint cbSize, fMask; public IntPtr hwnd, lpVerb, lpParameters, lpDirectory; public int nShow; public uint dwHotKey; public IntPtr hIcon, lpTitle,
        lpVerbW, lpParametersW, lpDirectoryW, lpTitleW; public NPoint ptInvoke;
}

[ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu {
    [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
    [PreserveSig] int InvokeCommand(ref CmInvokeInfoEx info);
    [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cch);
}

[ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu2 {
    [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
    [PreserveSig] int InvokeCommand(ref CmInvokeInfoEx info);
    [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cch);
    [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
}

[ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu3 {
    [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
    [PreserveSig] int InvokeCommand(ref CmInvokeInfoEx info);
    [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cch);
    [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
    [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result);
}

// The items (or one folder) of a single parent: IShellFolder + child PIDLs, all freed in Dispose.
internal sealed class ShellItems : IDisposable {
    static Guid iidFolder = new("000214E6-0000-0000-C000-000000000046");
    internal static Guid IidContextMenu = new("000214e4-0000-0000-c000-000000000046"), IidDataObject = new("0000010e-0000-0000-C000-000000000046");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, IntPtr bc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll")] static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr pidlLast);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr pointer);

    readonly List<IntPtr> full = new();
    readonly List<IntPtr> children = new();
    IShellFolder? folder;
    bool background, disposed;
    readonly int owner = Environment.CurrentManagedThreadId;   // the COM objects below live in this STA; calling them from another thread or after Dispose is a native crash, not an exception
    string? parentDir;

    // Items of one folder (all paths must share the parent; the first one decides). Null when nothing parses.
    internal static ShellItems? Of(IReadOnlyList<string> paths) {
        var s = new ShellItems();
        try {
            foreach (var path in paths) {
                // Child PIDLs are relative to ONE folder: a path from another parent would be handed to the wrong IShellFolder (access violation in GetUIObjectOf).
                string dir = System.IO.Path.GetDirectoryName(path) ?? "";
                if (s.parentDir == null) s.parentDir = dir;
                else if (!string.Equals(s.parentDir, dir, StringComparison.OrdinalIgnoreCase)) continue;
                if (SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero) continue;
                s.full.Add(pidl);
                if (SHBindToParent(pidl, ref iidFolder, out var ppv, out var last) != 0) continue;
                if (s.folder == null) { s.folder = (IShellFolder)Marshal.GetObjectForIUnknown(ppv); s.children.Add(last); }
                else if (Marshal.GetObjectForIUnknown(ppv) is IShellFolder other) { Marshal.ReleaseComObject(other); s.children.Add(last); }
                Marshal.Release(ppv);
            }
            if (s.folder != null && s.children.Count > 0) return s;
        } catch (Exception ex) { App.CrashLog(ex); }
        s.Dispose();
        return null;
    }

    // The folder itself (background menu).
    internal static ShellItems? OfFolder(string path) {
        var s = new ShellItems { background = true };
        try {
            if (SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero) return null;
            s.full.Add(pidl);
            if (SHBindToParent(pidl, ref iidFolder, out var ppv, out var last) != 0) { s.Dispose(); return null; }
            var parent = (IShellFolder)Marshal.GetObjectForIUnknown(ppv); Marshal.Release(ppv);
            try {
                if (parent.BindToObject(last, IntPtr.Zero, ref iidFolder, out var self) != 0 || self == IntPtr.Zero) { s.Dispose(); return null; }
                s.folder = (IShellFolder)Marshal.GetObjectForIUnknown(self); Marshal.Release(self);
            } finally { Marshal.ReleaseComObject(parent); }
            return s;
        } catch (Exception ex) { App.CrashLog(ex); s.Dispose(); return null; }
    }

    internal bool IsBackground => background;
    internal int Count => children.Count;

    // Raw interface pointer (caller releases with Marshal.Release), or Zero.
    internal IntPtr Ui(Guid iid, IntPtr hwnd) {
        if (disposed || folder == null || Environment.CurrentManagedThreadId != owner || (!background && children.Count == 0)) return IntPtr.Zero;
        int hr = background ? folder.CreateViewObject(hwnd, ref iid, out var p) : folder.GetUIObjectOf(hwnd, (uint)children.Count, children.ToArray(), ref iid, IntPtr.Zero, out p);
        return hr == 0 ? p : IntPtr.Zero;
    }

    // Shell data object (CF_HDROP, shell IDList array, drag description) as a raw pointer for SHDoDragDrop; caller releases.
    internal IntPtr DataObject(IntPtr hwnd) => background ? IntPtr.Zero : Ui(IidDataObject, hwnd);

    // Managed view of the same data object (used by the tests and by anything that needs to read it back).
    internal System.Runtime.InteropServices.ComTypes.IDataObject? ManagedDataObject(IntPtr hwnd) {
        var p = DataObject(hwnd); if (p == IntPtr.Zero) return null;
        try { return (System.Runtime.InteropServices.ComTypes.IDataObject)Marshal.GetObjectForIUnknown(p); } finally { Marshal.Release(p); }
    }

    public void Dispose() {
        disposed = true;
        if (folder != null) { try { Marshal.FinalReleaseComObject(folder); } catch { } folder = null; }
        foreach (var p in full) CoTaskMemFree(p);
        full.Clear(); children.Clear();
    }
}

internal sealed class ShellMenu : IDisposable {
    internal const uint First = 0x100, Last = 0x7FFF;
    const uint CMF_CANRENAME = 0x10, CMF_EXTENDEDVERBS = 0x100, CMIC_MASK_UNICODE = 0x4000, CMIC_MASK_SHIFT_DOWN = 0x10000000, CMIC_MASK_PTINVOKE = 0x20000000;
    const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100, MF_STRING = 0, MF_SEPARATOR = 0x800, MF_BYPOSITION = 0x400;
    const int WM_INITMENUPOPUP = 0x117, WM_MENUCHAR = 0x120, WM_DRAWITEM = 0x2B, WM_MEASUREITEM = 0x2C;

    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool InsertMenuW(IntPtr menu, uint position, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] static extern IntPtr GetSubMenu(IntPtr menu, int position);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMenuItemInfoW(IntPtr menu, uint item, bool byPosition, ref MenuItemInfo info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryW(string name);
    [DllImport("kernel32.dll")] static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);

    [StructLayout(LayoutKind.Sequential)]
    struct MenuItemInfo { public uint cbSize, fMask, fType, fState, wID; public IntPtr hSubMenu, hbmpChecked, hbmpUnchecked, dwItemData, dwTypeData; public uint cch; public IntPtr hbmpItem; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int SetPreferredAppModeFn(int mode);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void FlushMenuThemesFn();

    // Win32 popup menus follow the system theme only when the process opts in: ForceDark/ForceLight from our palette. Guarded: Windows 10 1903+ only, never throws.
    internal static void InitMenuTheme(bool dark) {
        try {
            if (Environment.OSVersion.Version.Build < 18362) return;
            var ux = LoadLibraryW("uxtheme.dll"); if (ux == IntPtr.Zero) return;
            var set = GetProcAddress(ux, (IntPtr)135); var flush = GetProcAddress(ux, (IntPtr)136);
            if (set == IntPtr.Zero) return;
            Marshal.GetDelegateForFunctionPointer<SetPreferredAppModeFn>(set)(dark ? 2 : 3);
            if (flush != IntPtr.Zero) Marshal.GetDelegateForFunctionPointer<FlushMenuThemesFn>(flush)();
        } catch (Exception ex) { Perf.Log("menu theme: " + ex.Message); }
    }

    readonly ShellItems items;
    IntPtr menu;
    object? com;
    IContextMenu? c1; IContextMenu2? c2; IContextMenu3? c3;
    internal IntPtr Handle => menu;

    ShellMenu(ShellItems items) { this.items = items; }

    // Every QueryContextMenu (item or background menu) leaks one suspended WorkFoldersShell.dll thread in this process; the resident instance recycles itself on this count.
    static int builds;
    internal static int Builds => Volatile.Read(ref builds);

    internal static ShellMenu? Create(IntPtr hwnd, ShellItems items, bool extended) {
        var m = new ShellMenu(items);
        try {
            var p = items.Ui(ShellItems.IidContextMenu, hwnd); if (p == IntPtr.Zero) { m.Dispose(); return null; }
            try { m.com = Marshal.GetObjectForIUnknown(p); } finally { Marshal.Release(p); }
            m.c1 = (IContextMenu)m.com; m.c2 = m.com as IContextMenu2; m.c3 = m.com as IContextMenu3;
            m.menu = CreatePopupMenu();
            uint flags = (items.IsBackground ? 0u : CMF_CANRENAME) | (extended ? CMF_EXTENDEDVERBS : 0);
            Interlocked.Increment(ref builds);
            if (m.c1.QueryContextMenu(m.menu, 0, First, Last, flags) < 0) { m.Dispose(); return null; }
            return m;
        } catch (Exception ex) { App.CrashLog(ex); m.Dispose(); return null; }
    }

    // Our own entries go first (ids below First), followed by a separator.
    internal void Prepend(IReadOnlyList<(uint id, string text)> own) {
        if (own.Count == 0) return;
        InsertMenuW(menu, 0, MF_BYPOSITION | MF_SEPARATOR, UIntPtr.Zero, null);
        for (int i = own.Count - 1; i >= 0; i--) InsertMenuW(menu, 0, MF_BYPOSITION | MF_STRING, (UIntPtr)own[i].id, own[i].text);
    }

    // Modal: returns the chosen command id (0 = dismissed). The window hook forwards the owner-draw / submenu messages the extensions need ("Open with", "Send to", "New").
    internal uint Track(IntPtr hwnd, int x, int y) {
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(Hook);
        try { SetForegroundWindow(hwnd); return TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, hwnd, IntPtr.Zero); }
        finally { source?.RemoveHook(Hook); }
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
        if (msg != WM_INITMENUPOPUP && msg != WM_DRAWITEM && msg != WM_MEASUREITEM && msg != WM_MENUCHAR) return IntPtr.Zero;
        try {
            if (c3 != null && c3.HandleMenuMsg2((uint)msg, wParam, lParam, out var result) == 0) { handled = true; return result; }
            if (c2 != null && msg != WM_MENUCHAR && c2.HandleMenuMsg((uint)msg, wParam, lParam) == 0) { handled = true; return msg == WM_INITMENUPOPUP ? IntPtr.Zero : (IntPtr)1; }
        } catch (Exception ex) { Perf.Log("menu msg: " + ex.Message); }
        return IntPtr.Zero;
    }

    internal bool Invoke(IntPtr hwnd, uint id, int x, int y, bool shift) {
        var info = new CmInvokeInfoEx { fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE | (shift ? CMIC_MASK_SHIFT_DOWN : 0), hwnd = hwnd, lpVerb = (IntPtr)(id - First), lpVerbW = (IntPtr)(id - First), nShow = 1, ptInvoke = new NPoint(x, y) };
        info.cbSize = (uint)Marshal.SizeOf<CmInvokeInfoEx>();
        try { return c1!.InvokeCommand(ref info) >= 0; } catch (Exception ex) { Perf.Log("menu invoke: " + ex.Message); return false; }
    }

    // Canonical verb of a command ("open", "copy", "properties"...), empty when the extension has none.
    internal string Verb(uint id) {
        var buffer = Marshal.AllocHGlobal(1024);
        try {
            Marshal.WriteInt16(buffer, 0);
            return c1!.GetCommandString((UIntPtr)(id - First), 4 /* GCS_VERBW */, IntPtr.Zero, buffer, 512) == 0 ? Marshal.PtrToStringUni(buffer) ?? "" : "";
        } catch { return ""; } finally { Marshal.FreeHGlobal(buffer); }
    }

    // (id, verb, text, depth) for every command; submenus are populated first (what WM_INITMENUPOPUP does) when requested.
    internal List<(uint id, string verb, string text, int depth)> Enumerate(bool populate) {
        var all = new List<(uint, string, string, int)>();
        Walk(menu, 0, populate, all);
        return all;
    }

    void Walk(IntPtr hmenu, int depth, bool populate, List<(uint, string, string, int)> all) {
        int n = GetMenuItemCount(hmenu);
        var text = Marshal.AllocHGlobal(1024);
        try {
            for (int i = 0; i < n; i++) {
                Marshal.WriteInt16(text, 0);
                var info = new MenuItemInfo { fMask = 0x2 | 0x4 | 0x40 /* ID | SUBMENU | STRING */, dwTypeData = text, cch = 511 };
                info.cbSize = (uint)Marshal.SizeOf<MenuItemInfo>();
                if (!GetMenuItemInfoW(hmenu, (uint)i, true, ref info)) continue;
                string label = Marshal.PtrToStringUni(text) ?? "";
                if (info.hSubMenu != IntPtr.Zero) {
                    if (populate) { try { if (c3 != null) c3.HandleMenuMsg2(WM_INITMENUPOPUP, info.hSubMenu, (IntPtr)(i & 0xFFFF), out _); else c2?.HandleMenuMsg(WM_INITMENUPOPUP, info.hSubMenu, (IntPtr)(i & 0xFFFF)); } catch { } }
                    all.Add((0, "", label, depth));
                    if (depth < 2) Walk(info.hSubMenu, depth + 1, populate, all);
                } else if (info.wID >= First && info.wID <= Last) all.Add((info.wID, Verb(info.wID), label, depth));
                else if (info.wID != 0) all.Add((info.wID, "", label, depth));
            }
        } finally { Marshal.FreeHGlobal(text); }
    }

    public void Dispose() {
        if (menu != IntPtr.Zero) { DestroyMenu(menu); menu = IntPtr.Zero; }
        c1 = null; c2 = null; c3 = null;
        if (com != null) { try { Marshal.FinalReleaseComObject(com); } catch { } com = null; }
    }
}
