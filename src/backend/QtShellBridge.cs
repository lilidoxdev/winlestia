// C# 12 / .NET 8 / Windows 11. GPL-3.0-or-later. Windows services for the original Caelestia QML frontend.

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

internal sealed partial class QtShellBridge : IDisposable
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(uint pid);

    [System.Runtime.InteropServices.DllImport("secur32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool GetUserNameEx(int format, System.Text.StringBuilder name, ref uint size);

    static readonly Lazy<string> displayUserName = new(() =>
    {
        try
        {
            string file = Path.Combine(Store.DataDir, "profil.json");
            if (File.Exists(file))
            {
                byte[] raw = File.ReadAllBytes(file);
                if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
                    raw = raw[3..];
                string text;
                try
                {
                    text = new UTF8Encoding(false, true).GetString(raw);
                }
                catch
                {
                    text = CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(raw);
                }
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                if (
                    doc.RootElement.TryGetProperty("displayName", out var dn)
                    && dn.ValueKind == System.Text.Json.JsonValueKind.String
                    && dn.GetString() is { } s
                    && !string.IsNullOrWhiteSpace(s)
                )
                    return s.Trim();
            }
        }
        catch { }
        try
        {
            var name = new StringBuilder(256);
            uint size = 256;
            if (GetUserNameEx(3, name, ref size) && name.Length > 0)
                return name.ToString();
        }
        catch { }
        return Environment.UserName;
    });

    uint frontendWindowPid;
    readonly Coordinator owner;
    readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

    readonly CancellationTokenSource cancellation = new();
    readonly EventWaitHandle wake = new(false, EventResetMode.AutoReset, @"Local\PanneauxCaelestiaWake");
    readonly Queue<(string name, long epoch)> events = new();
    long frontendEpoch;
    readonly Action networkChanged;

    readonly ExplorerAppearance explorer = new();
    readonly WindowTiler tiler = new();
    readonly ScreenFrameReservations frame = new();
    readonly WindowsWallpaper wallpaper = new();
    readonly AudioMixerHost mixer = new();
    bool mixerVisible;
    readonly WindowsSpectrum spectrum = new();
    readonly MediaController media = new();
    readonly SystemAudio audio = new();
    readonly WindowsConnections connections = new();
    readonly WifiHost wifi = new();
    bool wifiVisible;
    bool wifiSettingsOpened,
        bluetoothSettingsOpened;
    readonly PcTelemetry telemetry = new();
    readonly DictationService dictation;
    readonly FileSearch fileSearch = new();

    CliServer? cli;
    readonly AudioBarsHost bars = new();
    bool barsVisible;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    readonly Dictionary<string, string> icons = new();
    readonly HashSet<string> requested = new();
    readonly Dictionary<string, Entry> dockEntries = new();

    readonly SocialService social;
    readonly BookmarksService bookmarks;
    readonly OutsideDismiss dismissal;
    readonly string root,
        cache;
    Process? frontend;
    NamedPipeServerStream? pipe;

    OsdWatcher? osdWatcher;
    PcReading? reading;
    MediaSnapshot? player;
    AudioSnapshot? sound;
    IReadOnlyList<DisplayBrightness> displays = Array.Empty<DisplayBrightness>();
    ConnectionSnapshot? connection;

    DateTime nextConnections,
        nextBrightness,
        nextTasks,
        lastReply;
    object[] tasks = Array.Empty<object>();

    bool displaysOpen,
        readingDisplays,
        dockVisible,
        active,
        controls,
        pageMusic,
        busy,
        disposed,
        startingMedia;
    int page,
        iconRevision;
    string artworkKey = "",
        artworkPath = "";

    static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    internal void DisplaysChanged() => frame.DisplaysChanged();

    internal bool Visible => active;
    internal bool DialogOpen;
    internal bool CapturingShortcut { get; private set; }

    internal void CaptureHotkey(IntPtr data)
    {
        uint value = unchecked((uint)data.ToInt64());
        Send("shortcut-key:" + (value & 15) + ":" + ((value >> 16) & 255));
    }

    internal static bool KeepEvent(string name, long queuedEpoch, long currentEpoch) =>
        name != "close" || queuedEpoch == currentEpoch;

    static string FrontendLog =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Panneaux",
            "Caelestia",
            "frontend.log"
        );

    // Un moteur mort laissait un bureau sans barre et sans explication : on le dit, puis on rend la main.
    static void Fail() =>
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            System.Windows.MessageBox.Show(
                "Winlestia n'a pas pu démarrer son interface.\n\n"
                    + "Le détail de l'incident est dans :\n"
                    + FrontendLog
                    + "\n\n"
                    + "La barre des tâches Windows a été rétablie. Réinstalle Winlestia si cela se reproduit.",
                "Winlestia",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error
            );
            System.Windows.Application.Current.Shutdown();
        });

    internal QtShellBridge(Coordinator coordinator)
    {
        DialogOpen =
            Program.QtVerifyTools
            || Environment.GetEnvironmentVariable("PANNEAUX_VERIFY_MATERIAL84") == "1"
            || Environment.GetEnvironmentVariable("PANNEAUX_VERIFY_THICKNESS85") == "1";
        owner = coordinator;
        root = Program.QtRoot ?? Path.GetFullPath(Path.Combine(Store.Root, "..", "Caelestia-Windows"));
        cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Panneaux",
            "Caelestia"
        );
        Directory.CreateDirectory(Path.Combine(cache, "icons"));
        try
        {
            foreach (
                string stale in Directory
                    .GetFiles(Path.Combine(cache, "icons"), "*.fb.png")
                    .Concat(Directory.GetFiles(Path.Combine(cache, "icons"), "win-*.png"))
            )
                File.Delete(stale);
        }
        catch { }

        dismissal = new OutsideDismiss(
            () => Send("close"),
            () => active && !DialogOpen,
            h =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                return frontend != null && (pid == frontend.Id || pid == frontendWindowPid);
            }
        );

        dictation = new DictationService(dispatcher, Send);
        osdWatcher = new OsdWatcher(
            dispatcher,
            (volume, muted) =>
            {
                if (Store.State.Osd && !active && !Quiet)
                    Send("osd:volume:" + volume + ":" + (muted ? 1 : 0));
            },
            line =>
            {
                if (Store.State.Osd && !active && !Quiet)
                    Send(line);
            }
        );
        dictation.KeyCaptured = vk =>
        {
            if (vk is > 0 and < 255)
                Store.State.DictationKey = vk;
            dictation.Apply(Store.State.DictationEnabled, Store.State.DictationKey);
            Store.Save();
        };
        dictation.Apply(Store.State.DictationEnabled, Store.State.DictationKey);
        _ = Serve();
        cli = new CliServer(RunCli);
        var launched = DateTime.UtcNow;
        var start = new ProcessStartInfo(Path.Combine(root, ".venv", "Scripts", "pythonw.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root,
            RedirectStandardError = true,
        };
        // Le PySide6 livré est le seul valable : un site-packages utilisateur ne doit pas s'intercaler.
        start.Environment["PYTHONNOUSERSITE"] = "1";
        start.ArgumentList.Add(Path.Combine(root, "frontend.py"));
        start.ArgumentList.Add("--backend-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        if (Program.QtVerifyTools)
            start.ArgumentList.Add("--verify-tools82");
        else if (Program.QtVerify)
            start.ArgumentList.Add("--verify");
        else if (Program.QtReview)
            start.ArgumentList.Add("--preview");
        frontend = Process.Start(start) ?? throw new InvalidOperationException("Le moteur Qt n'a pas pu être lancé.");
        Store.Log(
            "Frontend lancé · t+"
                + (int)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds
                + " ms"
        );
        // Sans console, une trace Python disparaîtrait : on la garde dans glass.log.
        frontend.ErrorDataReceived += (_, line) =>
        {
            if (!disposed && !string.IsNullOrWhiteSpace(line.Data))
                Store.Log("Qt · " + line.Data);
        };
        frontend.BeginErrorReadLine();
        frontend.EnableRaisingEvents = true;
        frontend.Exited += (_, _) =>
            _ = dispatcher.BeginInvoke(
                (Action)(
                    () =>
                    {
                        if (disposed)
                            return;
                        bool crashed = !active && (DateTime.UtcNow - launched).TotalSeconds < 10;
                        Store.Log(
                            crashed
                                ? "Le moteur Qt n'a pas démarré. La barre Windows est rétablie."
                                : "Le moteur Qt s'est arrêté. La barre Windows est rétablie."
                        );
                        dockPreview.Close();
                        tiler.SetEnabled(false);
                        frame.Disable();
                        owner.SuspendTaskbar();
                        owner.RestoreDesktopIcons();
                        active = false;
                        CapturingShortcut = false;
                        dismissal.SetActive(false);
                        tiler.SetEnabled(false);
                        StopSampling();
                        if (crashed && !Program.QtVerify && !Program.QtReview && !Program.QtVerifyTools)
                            Fail();
                    }
                )
            );
        timer.Tick += Tick;
        timer.Start();
        social = new SocialService(dispatcher, () => Send("social"));
        _ = social.Start();
        bookmarks = new BookmarksService(dispatcher, () => Send("bookmarks"));
        bookmarks.Start();
        networkChanged = () =>
        {
            if (!disposed)
            {
                try
                {
                    wake.Set();
                }
                catch { }
            }
        };
        NetworkKindWatcher.Changed += networkChanged;
        NetworkKindWatcher.Start();
        VirtualDesktops.Changed += networkChanged;
        VirtualDesktops.Start();
        FullscreenWatcher.IgnoredPids = () =>
            new[] { (uint)(Volatile.Read(ref frontend)?.Id ?? 0), Volatile.Read(ref frontendWindowPid) };
        FullscreenWatcher.Changed += FullscreenChanged;
        FullscreenWatcher.Start();

        foreach (var entry in Store.HomeFavorites().Take(16))
            LoadIcon(entry);
    }

    int firstReply;

    internal void Send(string value)
    {
        if (value == "palette")
            dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)owner.RefreshCatalogueIfStale);
        if (
            frontendWindowPid > 0
            && (
                value
                    is "toggle"
                        or "access"
                        or "media"
                        or "performance"
                        or "controls"
                        or "palette"
                        or "settings"
                        or "settings:social"
                        or "settings:bookmarks"
                        or "clipboard"
                        or "audio"
                        or "microphone"
                        or "tools"
                        or "capture"
                        or "wallpaper-switcher"
                || value.StartsWith("workspace:")
            )
        )
            AllowSetForegroundWindow(frontendWindowPid);
        if (events.Count < 32)
            events.Enqueue((value, frontendEpoch));
        wake.Set();
    }

    async Task Serve()
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    "PanneauxCaelestia",
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly
                );
                pipe = server;
                await server.WaitForConnectionAsync(cancellation.Token);
                using var reader = new StreamReader(server, new UTF8Encoding(false), false, 65536, true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 65536, true) { AutoFlush = true };
                while (server.IsConnected && !cancellation.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(cancellation.Token);
                    if (line == null || line.Length > 131072)
                        break;
                    using var request = JsonDocument.Parse(line);
                    var element = request.RootElement.Clone();
                    var operation = await dispatcher.InvokeAsync(() => Handle(element));
                    string reply = await operation;
                    await writer.WriteLineAsync(reply.AsMemory(), cancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!disposed)
                    Store.Log("Qt IPC: " + ex.Message);
            }
            finally
            {
                pipe = null;
                if (!disposed)
                    _ = dispatcher.BeginInvoke(
                        (Action)(
                            () =>
                            {
                                dockPreview.Close();
                                active = false;
                                CapturingShortcut = false;
                                dismissal.SetActive(false);
                                tiler.SetEnabled(false);
                                StopSampling();
                            }
                        )
                    );
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        timer.Stop();
        wifi.Dispose();
        bars.Dispose();
        dictation.Dispose();
        osdWatcher?.Dispose();
        dockPreview.Dispose();
        social.Dispose();
        bookmarks.Dispose();
        explorer.Dispose();
        dismissal.Dispose();
        cli?.Dispose();
        fileSearch.Dispose();
        cancellation.Cancel();
        pipe?.Dispose();
        tiler.Dispose();
        frame.Dispose();
        wallpaper.Dispose();
        mixer.Dispose();
        spectrum.Dispose();
        media.Dispose();
        telemetry.Dispose();
        NetworkKindWatcher.Changed -= networkChanged;
        NetworkKindWatcher.Stop();
        VirtualDesktops.Changed -= networkChanged;
        VirtualDesktops.Stop();
        FullscreenWatcher.Changed -= FullscreenChanged;
        FullscreenWatcher.Stop();
        audio.Dispose();
        if (frontend is { HasExited: false })
        {
            try
            {
                frontend.Kill();
                frontend.WaitForExit(2000);
            }
            catch { }
        }
        frontend?.Dispose();
        wake.Dispose();
        cancellation.Dispose();
    }
}
