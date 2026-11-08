// Résout la palette du shell hors du thread UI avec l'arbre de décision propre au backend (Themes.cs Unify) :
// auto (fond d'écran) > personnalisé > shell clair > sombre par défaut. Réutilise le code M3Palette lié, une seule fois au démarrage.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using PanneauxGlass;

namespace WinlestiaFiles;

internal static class PaletteLoader
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SystemParametersInfo(uint action, uint size,
        StringBuilder value, uint flags);

    // La palette par défaut est déjà à l'écran ; ceci la remplace sur place quand le choix enregistré diffère. Pas de scrutation :
    // un changement de fond d'écran ou de thème fenêtre ouverte est capté par une notification à l'étape 6.
    internal static void Start(Dispatcher ui, Action applied)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        Watch(ui, applied);
        Task.Run(() =>
        {
            M3Palette? palette;
            try
            {
                palette = Resolve();
            }
            catch (Exception ex) { App.CrashLog(ex); return; }
            if (palette == null)
                return;
            ui.BeginInvoke(() => { long t = System.Diagnostics.Stopwatch.GetTimestamp(); Perf.Mark("palette apply begin");
                Theme.Current.Set(palette); UI.RepaintAll(); applied(); Perf.Log($"palette UI-thread cost {Perf.Ms(t):F1} ms");
                Perf.Log($"palette applied ({(Store.State.AutoTheme ? "wallpaper" : Store.State.Theme)}) after {Perf.Ms(started):F0} ms");
                });
        });
    }

    static int generation;

    // Mise à jour en direct (message de tube "palette" du backend) : relit preferences-glass.json + le fond d'écran hors du thread UI, puis
    // remplace la palette et repeint les pinceaux de jetons sur place.
    // Contrairement à Start, le "sombre par défaut" est appliqué aussi (un retour depuis le clair doit repeindre). La dernière requête
    // gagne.
    internal static void Refresh(Dispatcher ui, Action applied)
    {
        int mine = Interlocked.Increment(ref generation);
        Task.Run(() =>
        {
            M3Palette palette;
            try
            {
                Store.Reload();
                palette = Resolve() ?? M3Palette.DarkDefault();
            }
            catch (Exception ex) { App.CrashLog(ex); return; }
            ui.BeginInvoke(() => { if (mine != generation) return; Theme.Current.Set(palette); UI.RepaintAll(); applied(); });
        });
    }

    // Palette réellement affichée par le frontend (%LOCALAPPDATA%\Panneaux\palette-shell.json, écrite par frontend.py en mode auto) : elle
    // connaît les surcharges du fond d'écran animé que SPI_GETDESKWALLPAPER ne voit pas.
    internal static string ShellFile => Environment.GetEnvironmentVariable("WINLESTIA_FILES_PALETTE_SHELL") is { Length: > 0 } over ? over
        : Path.Combine(Store.DataDir, "palette-shell.json");

    internal static M3Palette? FromShellFile()
    {
        try
        {
            if (!File.Exists(ShellFile))
                return null;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(ShellFile));
            if (doc.RootElement.TryGetProperty("mode", out var mode) && mode.GetString() != "auto")
                return null;
            if (!doc.RootElement.TryGetProperty("palette", out var p) || p.ValueKind != System.Text.Json.JsonValueKind.Object)
                return null;
            var d = M3Palette.DarkDefault();
            System.Windows.Media.Color Get(string key, System.Windows.Media.Color fallback)
            {
                try
                {
                    if (p.TryGetProperty(key, out var v) && v.GetString() is { Length: 7 } s && s[0] == '#')
                        return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(s);
                }
                catch (Exception)
                {
                    // Couleur absente ou invalide : repli sur le thème par défaut.
                }
                return fallback;
            }
            d.Surface = Get("m3surface", d.Surface);
            d.SurfaceLow = Get("m3surfaceContainerLow", d.SurfaceLow);
            d.SurfaceContainer = Get("m3surfaceContainer", d.SurfaceContainer);
            d.SurfaceHigh = Get("m3surfaceContainerHigh", d.SurfaceHigh);
            d.SurfaceHighest = Get("m3surfaceContainerHighest", d.SurfaceHighest);
            d.OnSurface = Get("m3onSurface", d.OnSurface);
            d.OnSurfaceVariant = Get("m3onSurfaceVariant", d.OnSurfaceVariant);
            d.Primary = Get("m3primary", d.Primary);
            d.OnPrimary = Get("m3onPrimary", d.OnPrimary);
            d.PrimaryContainer = Get("m3primaryContainer", d.PrimaryContainer);
            d.OnPrimaryContainer = Get("m3onPrimaryContainer", d.OnPrimaryContainer);
            d.Secondary = Get("m3secondary", d.Secondary);
            d.SecondaryContainer = Get("m3secondaryContainer", d.SecondaryContainer);
            d.OnSecondaryContainer = Get("m3onSecondaryContainer", d.OnSecondaryContainer);
            d.Outline = Get("m3outline", d.Outline);
            d.OutlineVariant = Get("m3outlineVariant", d.OutlineVariant);
            d.Error = Get("m3error", d.Error);
            d.OnError = Get("m3onError", d.OnError);
            d.Dark = M3Palette.Lum(d.Surface) < .35;
            return d;
        }
        catch { return null; }
    }

    static FileSystemWatcher? watcher; static Timer? debounce;
    // Mise à jour en direct pilotée par événement quand le frontend réécrit la palette partagée (sans scrutation) ; un seul observateur par
    // processus.
    static void Watch(Dispatcher ui, Action applied)
    {
        if (watcher != null)
            return;
        try
        {
            string file = ShellFile, dir = Path.GetDirectoryName(file)!;
            if (!Directory.Exists(dir))
                return;
            watcher = new FileSystemWatcher(dir, Path.GetFileName(file)) {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime };
            FileSystemEventHandler h = (_, _) => { debounce?.Dispose(); debounce = new Timer(_ => Refresh(ui, applied), null, 400,
                Timeout.Infinite); };
            watcher.Created += h;
            watcher.Changed += h;
            watcher.Renamed += (_, _) => h(null, null!);
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { App.CrashLog(ex); }
    }

    static M3Palette? Resolve()
    {
        var prefs = Store.State;
        if (prefs.AutoTheme)
            return FromShellFile() ?? M3Palette.FromWallpaper(WallpaperPath(), "#182025");
        if (prefs.Theme == "custom")
            return M3Palette.Custom(prefs.CustomPalette);
        if (prefs.Theme == "shell")
            return M3Palette.LightDefault();
        return null;
    }

    // SPI_GETDESKWALLPAPER : fichier du fond d'écran actuel du bureau principal (le backend utilise l'écran en 0,0).
    static string WallpaperPath()
    {
        var buffer = new StringBuilder(520);
        return SystemParametersInfo(0x73, (uint)buffer.Capacity, buffer, 0) ? buffer.ToString() : "";
    }
}
