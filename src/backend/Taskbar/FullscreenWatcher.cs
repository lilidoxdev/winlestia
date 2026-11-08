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

internal static partial class FullscreenWatcher
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
}
