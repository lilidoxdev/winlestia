// Quels moniteurs affichent actuellement une fenêtre de premier plan en plein écran (YouTube en plein écran, lecteur vidéo, jeu sans
// bordure) : le chrome du shell doit s'y masquer. Piloté par événements : SetWinEventHook (WINEVENT_OUTOFCONTEXT) sur
// EVENT_SYSTEM_FOREGROUND, MINIMIZESTART et EVENT_OBJECT_LOCATIONCHANGE (processus au premier plan) arme un minuteur anti-rebond unique de
// 40 ms (réarmer = anti-rebond), puis une chaîne de revérifications d'apaisement (voir SettleDelays) ; tant qu'un moniteur est en plein
// écran, un minuteur de sécurité à 1 s revérifie aussi. Le recalcul tourne sur un thread du pool. Les hooks OUTOFCONTEXT livrent par
// la boucle de messages du thread qui les a installés : ils vivent sur un thread de hook dédié, bloqué dans GetMessage ; Start() peut
// être appelé depuis n'importe quel thread. Plein écran = fenêtre de premier plan, visible, non réduite (ni la nôtre ni celles du
// shell) dont le cadre DWM ou le rectangle couvre tout son moniteur et qui n'a pas de barre de titre (une fenêtre maximisée garde la
// sienne). En option, QUNS_RUNNING_D3D_FULL_SCREEN (D3D exclusif) est rattaché au moniteur de la fenêtre de premier plan. Current = noms
// de périphérique des moniteurs (\\.\DISPLAY1), signalés par Changed seulement quand l'ensemble change.
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
    // Les navigateurs passent en plein écran plusieurs secondes après les événements qui l'annoncent (jusqu'à ~5 s observées avec
    // Edge, sans événement à la fin) : revérifications d'apaisement à +1,5 s, +3 s, +6 s après le dernier événement.
    static readonly int[] SettleDelays = { 300, 600, 1000, 1500, 3000, 6000 };
    // armé seulement tant qu'un moniteur est en plein écran ; les événements (premier plan, réduction, position) pilotent la sortie
    // normale
    const int SafetyNetMs = 1000;
    static int settleStep;
    static bool started;
    // Les handles de hook appartiennent au thread de hook qui les a créés (thread-static : la fin d'un ancien thread ne touche jamais
    // les handles d'une exécution plus récente).
    [ThreadStatic] static IntPtr hookForeground, hookLocation, hookMinimize;
    [ThreadStatic] static int myGen;   // génération de l'exécution à laquelle appartient ce thread de hook
    static int gen;                    // incrémenté par Start, protégé par gate
    static WinEventProc? proc;   // gardé vivant pendant toute la durée de vie des hooks
    static volatile string[] current = Array.Empty<string>();
    internal static string[] Current => current;
    // Levé (thread du pool, hors de tout verrou) seulement quand l'ensemble change ; les abonnés ne doivent pas lever d'exception (elles
    // sont avalées).
    internal static event Action? Changed;
    // Ids de processus supplémentaires à ignorer (assistants frontend / backend) ; ce processus est toujours ignoré.
    internal static Func<uint[]>? IgnoredPids;
    // Crochet de test : considère aussi les fenêtres de ce processus.
    internal static bool IncludeOwnProcessForTest;

    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    // Les callbacks WinEvent hors contexte ne sont livrés que si le thread qui a posé le hook pompe des messages : les hooks vivent donc
    // sur un thread dédié bloqué dans GetMessage (aucun CPU au repos). Poser les hooks depuis un thread qui ne pompe pas retardait les
    // événements de plusieurs secondes.
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
        PeekMessageW(out MSG m, IntPtr.Zero, 0, 0, 0);   // crée la file de messages de ce thread avant que Stop puisse y poster
        myGen = g;
        lock (gate)
        {
            if (!started || proc == null || gen != g)
            {
                ready.Set();
                return;
            }
            hookThreadId = GetCurrentThreadId();
            hookForeground = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, proc, 0, 0,
                WINEVENT_OUTOFCONTEXT);
            hookMinimize = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZESTART, IntPtr.Zero, proc, 0, 0,
                WINEVENT_OUTOFCONTEXT);
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
        // Toujours retirer nos propres hooks ; ne toucher aux statiques partagés que si aucune exécution plus récente n'a pris le
        // relais.
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
        // le thread de hook se déconnecte lui-même (UnhookWinEvent appartient au thread installateur)
        bool joined = t?.Join(2000) ?? true;
        lock (gate)
        {
            if (!started && joined)
                proc = null;
        }   // en cas de délai dépassé, les hooks peuvent encore être vivants : garder le délégué référencé
        lock (computeGate)
        {
            current = Array.Empty<string>();
        }   // attend un Recompute en cours ; SetSet revérifie `started`. Pas de Changed : les abonnés sont en cours de démontage.
    }

    // LOCATIONCHANGE est limité au processus de la fenêtre de premier plan, pour que les mouvements du curseur ailleurs ne nous
    // réveillent jamais. Thread de hook uniquement, l'appelant tient gate. `target` est la fenêtre nommée par l'événement FOREGROUND
    // : GetForegroundWindow() peut encore renvoyer la fenêtre précédente au traitement de l'événement (vu avec une nouvelle fenêtre
    // Edge), ce qui laissait le hook sur le mauvais processus.
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
        // tout le processus : les navigateurs redimensionnent depuis un autre thread
        hookLocation = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, proc, pid, 0,
            WINEVENT_OUTOFCONTEXT);
    }

    static void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (evt == EVENT_OBJECT_LOCATIONCHANGE && (idObject != OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero || GetAncestor(hwnd,
            GA_ROOT) != hwnd))
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
        {   // sérialisé : un ancien calcul ne peut jamais écraser un plus récent
            try
            {
                changed = SetSet(Compute());
            }
            catch { /* keep the previous value */ }
        }
        // Tant qu'un moniteur est en plein écran, un filet de sécurité lent (SafetyNetMs) rattrape une sortie qu'aucun événement n'a
        // annoncée, par exemple un plantage (aucun coût sinon).
        lock (gate)
        {
            if (!started)
                return;
            watch?.Change(current.Length > 0 ? SafetyNetMs : Timeout.Infinite, Timeout.Infinite);
            // Auto-réparation : si le hook de position n'est pas sur le processus de la fenêtre active, demander au thread de hook de le
            // déplacer.
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

    // Enregistre un nouvel ensemble ; vrai quand il diffère (sans tenir compte de l'ordre). Refusé une fois arrêté.
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
