// `winlestia` command line handlers and the DDC/CI brightness reads they use.

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

namespace PanneauxGlass.Shell;

internal sealed partial class QtShellBridge
{
    [System.Runtime.InteropServices.DllImport(
        "user32.dll",
        CharSet = System.Runtime.InteropServices.CharSet.Unicode,
        SetLastError = true
    )]
    static extern bool SystemParametersInfo(uint action, uint param, string value, uint flags);

    // `winlestia` command line: only the commands accepted by CliCommands.TryParse reach this point. Runs on the UI
    // dispatcher.
    // Wallpaper: the file check (possibly a network drive) and SystemParametersInfo (may wait on a hung window) stay
    // off the UI thread.
    // Open windows for the palette and `find windows`: the shell’s own windows (titles « Panneaux · … ») are left out.
    // Called off the UI thread.
    static IEnumerable<object> PaletteWindows() =>
        TaskbarNative
            .Applications()
            .Where(w => !w.Title.StartsWith("Panneaux ·", StringComparison.Ordinal))
            .Select(w =>
                (object)
                    new
                    {
                        hwnd = w.Handle.ToInt64(),
                        title = w.Title,
                        exe = Path.GetFileNameWithoutExtension(w.Path),
                    }
            );

    static async Task<(int code, string text)> WifiCli() => (0, await WifiHost.SummaryAsync());

    Task<(int code, string text)> RunCli(CliCommand c) =>
        c.Kind == CliKind.Wifi ? WifiCli()
        : c.Kind == CliKind.FindWindows
            ? Task.Run(() => (0, JsonSerializer.Serialize(new { count = PaletteWindows().Count() }, Json)))
        : c.Kind == CliKind.FindFiles ? RunFindFiles(c.Arg)
        : c.Kind is CliKind.Brightness or CliKind.BrightnessSet ? RunBrightness(c)
        : c.Kind == CliKind.WallpaperList
            ? Task.Run(() =>
                (
                    0,
                    JsonSerializer.Serialize(
                        new
                        {
                            displays = WallpaperApply
                                .List()
                                .Select(d => new
                                {
                                    d.id,
                                    d.name,
                                    d.path,
                                })
                                .ToArray(),
                            animated = WallpaperApply.Animated(),
                        },
                        Json
                    )
                )
            )
        : c.Kind == CliKind.Wallpaper
            ? Task.Run(() =>
            {
                if (!CliCommands.ValidWallpaper(c.Arg, File.Exists, out var why))
                    return (2, why);
                var r = WallpaperApply.Apply(c.Arg, c.Number, LightRendering);
                if (r.code == 0)
                    wallpaper.Notify(100, true);
                return r;
            })
        : RunCliUi(c);

    // Counts only: neither a path nor a name leaves this method. FileSearch bounds the query to 2 s, below the server’s
    // 5 s.
    static async Task<(int code, string text)> RunFindFiles(string term)
    {
        var r = await FileSearch.RunOnceAsync(term, CancellationToken.None);
        return (
            0,
            JsonSerializer.Serialize(
                new { count = r.Items.Count, indexAvailable = r.Status != FileSearch.Unavailable },
                Json
            )
        );
    }

    Task<(int code, string text)> RunCliUi(CliCommand c) =>
        dispatcher
            .InvokeAsync(() =>
            {
                switch (c.Kind)
                {
                    case CliKind.Help:
                        return (0, CliCommands.HelpText);
                    case CliKind.Status:
                    {
                        var snap = VirtualDesktops.Current;
                        return (
                            0,
                            JsonSerializer.Serialize(
                                new
                                {
                                    version = typeof(QtShellBridge).Assembly.GetName().Version?.ToString(3) ?? "",
                                    desktops = snap.Count,
                                    currentDesktop = snap.Current + 1,
                                    theme = Store.State.Theme,
                                    wallpaper = wallpaper.Monitors.FirstOrDefault()?.path ?? "",
                                    reducedEffects = LightRendering,
                                    quiet = Quiet,
                                    autoOrganize = Store.State.AutoTile,
                                    activeBorderOnly = Store.State.ActiveBorderOnly,
                                },
                                Json
                            )
                        );
                    }
                    case CliKind.Panel:
                        Send(c.Arg == "dashboard" ? "toggle" : c.Arg);
                        return (0, "ok");
                    case CliKind.Visualizer:
                    {
                        var v = bars.Snapshot(false);
                        return (
                            0,
                            JsonSerializer.Serialize(
                                new
                                {
                                    running = v.Running,
                                    frames = v.Frames,
                                    panelOpen = active,
                                    wanted = barsVisible,
                                },
                                Json
                            )
                        );
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
                        return (
                            0,
                            JsonSerializer.Serialize(
                                new
                                {
                                    quiet = Quiet,
                                    forced = QuietPolicy.Label(quietForce),
                                    detected = QuietDetected,
                                },
                                Json
                            )
                        );
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
            })
            .Task;

    // DDC/CI reads are slow: never awaited by Tick. SetDisplay: the OSD goes through the existing signal only for
    // commands (CLI), never for the panel slider.
    async Task ReadDisplays()
    {
        readingDisplays = true;
        try
        {
            displays = await MonitorBrightness.ReadDisplaysAsync();
        }
        catch (Exception ex)
        {
            Store.Log("Luminosité Qt: " + ex.Message);
        }
        finally
        {
            readingDisplays = false;
        }
    }

    async Task<bool> SetDisplay(int id, double level, bool osd)
    {
        bool ok = await MonitorBrightness.SetDisplayAsync(id, level);
        _ = dispatcher.BeginInvoke(
            (Action)(
                () =>
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
                }
            )
        );
        return ok;
    }

    // Cache first; a fresh read only when it is empty or older than the refresh period, bounded to 4 s (the CLI server
    // answers within 5 s).
    async Task<(int code, string text)> RunBrightness(CliCommand c)
    {
        try
        {
            if (c.Kind == CliKind.BrightnessSet)
                return await SetDisplay(c.Number, c.Value, true).WaitAsync(TimeSpan.FromSeconds(4))
                    ? (0, "ok")
                    : (1, "Display " + c.Number + " not found or without DDC/CI.");
            var list = MonitorBrightness.Displays();
            if (list.Count == 0 || MonitorBrightness.CacheAgeMs > BrightnessPolicy.RefreshSeconds * 1000L)
            {
                try
                {
                    list = await MonitorBrightness.ReadDisplaysAsync().WaitAsync(TimeSpan.FromSeconds(4));
                }
                catch (TimeoutException)
                {
                    list = MonitorBrightness.Displays();
                }
            }
            return (
                0,
                JsonSerializer.Serialize(
                    list.Select(d => new
                    {
                        id = d.Id,
                        name = d.Name.Length > 0 ? d.Name : "Display " + d.Id,
                        value = (int)Math.Round(d.Value),
                        available = d.Available,
                    }),
                    Json
                )
            );
        }
        catch (TimeoutException)
        {
            return (1, "The display did not answer in time.");
        }
    }
}
