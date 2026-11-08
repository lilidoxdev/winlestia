// language: C# 12, file: QtShellBridge.Cli.cs, target: Windows 11 / .NET 8, commandes CLI, outils, fenêtres et luminosité
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Devices.Radios;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    void RefreshMixer()
    {
        foreach (var app in mixer.Applications.Where(a => !string.IsNullOrEmpty(a.path)))
            LoadIcon(new Entry { Id = app.id, Name = app.name, Target = app.path, IconPath = app.path });
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern bool SystemParametersInfo(
        uint action,
        uint param,
        string value,
        uint flags);
    // Ligne de commande `winlestia` : seules les commandes acceptées par CliCommands.TryParse arrivent ici. Exécuté sur le dispatcher UI.
    // Fond d'écran : la vérification du fichier (parfois un lecteur réseau) et SystemParametersInfo (peut attendre une fenêtre figée)
    // restent hors du thread UI.
    // Fenêtres ouvertes pour la palette et `find windows` : les fenêtres du shell (titres « Panneaux · … ») sont exclues. Appelé hors
    // thread UI.
    static IEnumerable<object> PaletteWindows()
        => TaskbarNative.Applications()
            .Where(w => !w.Title.StartsWith("Panneaux ·", StringComparison.Ordinal))
            .Select(w => (object)new { hwnd = w.Handle.ToInt64(), title = w.Title, exe = Path.GetFileNameWithoutExtension(w.Path) });
    static async Task<(int code, string text)> WifiCli() => (0, await WifiHost.SummaryAsync());
    Task<(int code, string text)> RunCli(CliCommand c) => c.Kind == CliKind.Wifi ? WifiCli() : c.Kind == CliKind.FindWindows ? Task.Run((
        ) =>
        (0, JsonSerializer.Serialize(new
        {
            count = PaletteWindows().Count()
        },
                                     Json))) : c.Kind == CliKind.FindFiles ? RunFindFiles(c.Arg)
                                         : c.Kind is CliKind.Brightness or CliKind.BrightnessSet ? RunBrightness(c)
                                         : c.Kind == CliKind.WallpaperList ? Task.Run(() =>
        (0, JsonSerializer.Serialize(new
        {
            displays = WallpaperApply.List().Select(d => new { d.id, d.name, d.path }).ToArray(),
            animated = WallpaperApply.Animated()
        },
                                     Json))) : c.Kind == CliKind.Wallpaper ? Task.Run(() =>
                                     {
                                         if (!CliCommands.ValidWallpaper(
                                         c.Arg,
                                         File.Exists,
                                         out var why))
                                             return (2, why);
                                         var r = WallpaperApply.Apply(c.Arg, c.Number, LightRendering);
                                         if (r.code == 0)
                                             wallpaper.Notify(100, true);
                                         return r;
                                     }) : RunCliUi(c);
    // Counts only: neither a path nor a name leaves this method. FileSearch bounds the query to 2 s, below the server’s 5 s.
    static async Task<(int code, string text)> RunFindFiles(string term)
    {
        var r = await FileSearch.RunOnceAsync(term, CancellationToken.None);
        return (0, JsonSerializer.Serialize(new
        {
            count = r.Items.Count,
            indexAvailable = r.Status != FileSearch.Unavailable
        }, Json));
    }
    Task<(int code, string text)> RunCliUi(CliCommand c) => dispatcher.InvokeAsync(() =>
    {
        switch (c.Kind)
        {
            case CliKind.Help:
                return (0, CliCommands.HelpText);
            case CliKind.Status:
                {
                    var snap = VirtualDesktops.Current;
                    return (0, JsonSerializer.Serialize(new
                    {
                        version = typeof(QtShellBridge).Assembly.GetName().Version?.ToString(3) ?? "",
                        desktops = snap.Count,
                        currentDesktop = snap.Current + 1,
                        theme = Store.State.Theme,
                        wallpaper = wallpaper.Monitors.FirstOrDefault()?.path ?? "",
                        reducedEffects = LightRendering,
                        quiet = Quiet,
                        autoOrganize = Store.State.AutoTile,
                        activeBorderOnly = Store.State.ActiveBorderOnly
                    }, Json));
                }
            case CliKind.Panel:
                Send(c.Arg == "dashboard" ? "toggle" : c.Arg);
                return (0, "ok");
            case CliKind.Visualizer:
                {
                    var v = bars.Snapshot(false);
                    return (0, JsonSerializer.Serialize(new
                    {
                        running = v.Running,
                        frames = v.Frames,
                        panelOpen = active,
                        wanted = barsVisible
                    }, Json));
                }
            case CliKind.Palette:
                Send("palette");
                return (0, "ok");
            case CliKind.Quiet:
                {
                    if (c.Arg != "" && QuietPolicy.TryParse(c.Arg, out var forcedQuiet))
                    {
                        quietForce = forcedQuiet;
                        ApplyQuiet();
                    }
                    return (0, JsonSerializer.Serialize(new
                    {
                        quiet = Quiet,
                        forced = QuietPolicy.Label(quietForce),
                        detected = QuietDetected
                    }, Json));
                }
            case CliKind.DesktopNext:
                _ = Task.Run(() => VirtualDesktops.SwitchTo(0, 1));
                return (0, "ok");
            case CliKind.DesktopPrev:
                _ = Task.Run(() => VirtualDesktops.SwitchTo(0, -1));
                return (0, "ok");
            case CliKind.DesktopNew:
                _ = Task.Run(() => VirtualDesktops.CreateNew());
                return (0, "ok");
            case CliKind.DesktopGo:
                {
                    int count = VirtualDesktops.Current.Count;
                    if (c.Number > count)
                        return (1, "Desktop " + c.Number + " does not exist (" + count + " available).");
                    int to = c.Number - 1;
                    _ = Task.Run(() => VirtualDesktops.SwitchTo(to));
                    return (0, "ok");
                }
        }
        return (2, "Unknown command.");
    }).Task;

    void RunTool(string id)
    {
        switch (id)
        {
            case "capture":
                Send("capture");
                return;
            case "ocr":
                Send("capture-ocr");
                return;
            case "audio":
                Send("audio");
                return;
            case "clipboard":
                Send("clipboard");
                return;
            case "notifications":
                owner.ToggleNotifications();
                return;
        }
        Send("close");
        _ = dispatcher.BeginInvoke((Action)(() => WithDialog(() =>
        {
            switch (id)
            {
                case "pipette":
                    owner.OpenColorPicker();
                    break;
                case "ruler":
                    owner.OpenRuler();
                    break;
                case "pin":
                    owner.ToggleAlwaysOnTop();
                    break;
                case "largefiles":
                    owner.OpenLargeFiles();
                    break;
                case "profiles":
                    owner.OpenWorkspaces();
                    break;
                case "memo":
                    owner.OpenMemo();
                    break;
                case "help":
                    owner.OpenHelp();
                    break;
            }
        })));
    }
    async Task<string> CaptureExport(JsonElement args)
    {
        string path = Path.GetFullPath(Text(args, "path")), directory = Path.GetFullPath(Path.Combine(cache, "captures"))
            + Path.DirectorySeparatorChar;
        if (!path.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(path) != ".png"
            || !File.Exists(path)
            || new FileInfo(path).Length > 100_000_000)
            throw new InvalidOperationException("Capture invalide.");
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        try
        {
            if (Text(args, "action") == "pin")
            {
                PinWindow.Open(owner, image);
                return "Capture épinglée";
            }
            string text = await OcrService.RecognizeAsync(image);
            if (!string.IsNullOrWhiteSpace(text))
            {
                System.Windows.Clipboard.SetText(text);
                Send("clipboard");
                return "Texte copié dans le presse-papiers";
            }
            Send("capture");
            return "Aucun texte lisible dans cette zone";
        }
        finally { File.Delete(path); }
    }

    // Les lectures DDC/CI sont lentes : Tick ne les attend jamais. SetDisplay : l'OSD passe par le signal existant
    // seulement pour les commandes (CLI), jamais pour le curseur du panneau.
    async Task ReadDisplays()
    {
        readingDisplays = true;
        try
        {
            displays = await MonitorBrightness.ReadDisplaysAsync();
        }
        catch (Exception ex) { Store.Log("Luminosité Qt: " + ex.Message); }
        finally { readingDisplays = false; }
    }
    async Task<bool> SetDisplay(int id, double level, bool osd)
    {
        bool ok = await MonitorBrightness.SetDisplayAsync(id, level);
        _ = dispatcher.BeginInvoke((Action)(() =>
        {
            if (disposed)
                return;
            if (ok)
            {
                displays = MonitorBrightness.Displays();
                if (osd && Store.State.Osd && !active && !Quiet)
                    Send("osd:brightness:" + (int)Math.Round(level));
            }
            else
                nextBrightness = DateTime.MinValue;
        }));
        return ok;
    }
    // Cache d'abord ; nouvelle lecture seulement s'il est vide ou périmé, bornée à 4 s (le serveur CLI répond sous 5 s).
    static readonly TimeSpan DisplayReadTimeout = TimeSpan.FromSeconds(4);
    async Task<(int code, string text)> RunBrightness(CliCommand c)
    {
        try
        {
            if (c.Kind == CliKind.BrightnessSet)
                return await SetDisplay(c.Number, c.Value, true).WaitAsync(DisplayReadTimeout)
                    ? (0, "ok")
                    : (1, "Display " + c.Number + " not found or without DDC/CI.");
            var list = MonitorBrightness.Displays();
            if (list.Count == 0 || MonitorBrightness.CacheAgeMs > BrightnessPolicy.RefreshSeconds * 1000L)
            {
                try
                {
                    list = await MonitorBrightness.ReadDisplaysAsync().WaitAsync(DisplayReadTimeout);
                }
                catch (TimeoutException) { list = MonitorBrightness.Displays(); }
            }
            return (0, JsonSerializer.Serialize(list.Select(d =>
                new
                {
                    id = d.Id,
                    name = d.Name.Length > 0 ? d.Name : "Display " + d.Id,
                    value = (int)Math.Round(d.Value),
                    available = d.Available
                }),
                                                Json));
        }
        catch (TimeoutException) { return (1, "The display did not answer in time."); }
    }

    void WithDialog(Action action)
    {
        DialogOpen = true;
        try
        {
            action();
        }
        finally { DialogOpen = false; }
    }
}
