// language: C# 12, file: LockKeyBlocker.cs, target: Windows 11 / .NET 8, hook clavier bas niveau actif uniquement pendant le verrouillage
using System;
using System.Runtime.InteropServices;
using System.Threading;
namespace PanneauxGlass;

// Règle pure : touches avalées pendant le verrouillage (Win, Alt+Tab, Alt+Échap, Ctrl+Échap, Alt+F4, Alt+Espace).
internal static class LockKeyRules
{
    internal static bool Blocks(uint vk, bool alt, bool ctrl) => vk switch
    {
        0x5B or 0x5C => true,
        0x09 => alt,
        0x1B => alt || ctrl,
        0x73 => alt,
        0x20 => alt,
        _ => false
    };
}

// Le hook vit sur son propre thread avec une boucle de messages ; Stop() le retire. Échec ouvert : si le hook ne s'installe pas,
// le verrouillage continue sans filtrage des touches. Le thread est de fond, donc la fin du processus retire le hook.
internal sealed class LockKeyBlocker : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct KeyData
    {
        public uint Key, Scan, Flags, Time; public UIntPtr Extra;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Msg
    {
        public IntPtr Hwnd; public uint Message; public UIntPtr W; public IntPtr L; public uint Time; public int X, Y;
    }
    delegate IntPtr Callback(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookExW(int id, Callback callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern int GetMessageW(out Msg message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PeekMessageW(out Msg message, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] static extern bool PostThreadMessageW(uint thread, uint message, UIntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    readonly object gate = new();
    readonly Callback callback;
    Thread? thread;
    uint threadId;
    IntPtr hook;

    internal LockKeyBlocker() => callback = Handle;

    internal bool Active
    {
        get
        {
            lock (gate)
                return thread != null;
        }
    }

    internal void Start()
    {
        lock (gate)
        {
            if (thread != null)
                return;
            using var ready = new ManualResetEventSlim();
            var t = new Thread(() => Run(ready)) { IsBackground = true, Name = "Winlestia lock keys" };
            thread = t;
            t.Start();
            ready.Wait(2000);
        }
    }

    void Run(ManualResetEventSlim ready)
    {
        try
        {
            // PeekMessage force la création de la file de messages du thread avant que Stop() ne puisse y poster WM_QUIT.
            PeekMessageW(out _, IntPtr.Zero, 0, 0, 0);
            hook = SetWindowsHookExW(13, callback, Native.GetModuleHandleW(null), 0);
            threadId = GetCurrentThreadId();
            if (hook == IntPtr.Zero)
            {
                Store.Log("Verrouillage : hook clavier indisponible, touches système non filtrées.");
                return;
            }
        }
        finally
        {
            ready.Set();
        }
        try
        {
            while (GetMessageW(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }
        }
        finally
        {
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }
    }

    internal void Stop()
    {
        Thread? t;
        lock (gate)
        {
            t = thread;
            thread = null;
        }
        if (t == null)
            return;
        uint id = threadId;
        if (id != 0)
        {
            for (int i = 0; i < 10 && t.IsAlive; i++)
            {
                if (PostThreadMessageW(id, 0x0012, UIntPtr.Zero, IntPtr.Zero))
                    break;
                Thread.Sleep(20);
            }
        }
        t.Join(500);
        threadId = 0;
    }

    IntPtr Handle(int code, IntPtr message, IntPtr data)
    {
        try
        {
            if (code >= 0 && message.ToInt64() is 0x100 or 0x101 or 0x104 or 0x105)
            {
                var key = Marshal.PtrToStructure<KeyData>(data);
                bool alt = (key.Flags & 0x20) != 0, ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;
                if (LockKeyRules.Blocks(key.Key, alt, ctrl))
                    return new IntPtr(1);
            }
        }
        catch (Exception)
        {
            // échec ouvert : une touche n'est jamais bloquée par erreur
        }
        return CallNextHookEx(hook, code, message, data);
    }

    public void Dispose() => Stop();
}
