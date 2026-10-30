// C# 12 / VirtualDesktops.cs / Windows 11. Virtual desktop indicator support WITHOUT any undocumented COM interface.
// Read: HKCU registry only (VirtualDesktopIDs, CurrentVirtualDesktop - per-session key first, general key as fallback, optional Desktops\{GUID}\Name).
// Changes: no registry notification (Windows 11 does not signal RegNotifyChangeKeyValue for these writes): out-of-context WinEvent hooks (foreground, cloak/uncloak)
// on one dedicated message-loop thread re-read the registry 150 ms and 900 ms after the last event. At rest the thread blocks in GetMessage: no timer, no polling, no wakeup. Stop() posts WM_QUIT and joins it.
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
    static volatile Snapshot snapshot = new(0, 0, Array.Empty<string>());
    internal static Snapshot Current => snapshot;
    // Raised on the watcher thread only when the snapshot differs; subscribers must not throw (swallowed).
    internal static event Action? Changed;

    static Thread? worker;
    static uint workerId;
    static WinEventProc? proc;   // static: the GC must never collect the hook delegate

    internal static void Start() {
        var ready = new ManualResetEventSlim(false);
        lock (gate) {
            if (worker != null) return;
            worker = new Thread(() => Run(ready)) { IsBackground = true, Name = "VirtualDesktopsWatcher" };
            worker.Start();
        }
        ready.Wait(2000);
    }

    internal static void Stop() {
        Thread? t; uint id;
        lock (gate) { t = worker; id = workerId; worker = null; workerId = 0; }
        if (t == null) return;
        try { if (id != 0) PostThreadMessageW(id, WM_QUIT, IntPtr.Zero, IntPtr.Zero); t.Join(2000); } catch { }
    }

    static string SessionRoot => Explorer + @"\SessionInfo\" + Process.GetCurrentProcess().SessionId;
    static string SessionKey => SessionRoot + @"\VirtualDesktops";

    // Hook thread state (touched only by the watcher thread: WinEvent callbacks run inside its DispatchMessage).
    [ThreadStatic] static long lastEventTick;
    [ThreadStatic] static int step;   // 0 idle, 1 waiting for the 150 ms re-read, 2 waiting for the 900 ms re-read
    [ThreadStatic] static uint timerId;   // a thread timer (no hwnd) gets its id FROM Windows: the requested id only selects an existing timer to replace

    static void Arm(uint ms) { var id = SetTimer(IntPtr.Zero, timerId, ms, IntPtr.Zero); if (id != UIntPtr.Zero) timerId = (uint)id; }
    static void Disarm() { if (timerId != 0) KillTimer(IntPtr.Zero, timerId); timerId = 0; }

    static void Run(ManualResetEventSlim ready) {
        PeekMessageW(out MSG m, IntPtr.Zero, 0, 0, 0);   // creates this thread's message queue before Stop can post to it
        IntPtr hFg = IntPtr.Zero, hCloak = IntPtr.Zero;
        try {
            lock (gate) { if (worker == null) return; workerId = GetCurrentThreadId(); }   // Stop already ran: leave
            proc = OnEvent;
            hFg = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            hCloak = SetWinEventHook(EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED, IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            ready.Set();
            Refresh();
            while (GetMessageW(out m, IntPtr.Zero, 0, 0) > 0) {
                if (m.hwnd == IntPtr.Zero && m.message == WM_TIMER && timerId != 0 && (uint)m.wParam == timerId) { OnTimer(); continue; }
                TranslateMessage(ref m); DispatchMessageW(ref m);
            }
        } catch { }
        finally {
            Disarm();
            if (hFg != IntPtr.Zero) UnhookWinEvent(hFg);
            if (hCloak != IntPtr.Zero) UnhookWinEvent(hCloak);
            ready.Set();
        }
    }

    static void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) {
        if (evt != EVENT_SYSTEM_FOREGROUND && (idObject != OBJID_WINDOW || idChild != 0)) return;
        lastEventTick = Environment.TickCount64; step = 1;
        Arm(150);   // passing the live id replaces the pending timer
    }

    static void OnTimer() {
        if (step == 0) { Disarm(); return; }
        Refresh();
        if (step == 1) {
            step = 2;
            long left = lastEventTick + 900 - Environment.TickCount64;
            Arm((uint)Math.Max(left, 10));
        } else { step = 0; Disarm(); }   // idle again: nothing armed
    }

    static void Refresh() {
        lock (gate) {   // watcher thread and SwitchTo's caller both land here: a slow read must not overwrite a newer one
            Snapshot next;
            try { next = Read(); } catch { next = new Snapshot(0, 0, Array.Empty<string>()); }
            var old = snapshot;
            if (old.Count == next.Count && old.Current == next.Current && old.Names.SequenceEqual(next.Names)) return;
            snapshot = next;
        }
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
        Refresh();   // a gesture may come before the pending debounce: never act on a stale snapshot
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
    const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018, WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2, WM_QUIT = 0x12, WM_TIMER = 0x113;
    const int OBJID_WINDOW = 0;
    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY, lPrivate; }
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern int GetMessageW(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PeekMessageW(out MSG m, IntPtr h, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")] static extern bool PostThreadMessageW(uint tid, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern UIntPtr SetTimer(IntPtr h, uint id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] static extern bool KillTimer(IntPtr h, uint id);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}
