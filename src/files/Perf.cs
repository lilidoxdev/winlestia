// Opt-in measurement (WINLESTIA_FILES_PERF=1): Stopwatch lines in %LOCALAPPDATA%\Winlestia\Files\perf.log and a UI stall detector.
// Nothing here exists at runtime when the variable is unset (Enabled is false, the timer is never created).
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace WinlestiaFiles;

internal static class Perf {
    internal static readonly bool Enabled = Environment.GetEnvironmentVariable("WINLESTIA_FILES_PERF") == "1";
    static readonly object gate = new();
    static string? file;
    internal static StallMonitor? Stall;

    internal static bool FirstFrameDone;
    internal static event Action? BeforeFirstFrame;
    internal static void FirstFrame() { FirstFrameDone = true; var pending = BeforeFirstFrame; BeforeFirstFrame = null; pending?.Invoke(); }
    static long processStart;
    // Milestone line: ms since the process was created (only when Enabled).
    internal static void Mark(string name) {
        if (!Enabled) return;
        Mark0();
        Log($"mark {name} @ {Ms(processStart):F0} ms since process start");
    }

    internal static double SinceProcess() { Mark0(); return Ms(processStart); }
    static void Mark0() { if (processStart == 0) processStart = Stopwatch.GetTimestamp() - (long)((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds * Stopwatch.Frequency / 1000.0); }

    internal static double Ms(long since) => (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;

    internal static void Log(string line) {
        if (!Enabled) return;
        string text = DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + " qpc=" + Stopwatch.GetTimestamp() + Environment.NewLine;
        Task.Run(() => {
            try {
                lock (gate) {
                    file ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winlestia", "Files", "perf.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                    File.AppendAllText(file, text);
                }
            } catch { }
        });
    }
}

// 16 ms DispatcherTimer at Normal priority: its lateness is the time the UI thread was busy with one item.
internal sealed class StallMonitor {
    readonly DispatcherTimer timer = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(16) };
    long last = Stopwatch.GetTimestamp();
    double max;
    internal StallMonitor() { timer.Tick += (_, _) => { double gap = Perf.Ms(last); last = Stopwatch.GetTimestamp(); if (gap - 16 > max) max = gap - 16; }; timer.Start(); }
    internal void Reset() { max = 0; last = Stopwatch.GetTimestamp(); }
    internal double Max => max;
    internal void Stop() => timer.Stop();
}
