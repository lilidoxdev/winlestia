// C# 12 / VirtualDesktops.cs / Windows 11. Virtual desktop indicator support WITHOUT any undocumented COM interface.
// Read: HKCU registry only (VirtualDesktopIDs, CurrentVirtualDesktop - per-session key first, general key as fallback, optional Desktops\{GUID}\Name).
// Changes: RegNotifyChangeKeyValue + WaitHandle on one dedicated thread (no timer, no polling); Stop() signals and joins it.
// Switch: only when asked by a user gesture; Win+Ctrl+Left/Right through SendInput, repeated by the gap between current and target. Modifiers are always released.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace PanneauxGlass;

internal static class VirtualDesktops {
    internal sealed record Snapshot(int Count, int Current, string[] Names);
    const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    const string General = Explorer + @"\VirtualDesktops";
    static readonly object gate = new();
    static Thread? worker;
    static ManualResetEvent? stop;
    static volatile Snapshot snapshot = new(0, 0, Array.Empty<string>());
    internal static Snapshot Current => snapshot;
    // Raised on the watcher thread only when the snapshot differs; subscribers must not throw (swallowed).
    internal static event Action? Changed;

    internal static void Start() {
        lock (gate) {
            if (worker != null) return;
            stop = new ManualResetEvent(false);
            var s = stop;
            worker = new Thread(() => Run(s)) { IsBackground = true, Name = "VirtualDesktopsWatcher" };
            worker.Start();
        }
    }

    internal static void Stop() {
        Thread? t; ManualResetEvent? s;
        lock (gate) { t = worker; s = stop; worker = null; stop = null; }
        if (t == null || s == null) return;
        try { s.Set(); t.Join(2000); } catch { }
        try { s.Dispose(); } catch { }
    }

    static string SessionRoot => Explorer + @"\SessionInfo\" + Process.GetCurrentProcess().SessionId;
    static string SessionKey => SessionRoot + @"\VirtualDesktops";

    static void Run(ManualResetEvent stopEvent) {
        while (true) {
            Refresh();
            var keys = new List<RegistryKey>(); var events = new List<ManualResetEvent>();
            try {
                // General key (subtree: Desktops\{GUID}\Name). Session key when present, else its parent (a non-recursive name watch sees the subkey appear).
                Watch(General, true, keys, events);
                if (!Watch(SessionKey, false, keys, events)) Watch(SessionRoot, false, keys, events);
                if (events.Count == 0) { stopEvent.WaitOne(); return; }   // nothing to watch: stay idle until Stop()
                var handles = new WaitHandle[events.Count + 1]; handles[0] = stopEvent;
                for (int i = 0; i < events.Count; i++) handles[i + 1] = events[i];
                if (WaitHandle.WaitAny(handles) == 0) return;
            } catch { try { if (stopEvent.WaitOne(1000)) return; } catch { return; } }   // rare failure: back off on the stop event, not a poll
            finally { foreach (var k in keys) try { k.Dispose(); } catch { } foreach (var e in events) try { e.Dispose(); } catch { } }
        }
    }

    static bool Watch(string path, bool subtree, List<RegistryKey> keys, List<ManualResetEvent> events) {
        RegistryKey? key = null;
        try {
            key = Registry.CurrentUser.OpenSubKey(path, false);
            if (key == null) return false;
            var ev = new ManualResetEvent(false);
            int rc = RegNotifyChangeKeyValue(key.Handle.DangerousGetHandle(), subtree, 0x1 | 0x4, ev.SafeWaitHandle.DangerousGetHandle(), true);   // NAME | LAST_SET
            if (rc != 0) { ev.Dispose(); key.Dispose(); return false; }
            keys.Add(key); events.Add(ev); return true;
        } catch { key?.Dispose(); return false; }
    }

    static void Refresh() {
        Snapshot next;
        try { next = Read(); } catch { next = new Snapshot(0, 0, Array.Empty<string>()); }
        var old = snapshot;
        if (old.Count == next.Count && old.Current == next.Current && old.Names.SequenceEqual(next.Names)) return;
        snapshot = next;
        try { Changed?.Invoke(); } catch { }
    }

    internal static Snapshot Read() {
        using var general = Registry.CurrentUser.OpenSubKey(General, false);
        if (general == null) return new Snapshot(0, 0, Array.Empty<string>());
        var ids = ParseIds(general.GetValue("VirtualDesktopIDs") as byte[]);
        byte[]? cur = null;
        using (var session = Registry.CurrentUser.OpenSubKey(SessionKey, false)) cur = session?.GetValue("CurrentVirtualDesktop") as byte[];
        cur ??= general.GetValue("CurrentVirtualDesktop") as byte[];
        int index = ids.Count == 0 || cur is not { Length: 16 } ? 0 : Math.Max(0, ids.FindIndex(i => i.AsSpan().SequenceEqual(cur)));
        var names = new string[ids.Count];
        for (int i = 0; i < ids.Count; i++) {
            try {
                using var d = general.OpenSubKey(@"Desktops\{" + new Guid(ids[i]).ToString().ToUpperInvariant() + "}", false);
                names[i] = (d?.GetValue("Name") as string) ?? "";
            } catch { names[i] = ""; }
        }
        return new Snapshot(ids.Count, index, names);
    }

    internal static List<byte[]> ParseIds(byte[]? data) {
        var list = new List<byte[]>();
        if (data == null) return list;
        for (int o = 0; o + 16 <= data.Length; o += 16) list.Add(data.AsSpan(o, 16).ToArray());
        return list;
    }

    // ---- switching ----
    const ushort VkLWin = 0x5B, VkControl = 0x11, VkLeft = 0x25, VkRight = 0x27;
    internal readonly record struct Key(ushort Vk, bool Up);

    // Pure plan, unit-tested without sending anything: modifiers down, one arrow press per step, modifiers up (always last).
    internal static List<Key> PlanKeys(int current, int target, int count) {
        var keys = new List<Key>();
        if (count < 2 || current < 0 || current >= count) return keys;
        target = Math.Clamp(target, 0, count - 1);
        int steps = target - current;
        if (steps == 0) return keys;
        ushort arrow = steps > 0 ? VkRight : VkLeft;
        keys.Add(new Key(VkLWin, false)); keys.Add(new Key(VkControl, false));
        for (int i = 0; i < Math.Abs(steps); i++) { keys.Add(new Key(arrow, false)); keys.Add(new Key(arrow, true)); }
        keys.Add(new Key(VkControl, true)); keys.Add(new Key(VkLWin, true));
        return keys;
    }

    // User gesture entry point. relative != 0 means wheel (next/previous), otherwise absolute target index.
    internal static bool SwitchTo(int target, int relative = 0) {
        var s = snapshot;
        if (s.Count < 2) return false;
        int dest = relative != 0 ? Math.Clamp(s.Current + relative, 0, s.Count - 1) : target;
        return Send(PlanKeys(s.Current, dest, s.Count));
    }

    static bool Send(List<Key> keys) {
        if (keys.Count == 0) return false;
        bool ok = true;
        try {
            foreach (var k in keys) {
                if (!SendKey(k.Vk, k.Up)) { ok = false; break; }
            }
        } catch { ok = false; }
        finally { ReleaseModifiers(); }
        return ok;
    }

    static bool IsExtended(ushort vk) => vk is VkLeft or VkRight or VkLWin;

    static bool SendKey(ushort vk, bool up) {
        var input = new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = (up ? 2u : 0u) | (IsExtended(vk) ? 1u : 0u) } } };
        return SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 1;
    }

    static void ReleaseModifiers() {
        foreach (ushort vk in new[] { VkControl, VkLWin }) { try { SendKey(vk, true); } catch { } }
    }

    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion U; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("advapi32.dll")] static extern int RegNotifyChangeKeyValue(IntPtr hKey, bool watchSubtree, uint filter, IntPtr hEvent, bool asynchronous);
}
