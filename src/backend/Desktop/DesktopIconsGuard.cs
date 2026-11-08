// C# 12, fichier DesktopIconsGuard.cs, cible Windows 11 / .NET 8, réapplication de FWF_NOICONS pilotée par événements
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PanneauxGlass;

// Wallpaper Engine / DesktopHut peuvent faire recréer SHELLDLL_DefView après nous : le drapeau FWF_NOICONS est alors perdu.
// Hook hors contexte limité au processus Explorer du Bureau ; aucun minuteur périodique, aucun hook souris.
internal sealed class DesktopIconsGuard : IDisposable
{
    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)]
    struct Msg
    {
        public IntPtr Hwnd; public uint Message; public UIntPtr W; public IntPtr L; public uint Time; public int X, Y;
    }
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid,
        uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern int GetMessage(out Msg message, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint message, UIntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    readonly Func<bool> managed; readonly Action reassert, relinquished; readonly object gate = new();
        readonly DesktopReassertPolicy policy = new(); readonly Timer timer;
    Thread? thread; uint threadId; WinEventProc? proc; bool active, running, disposed, forced; int logged;

    internal bool Installed
    {
        get
        {
            lock (gate)
                return active;
        }
    }

    internal DesktopIconsGuard(Func<bool> managed, Action reassert, Action relinquished)
    {
        this.managed = managed;
        this.reassert = reassert;
        this.relinquished = relinquished;
        timer = new Timer(Fire, null, Timeout.Infinite, Timeout.Infinite);
    }

    static string ClassOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return "";
        var text = new StringBuilder(64);
        return GetClassName(hwnd, text, 64) > 0 ? text.ToString() : "";
    }

    // (Ré)installe le hook sur le processus Explorer courant ; faux si le Bureau n'est pas encore publié (TaskbarCreated réessaiera).
    internal bool Install()
    {
        Uninstall();
        IntPtr shell = GetShellWindow();
        uint pid = 0;
        if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out pid) == 0 || pid == 0)
        {
            Store.Log("Surveillance du Bureau: Explorateur pas prêt.");
            return false;
        }
        using var ready = new ManualResetEventSlim(false);
        bool installed = false;
        uint id = 0;
        proc = OnEvent;
        var callback = proc;
        var worker = new Thread(() =>
        {
            id = GetCurrentThreadId();
            IntPtr a = SetWinEventHook(0x8000, 0x8000, IntPtr.Zero, callback, pid, 0, 0), b = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero,
                callback, pid, 0, 0), c = SetWinEventHook(0x800F, 0x800F, IntPtr.Zero, callback, pid, 0, 0);
            installed = a != IntPtr.Zero && b != IntPtr.Zero && c != IntPtr.Zero;
            ready.Set();
            try
            {
                if (installed)
                    while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
                    {
                    }
            }
            finally { if (a != IntPtr.Zero) UnhookWinEvent(a); if (b != IntPtr.Zero) UnhookWinEvent(b);
                if (c != IntPtr.Zero) UnhookWinEvent(c); }
        })
        {
            IsBackground = true,
            Name = "Desktop icons hook"
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        if (!ready.Wait(2000) || !installed)
        {
            Store.Log("Surveillance du Bureau: hook indisponible.");
            return false;
        }
        lock (gate)
        {
            if (disposed)
            {
                PostThreadMessage(id, 0x12, UIntPtr.Zero, IntPtr.Zero);
                return false;
            }
            thread = worker;
            threadId = id;
            active = true;
            policy.Reset();
        }
        Store.Log("Surveillance du Bureau installée (Explorateur pid " + pid + ").");
        return true;
    }

    internal void Uninstall()
    {
        Thread? old;
        uint id;
        lock (gate)
        {
            active = false;
            old = thread;
            id = threadId;
            thread = null;
            policy.Reset();
            timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        if (old == null)
            return;
        PostThreadMessage(id, 0x12, UIntPtr.Zero, IntPtr.Zero);
        old.Join(1000);
    }

    void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero || !active)
            return;
        string cls = ClassOf(hwnd);
        if (cls != "WorkerW" && cls != "SHELLDLL_DefView")
            return;
        if (DesktopEventFilter.Relevant(cls, cls == "SHELLDLL_DefView" ? ClassOf(GetParent(hwnd)) : ""))
            Request(false);
    }

    // force : Explorateur redémarré, on réaffirme même sans événement du Bureau (le premier masquage a pu échouer).
    internal void Request(bool force)
    {
        lock (gate)
        {
            if (disposed || running)
                return;
            if (!force && (!active || !managed()))
                return;
            var now = DateTime.UtcNow;
            if (!policy.Event(now, force))
                return;
            if (force)
                forced = true;
            Arm(now);
        }
    }
    void Arm(DateTime now)
    {
        var delay = policy.Delay(now);
        if (delay != null)
            timer.Change(delay.Value, Timeout.InfiniteTimeSpan);
    }

    void Fire(object? state)
    {
        lock (gate)
        {
            if (disposed || running)
                return;
            var now = DateTime.UtcNow;
            if (!policy.Due(now))
            {
                Arm(now);
                return;
            }
            running = true;
        }
        bool gaveUp = false;
        try
        {
            // HideIcons à 0 : quelqu'un a volontairement rétabli les icônes (SECOURS_BUREAU --show-only, menu, case Windows). On se
            // retire.
            bool intent;
            lock (gate)
                // après un redémarrage d'Explorer, HideIcons peut valoir 0 sans choix de l'utilisateur (le premier masquage a échoué)
                intent = forced;
            if (!intent && !DesktopReassertPolicy.StillWanted(Native.DesktopHidden() ? 1 : 0))
            {
                gaveUp = true;
                Store.Log("Icônes du Bureau rétablies par ailleurs : surveillance retirée.");
                relinquished();
                Uninstall();
                return;
            }
            reassert();
            lock (gate)
                forced = false;
            if (logged++ < 20)
                Store.Log("Icônes du Bureau réaffirmées (Explorateur).");
        }
        catch (Exception ex) { if (logged++ < 20) Store.Log("Réaffirmation du Bureau: " + ex.Message); }
        finally { lock (gate) { running = false; if (!disposed && !gaveUp) { var end = DateTime.UtcNow; policy.Ran(end); Arm(end); } } }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
        }
        Uninstall();
        timer.Dispose();
    }
}
