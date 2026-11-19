// Mesure sur demande (WINLESTIA_FILES_PERF=1) : lignes Stopwatch dans %LOCALAPPDATA%\Winlestia\Files\perf.log et détecteur de blocage de
// l'interface.
// Rien de tout cela n'existe à l'exécution quand la variable n'est pas définie (Enabled est faux, le minuteur n'est jamais créé).
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace WinlestiaFiles;

internal static class Perf
{
    internal static readonly bool Enabled = Environment.GetEnvironmentVariable("WINLESTIA_FILES_PERF") == "1";
    static readonly object gate = new();
    static string? file;
    internal static StallMonitor? Stall;

    internal static bool FirstFrameDone;
    internal static event Action? BeforeFirstFrame;
    internal static void FirstFrame()
    {
        FirstFrameDone = true;
        var pending = BeforeFirstFrame;
        BeforeFirstFrame = null;
        pending?.Invoke();
    }
    static long processStart;
    // Ligne de jalon : ms depuis la création du processus (seulement si Enabled).
    internal static void Mark(string name)
    {
        if (!Enabled)
            return;
        Mark0();
        Log($"mark {name} @ {Ms(processStart):F0} ms since process start");
    }

    internal static double SinceProcess()
    {
        Mark0();
        return Ms(processStart);
    }
    static void Mark0()
    {
        if (processStart == 0)
            processStart = Stopwatch.GetTimestamp() - (long)((DateTime.Now - Process.GetCurrentProcess().StartTime)
                .TotalMilliseconds * Stopwatch.Frequency / 1000.0);
    }

    internal static double Ms(long since) => (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;

    internal static void Log(string line)
    {
        if (!Enabled)
            return;
        string text = DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + " qpc=" + Stopwatch.GetTimestamp() + Environment.NewLine;
        Task.Run(() =>
        {
            try
            {
                lock (gate)
                {
                    file ??= Path.Combine(FilesSettings.Dir, "perf.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.AppendAllText(file, text);
                }
            }
            catch (Exception)
            {
                // Le journal ne peut pas se journaliser lui-même.
            }
        });
    }
}

// DispatcherTimer de 16 ms en priorité Normal : son retard est le temps pendant lequel le thread UI était occupé par un seul élément.
internal sealed class StallMonitor
{
    readonly DispatcherTimer timer = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(16) };
    long last = Stopwatch.GetTimestamp();
    double max;
    internal StallMonitor()
    {
        timer.Tick += (_, _) => { double gap = Perf.Ms(last); last = Stopwatch.GetTimestamp(); if (gap - 16 > max) max = gap - 16; };
        timer.Start();
    }
    internal void Reset()
    {
        max = 0;
        last = Stopwatch.GetTimestamp();
    }
    internal double Max => max;
    internal void Stop() => timer.Stop();
}
