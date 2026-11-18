// language: C# 12, file: PeripheralBatteryService.cs, target: Windows 11 / .NET 8, batterie des périphériques : cache + sondage lent (60 s)
// tant qu'il est actif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace PanneauxGlass;

// State() ne lit que le dernier instantané en cache. Aucun minuteur tant que SetActive(false) : pas de réveil dock masqué / fonction
// coupée.
internal sealed class PeripheralBatteryService : IDisposable
{
    internal const int PollMilliseconds = 60_000;
    internal const int ScanTimeoutMilliseconds = 40_000;
    readonly Func<Task<List<PeripheralCandidate>>> scan;
    readonly Action? changed;
    readonly object gate = new();
    Timer? timer;
    bool active, scanning, rescan, disposed;
    volatile IReadOnlyList<PeripheralCandidate> candidates = Array.Empty<PeripheralCandidate>();

    public PeripheralBatteryService(Action? changed = null, Func<Task<List<PeripheralCandidate>>>? scan = null)
    {
        this.changed = changed;
        this.scan = scan ?? PeripheralScanner.ScanAsync;
    }

    public IReadOnlyList<PeripheralCandidate> Candidates => candidates;
    public bool Active { get { lock (gate) return active; } }

    public void SetActive(bool on)
    {
        lock (gate)
        {
            if (disposed || on == active)
                return;
            active = on;
            if (!on)
            {
                timer?.Dispose();
                timer = null;
                return;
            }
        }
        RefreshNow();
    }

    // Scan immédiat en arrière-plan ; une demande pendant un scan en relance un seul juste après.
    // force : scan unique même inactif (dock masqué), sans jamais armer le minuteur.
    public void RefreshNow(bool force = false)
    {
        lock (gate)
        {
            if (disposed || (!active && !force))
                return;
            if (scanning)
            {
                rescan = true;
                return;
            }
            scanning = true;
            timer?.Dispose();
            timer = null;
        }
        _ = Task.Run(RunAsync);
    }

    async Task RunAsync()
    {
        while (true)
        {
            try
            {
                var work = scan();
                if (await Task.WhenAny(work, Task.Delay(ScanTimeoutMilliseconds)).ConfigureAwait(false) == work)
                {
                    var result = await work.ConfigureAwait(false);
                    bool differs = !candidates.SequenceEqual(result);
                    candidates = result;
                    if (differs)
                        changed?.Invoke();
                }
            }
            catch (Exception)
            {
            }
            lock (gate)
            {
                if (disposed || !active)
                {
                    scanning = rescan = false;
                    return;
                }
                if (rescan)
                {
                    rescan = false;
                    continue;
                }
                scanning = false;
                timer = new Timer(_ => RefreshNow(), null, PollMilliseconds, Timeout.Infinite);
                return;
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            active = false;
            timer?.Dispose();
            timer = null;
        }
    }
}
