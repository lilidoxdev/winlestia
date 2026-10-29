// Opt-in (Preferences.FilesPreload, default false): one deferred, low-priority launch of a hidden resident Winlestia.Files.exe --background.
// No loop: if the process dies it is restarted at most once per backend session. Exit code 75 = deliberate recycle (menu-thread leak): relaunched after DelayMs, unlimited but at most one per minute. On exit a "quit" is sent over the pipe, nothing is killed.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Winlestia.Shared;

namespace PanneauxGlass;

internal static class FilesPreload {
    const int DelayMs = 25000;
    static readonly FilesPreloadPolicy policy = new();
    static Timer? recycleTimer, paletteTimer;
    static volatile bool quitting;
    static Process? child;

    internal static void Start() {
        Task.Run(async () => {
            try {
                await Task.Delay(DelayMs).ConfigureAwait(false);
                Launch();
            } catch (Exception ex) { Store.Log("FilesPreload: " + ex.Message); }
        });
    }

    static void Launch(bool recycle = false) {
        if (!policy.MayLaunch(recycle, Store.State.FilesPreload, quitting)) return;
        if (FilesPipe.TrySend(FilesPipe.Build(Array.Empty<string>(), "", "ping"), 150)) return;   // already resident
        try { if (child != null && !child.HasExited) return; } catch { }   // never two residents at once, whatever path got us here
        string exe = Path.Combine(AppContext.BaseDirectory, "Winlestia.Files.exe");
        if (!File.Exists(exe)) return;
        if (!recycle) policy.Starts++;
        var p = Process.Start(new ProcessStartInfo(exe, "--background") { UseShellExecute = false, CreateNoWindow = true });
        if (p == null) return;
        child = p;
        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) => OnExited(p);
        // BelowNormal only while it builds its hidden window; the resident then idles at Normal so a later open is not starved.
        Task.Run(async () => { await Task.Delay(8000).ConfigureAwait(false); try { if (!p.HasExited) p.PriorityClass = ProcessPriorityClass.Normal; } catch { } });
    }

    static void OnExited(Process p) {
        int code = -1;
        try { code = p.ExitCode; } catch { }
        switch (policy.OnExit(code, Environment.TickCount64, quitting, Store.State.FilesPreload)) {
            case PreloadAction.Crash:
                Task.Run(async () => { await Task.Delay(5000).ConfigureAwait(false); try { Launch(); } catch { } });
                break;
            case PreloadAction.Recycle:
                Store.Log("FilesPreload: resident recycled (exit 75), relaunch in " + DelayMs / 1000 + " s");
                lock (policy) {
                    recycleTimer?.Dispose();
                    recycleTimer = new Timer(_ => { try { Launch(true); } catch (Exception ex) { Store.Log("FilesPreload: " + ex.Message); } }, null, DelayMs, Timeout.Infinite);
                }
                break;
            case PreloadAction.StopTooFast:
                Store.Log("FilesPreload: resident recycled twice within a minute, no relaunch");
                break;
        }
    }

    // Palette / wallpaper / light-dark changed (Theme.Use): tell a running Winlestia Fichiers (resident or windowed) to re-read the preferences. Never launches one.
    // Debounced 600 ms (the preferences file is saved right after the theme switch); one pool-thread pipe probe, no polling.
    internal static void NotifyPalette() {
        if (quitting) return;
        lock (policy) {
            paletteTimer?.Dispose();
            paletteTimer = new Timer(_ => { try { FilesPipe.TrySend(FilesPipe.Build(Array.Empty<string>(), "", "palette"), 150); } catch { } }, null, 600, Timeout.Infinite);
        }
    }

    internal static void Quit() {
        quitting = true; lock (policy) { paletteTimer?.Dispose(); paletteTimer = null; } lock (policy) { recycleTimer?.Dispose(); recycleTimer = null; } try { if (child != null) FilesPipe.TrySend(FilesPipe.Build(Array.Empty<string>(), "", "quit"), 150); } catch { }
    }
}
