// Resolves the shell palette off the UI thread with the backend's own decision tree (Themes.cs Unify):
// auto (wallpaper) > custom > light shell > dark default. Reuses the linked M3Palette code, one-shot at startup.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using PanneauxGlass;

namespace WinlestiaFiles;

internal static class PaletteLoader {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SystemParametersInfo(uint action, uint size, StringBuilder value, uint flags);

    // Default palette is already on screen; this swaps it in place when the stored choice differs. No polling:
    // a wallpaper or theme change while the window is open is picked up by a notification in step 6.
    internal static void Start(Dispatcher ui, Action applied) {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        Watch(ui, applied);
        Task.Run(() => {
            M3Palette? palette;
            try { palette = Resolve(); } catch (Exception ex) { App.CrashLog(ex); return; }
            if (palette == null) return;
            ui.BeginInvoke(() => { long t = System.Diagnostics.Stopwatch.GetTimestamp(); Perf.Mark("palette apply begin"); Theme.Current.Set(palette); UI.RepaintAll(); applied(); Perf.Log($"palette UI-thread cost {Perf.Ms(t):F1} ms"); Perf.Log($"palette applied ({(Store.State.AutoTheme ? "wallpaper" : Store.State.Theme)}) after {Perf.Ms(started):F0} ms"); });
        });
    }

    static int generation;

    // Live update (backend "palette" pipe message): re-read preferences-glass.json + wallpaper off the UI thread, then swap the palette and repaint the token brushes in place.
    // Unlike Start, "dark default" is applied too (a switch back from light must repaint). The latest request wins.
    internal static void Refresh(Dispatcher ui, Action applied) {
        int mine = Interlocked.Increment(ref generation);
        Task.Run(() => {
            M3Palette palette;
            try { Store.Reload(); palette = Resolve() ?? M3Palette.DarkDefault(); } catch (Exception ex) { App.CrashLog(ex); return; }
            ui.BeginInvoke(() => { if (mine != generation) return; Theme.Current.Set(palette); UI.RepaintAll(); applied(); });
        });
    }

    // Palette the frontend actually shows (Glass-v5/palette-shell.json, written by frontend.py in auto mode): it knows live-wallpaper overrides that SPI_GETDESKWALLPAPER cannot see.
    internal static string ShellFile => Environment.GetEnvironmentVariable("WINLESTIA_FILES_PALETTE_SHELL") is { Length: > 0 } over ? over : Path.Combine(Store.Root, "palette-shell.json");

    internal static M3Palette? FromShellFile() {
        try {
            if (!File.Exists(ShellFile)) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(ShellFile));
            if (doc.RootElement.TryGetProperty("mode", out var mode) && mode.GetString() != "auto") return null;
            if (!doc.RootElement.TryGetProperty("palette", out var p) || p.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            var d = M3Palette.DarkDefault();
            System.Windows.Media.Color Get(string key, System.Windows.Media.Color fallback) {
                try { if (p.TryGetProperty(key, out var v) && v.GetString() is { Length: 7 } s && s[0] == '#') return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(s); } catch { }
                return fallback;
            }
            d.Surface = Get("m3surface", d.Surface); d.SurfaceLow = Get("m3surfaceContainerLow", d.SurfaceLow); d.SurfaceContainer = Get("m3surfaceContainer", d.SurfaceContainer);
            d.SurfaceHigh = Get("m3surfaceContainerHigh", d.SurfaceHigh); d.SurfaceHighest = Get("m3surfaceContainerHighest", d.SurfaceHighest);
            d.OnSurface = Get("m3onSurface", d.OnSurface); d.OnSurfaceVariant = Get("m3onSurfaceVariant", d.OnSurfaceVariant);
            d.Primary = Get("m3primary", d.Primary); d.OnPrimary = Get("m3onPrimary", d.OnPrimary); d.PrimaryContainer = Get("m3primaryContainer", d.PrimaryContainer); d.OnPrimaryContainer = Get("m3onPrimaryContainer", d.OnPrimaryContainer);
            d.Secondary = Get("m3secondary", d.Secondary); d.SecondaryContainer = Get("m3secondaryContainer", d.SecondaryContainer); d.OnSecondaryContainer = Get("m3onSecondaryContainer", d.OnSecondaryContainer);
            d.Outline = Get("m3outline", d.Outline); d.OutlineVariant = Get("m3outlineVariant", d.OutlineVariant); d.Error = Get("m3error", d.Error); d.OnError = Get("m3onError", d.OnError);
            d.Dark = M3Palette.Lum(d.Surface) < .35;
            return d;
        } catch { return null; }
    }

    static FileSystemWatcher? watcher; static Timer? debounce;
    // Event-driven live update when the frontend rewrites the shared palette (no polling); one watcher per process.
    static void Watch(Dispatcher ui, Action applied) {
        if (watcher != null) return;
        try {
            string file = ShellFile, dir = Path.GetDirectoryName(file)!;
            if (!Directory.Exists(dir)) return;
            watcher = new FileSystemWatcher(dir, Path.GetFileName(file)) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime };
            FileSystemEventHandler h = (_, _) => { debounce?.Dispose(); debounce = new Timer(_ => Refresh(ui, applied), null, 400, Timeout.Infinite); };
            watcher.Created += h; watcher.Changed += h; watcher.Renamed += (_, _) => h(null, null!);
            watcher.EnableRaisingEvents = true;
        } catch (Exception ex) { App.CrashLog(ex); }
    }

    static M3Palette? Resolve() {
        var prefs = Store.State;
        if (prefs.AutoTheme) return FromShellFile() ?? M3Palette.FromWallpaper(WallpaperPath(), "#182025");
        if (prefs.Theme == "custom") return M3Palette.Custom(prefs.CustomPalette);
        if (prefs.Theme == "shell") return M3Palette.LightDefault();
        return null;
    }

    // SPI_GETDESKWALLPAPER: current wallpaper file of the primary desktop (the backend uses the monitor at 0,0).
    static string WallpaperPath() {
        var buffer = new StringBuilder(520);
        return SystemParametersInfo(0x73, (uint)buffer.Capacity, buffer, 0) ? buffer.ToString() : "";
    }
}
