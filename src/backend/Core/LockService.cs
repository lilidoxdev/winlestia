// language: C# 12, file: LockService.cs, target: Windows 11 / .NET 8, état de l'écran de verrouillage : verrou, essais, hook clavier,
// minuteur d'inactivité
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
namespace PanneauxGlass;

// Tout l'état est lu et écrit sur le thread du dispatcher ; seul le calcul de l'empreinte part en tâche de fond.
// Le verrouillage n'est jamais persisté (démarrage déverrouillé) et se lève de lui-même dès que son interface disparaît.
internal sealed class LockService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct LastInput
    {
        public uint Size, Time;
    }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput info);

    readonly Dispatcher dispatcher;
    readonly Action notify;
    readonly Func<bool> frontendReady, fullscreen;
    readonly LockAttempts attempts = new();
    readonly LockKeyBlocker blocker = new();
    readonly DispatcherTimer idleTimer, watchdog, retry;
    bool locked, busy, disposed, shown;
    int generation;
    string passwordResult = "";
    int passwordSeq;

    internal LockService(Dispatcher dispatcher, Action notify, Func<bool> frontendReady, Func<bool> fullscreen)
    {
        this.dispatcher = dispatcher;
        this.notify = notify;
        this.frontendReady = frontendReady;
        this.fullscreen = fullscreen;
        idleTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        idleTimer.Tick += (_, _) => IdleFired();
        watchdog = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        watchdog.Tick += (_, _) => WatchdogFired();
        retry = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        retry.Tick += (_, _) =>
        {
            retry.Stop();
            Notify();
        };
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    internal bool IsLocked => locked;
    static Preferences Prefs => Store.State;

    internal object Snapshot() => new
    {
        locked,
        failures = attempts.Failures,
        retryAfter = attempts.RetryAfter(DateTime.UtcNow),
        passwordResult,
        passwordSeq
    };

    internal object SettingsSnapshot() => new
    {
        enabled = Prefs.LockEnabled,
        hasPassword = LockRules.HasPassword(Prefs),
        idleSeconds = Prefs.LockIdleSeconds
    };

    void Notify()
    {
        if (!disposed)
            _ = dispatcher.BeginInvoke((Action)(() => { if (!disposed) notify(); }));
    }

    internal bool LockNow()
    {
        bool done = false;
        if (!locked && !busy && Prefs.LockEnabled && LockRules.HasPassword(Prefs) && frontendReady())
        {
            generation++;
            locked = true;
            shown = false;
            done = true;
            attempts.Reset();
            idleTimer.Stop();
            blocker.Start();
            watchdog.Interval = TimeSpan.FromSeconds(LockRules.WatchdogSeconds);
            watchdog.Start();
        }
        Notify();
        return done;
    }

    // Le client confirme que sa fenêtre de verrouillage est visible ; hors verrouillage, sans effet.
    internal void Shown()
    {
        if (!locked)
            return;
        shown = true;
        watchdog.Stop();
    }

    void WatchdogFired()
    {
        watchdog.Stop();
        if (!disposed && LockRules.WatchdogShouldRelease(locked, shown))
        {
            Store.Log("Verrouillage : fenêtre non confirmée, déverrouillage.");
            Release(true);
        }
    }

    // Une seule échéance à usage unique à la fin du délai d'attente : l'évènement « lock » invite le client à relire l'état.
    void ArmRetry()
    {
        retry.Stop();
        int seconds = attempts.RetryAfter(DateTime.UtcNow);
        if (seconds > 0)
        {
            retry.Interval = TimeSpan.FromSeconds(seconds);
            retry.Start();
        }
    }

    void StopTimers()
    {
        watchdog.Stop();
        retry.Stop();
    }

    // Levée du verrou sans condition (fermeture, perte du client, données illisibles). Retire toujours le hook.
    internal void Release(bool announce)
    {
        bool was = locked;
        generation++;
        locked = false;
        busy = false;
        StopTimers();
        attempts.Reset();
        blocker.Stop();
        if (was)
            Rearm();
        if (announce && was)
            Notify();
    }

    internal void Unlock(string password)
    {
        if (!locked || busy)
        {
            Notify();
            return;
        }
        if (attempts.RetryAfter(DateTime.UtcNow) > 0)
        {
            Notify();
            return;
        }
        string hash = Prefs.LockHash, salt = Prefs.LockSalt;
        int iterations = Prefs.LockIterations;
        if (!LockSecret.IsUsable(hash, salt, iterations))
        {
            Store.Log("Verrouillage : données de mot de passe absentes ou illisibles, déverrouillage.");
            Release(true);
            return;
        }
        busy = true;
        int captured = generation;
        _ = Task.Run(() => LockSecret.Verify(password, hash, salt, iterations)).ContinueWith(t =>
        {
            bool ok = t.Status == TaskStatus.RanToCompletion && t.Result;
            _ = dispatcher.BeginInvoke((Action)(() =>
            {
                if (disposed || !busy || !LockRules.IsCurrent(captured, generation))
                    return;
                busy = false;
                if (!locked)
                    return;
                if (ok)
                    Release(false);
                else
                {
                    attempts.Fail(DateTime.UtcNow);
                    ArmRetry();
                }
                Notify();
            }));
        }, TaskScheduler.Default);
    }

    internal void SetPassword(string current, string password)
    {
        passwordSeq++;
        if (locked || busy)
        {
            passwordResult = "";
            Notify();
            return;
        }
        busy = true;
        var prefs = Prefs;
        _ = Task.Run(() =>
        {
            // Travail sur une copie des seules données utiles : les préférences ne sont modifiées que sur le thread du dispatcher.
            var scratch = new Preferences { LockHash = prefs.LockHash, LockSalt = prefs.LockSalt, LockIterations = prefs.LockIterations };
            string result = LockRules.SetPassword(scratch, current, password);
            return (result, scratch.LockHash, scratch.LockSalt, scratch.LockIterations);
        }).ContinueWith(t =>
        {
            _ = dispatcher.BeginInvoke((Action)(() =>
            {
                if (disposed)
                    return;
                busy = false;
                if (t.Status != TaskStatus.RanToCompletion)
                    passwordResult = "invalid";
                else
                {
                    var (result, hash, salt, iterations) = t.Result;
                    passwordResult = result;
                    if (result == "ok")
                    {
                        Prefs.LockHash = hash;
                        Prefs.LockSalt = salt;
                        Prefs.LockIterations = iterations;
                        Store.Save();
                    }
                }
                Rearm();
                Notify();
            }));
        }, TaskScheduler.Default);
    }

    internal void ApplySettings(bool? enabled, int? idleSeconds)
    {
        if (!locked)
        {
            LockRules.ApplySettings(Prefs, enabled, idleSeconds);
            Store.Save();
            Rearm();
        }
        Notify();
    }

    // Un seul minuteur à usage unique, armé pour le temps restant avant le seuil ; aucun minuteur si l'auto-verrouillage est inactif.
    internal void Rearm()
    {
        idleTimer.Stop();
        if (disposed)
            return;
        var step = LockRules.NextIdle(Prefs.LockEnabled, LockRules.HasPassword(Prefs), Prefs.LockIdleSeconds, locked, IdleMilliseconds(),
            fullscreen());
        int? next = step.DelayMs;
        if (step.Lock && !LockNow())
            next = LockRules.FullscreenRetryMs;
        if (next is int delay)
        {
            idleTimer.Interval = TimeSpan.FromMilliseconds(delay);
            idleTimer.Start();
        }
    }

    void IdleFired()
    {
        idleTimer.Stop();
        Rearm();
    }

    static long IdleMilliseconds()
    {
        var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        return GetLastInputInfo(ref info) ? (uint)(Environment.TickCount - (int)info.Time) : 0;
    }

    void OnProcessExit(object? sender, EventArgs e) => blocker.Stop();

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        idleTimer.Stop();
        StopTimers();
        generation++;
        locked = false;
        blocker.Dispose();
    }
}
