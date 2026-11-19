// C# 12, fichier SensorService.cs, cible Windows 11 / .NET 8, assistant élevé de capteurs : lecture du tube, état pour l'interface
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PanneauxGlass;

// Fonctions pures (testables sans élévation ni matériel).
internal static class SensorLogic
{
    internal const string PipeName = "WinlestiaSensors";

    // Réponse du tube : une décimale en culture invariante ou « null » ; tout le reste (vide, texte, hors plage) vaut null.
    internal static double? ParseAnswer(string? line)
    {
        if (line == null || !double.TryParse(line.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            return null;
        return CpuTempChain.Plausible(v);
    }

    // Code de sortie de l'assistant -> résultat de l'action.
    internal static string ResultFor(int exitCode) => exitCode switch { 0 => "ok", 10 => "reboot", _ => "failed" };

    internal static string State(bool installed, bool driver, bool active)
        => active ? "active" : !installed ? "absent" : !driver ? "noDriver" : "stopped";

    internal static bool IsOutdated(Version? installed, Version? shipped) => installed != null && shipped != null && installed < shipped;
}

// Après 3 échecs de suite, le tube est ignoré 30 s : un assistant absent ne coûte rien.
internal sealed class PipeBackoff(Func<long> clock, int threshold = 3, long pauseMs = 30000)
{
    int fails; long until;
    internal bool ShouldSkip => clock() < until;
    internal void Report(bool ok)
    {
        if (ok)
        {
            fails = 0;
            return;
        }
        if (++fails >= threshold)
        {
            until = clock() + pauseMs;
            fails = 0;
        }
    }
}

// « Valeur récente » avec un indicateur explicite : jamais de date sentinelle qui déborde à la soustraction.
internal sealed class Freshness(Func<long> clock, long windowMs)
{
    readonly object gate = new();
    bool has; long at;
    internal bool IsFresh
    {
        get
        {
            lock (gate)
                return has && clock() - at < windowMs;
        }
    }
    internal void Mark()
    {
        lock (gate)
        {
            has = true;
            at = clock();
        }
    }
}

internal static class SensorPipe
{
    static readonly Freshness lastOk = new(() => Environment.TickCount64, 15000);
    static readonly PipeBackoff backoff = new(() => Environment.TickCount64);
    static readonly object pidGate = new();
    static uint verifiedPid;
    internal static bool RecentlyOk => lastOk.IsFresh;

    internal static string ExpectedServerImage => Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Winlestia", "Sensors", "Winlestia.Sensors.exe"));

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint serverPid);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    // Le serveur du tube doit être l'exécutable installé (contre l'usurpation du nom de tube) ; PID vérifié mis en cache.
    static bool ServerIsTrusted(NamedPipeClientStream client)
    {
        if (!GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(), out uint pid))
            return false;
        lock (pidGate)
            if (verifiedPid == pid && pid != 0)
                return true;
        IntPtr h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == IntPtr.Zero)
            return false;
        try
        {
            var name = new StringBuilder(1024);
            uint size = (uint)name.Capacity;
            if (!QueryFullProcessImageNameW(h, 0, name, ref size))
                return false;
            if (!string.Equals(name.ToString(), ExpectedServerImage, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        finally { CloseHandle(h); }
        lock (pidGate)
            verifiedPid = pid;
        return true;
    }

    // Interrogation brute : null si l'assistant est absent, usurpé, muet ou sans valeur plausible.
    static double? Query()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", SensorLogic.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            client.Connect(200);
            if (!ServerIsTrusted(client))
                return null;
            using var cts = new CancellationTokenSource(500);
            var writer = new StreamWriter(client) { AutoFlush = true, NewLine = "\n" };
            writer.WriteLine("t");
            var reader = new StreamReader(client);
            var v = SensorLogic.ParseAnswer(reader.ReadLineAsync(cts.Token).AsTask().GetAwaiter().GetResult());
            if (v.HasValue)
                lastOk.Mark();
            return v;
        }
        catch (Exception) { return null; }
    }

    // Seul point d'entrée du tube (chaîne de température et sonde d'état), avec temporisation.
    internal static double? Read()
    {
        if (backoff.ShouldSkip)
            return null;
        var v = Query();
        backoff.Report(v.HasValue);
        return v;
    }
}

// État de l'assistant pour l'interface, sans élévation ; vérifications peu coûteuses, mises en cache 10 s.
internal sealed class SensorService(Action changed)
{
    const string SubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
    readonly object gate = new();
    readonly Freshness checkedAt = new(() => Environment.TickCount64, 10000);
    string state = "absent"; bool driver, outdated, available;
    string result = ""; int busy, refreshing;

    static string InstalledExe => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Winlestia",
        "Sensors", "Winlestia.Sensors.exe");
    static string ShippedExe => Path.Combine(AppContext.BaseDirectory, "Sensors", "Winlestia.Sensors.exe");

    static bool DriverPresent()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(SubKey);
                if (key != null)
                    return true;
            }
            catch (Exception) { }
        }
        return false;
    }

    static Version? FileVersion(string path)
    {
        try
        {
            return Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out var v) ? v : null;
        }
        catch (Exception) { return null; }
    }

    void Refresh()
    {
        bool installed = File.Exists(InstalledExe), shipped = File.Exists(ShippedExe);
        bool hasDriver = DriverPresent();
        bool active = installed && (SensorPipe.RecentlyOk || SensorPipe.Read().HasValue);
        bool old = installed && shipped && SensorLogic.IsOutdated(FileVersion(InstalledExe), FileVersion(ShippedExe));
        lock (gate)
        {
            state = SensorLogic.State(installed, hasDriver, active);
            driver = hasDriver;
            outdated = old;
            available = shipped;
        }
        checkedAt.Mark();
    }

    // Jamais sur le fil du pont : renvoie le dernier état connu, relu en tâche de fond (un seul vol à la fois).
    internal object Snapshot()
    {
        if (!checkedAt.IsFresh && Interlocked.CompareExchange(ref refreshing, 1, 0) == 0)
        {
            Task.Run(() =>
            {
                string before, after;
                try
                {
                    lock (gate)
                        before = state + driver + outdated + available;
                    Refresh();
                    lock (gate)
                        after = state + driver + outdated + available;
                    if (before != after)
                        changed();
                }
                catch (Exception) { }
                finally { Volatile.Write(ref refreshing, 0); }
            });
        }
        lock (gate)
            return new
            {
                state,
                driver,
                outdated,
                available,
                result = Volatile.Read(ref busy) == 1 ? "busy" : result
            };
    }

    internal void Install(bool withDriver) => Launch(withDriver ? "install --driver" : "install");
    internal void Remove() => Launch("uninstall");

    // Hors du fil du pont ; un seul lancement à la fois.
    void Launch(string arguments)
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            return;
        Task.Run(async () =>
        {
            string outcome;
            try
            {
                if (!File.Exists(ShippedExe))
                    outcome = "failed";
                else
                {
                    var info = new ProcessStartInfo(ShippedExe, arguments)
                    {
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden,
                        WorkingDirectory = Path.GetDirectoryName(ShippedExe)!
                    };
                    using var p = Process.Start(info);
                    if (p == null)
                        outcome = "failed";
                    else
                    {
                        // Attente bornée (10 min) : « busy » ne peut jamais rester coincé.
                        outcome = p.WaitForExit(600000) ? SensorLogic.ResultFor(p.ExitCode) : "failed";
                    }
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { outcome = "declined"; }
            catch (Exception ex) { Store.Log("Capteurs: " + ex.Message); outcome = "failed"; }
            try
            {
                // Laisse à la tâche lancée par l'assistant le temps de répondre avant de relire l'état.
                await Task.Delay(1500);
                lock (gate)
                    result = outcome;
                Refresh();
            }
            finally
            {
                Volatile.Write(ref busy, 0);
                changed();
            }
        });
        changed();
    }
}
