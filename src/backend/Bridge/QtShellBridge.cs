// C# 12 / .NET 8 / Windows 11. GPL-3.0-or-later. Services Windows du frontend QML Caelestia d'origine.
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

    uint frontendWindowPid;
    IBridgeOwner owner;
    readonly RunOptions options;
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
    bool wifiSettingsOpened, bluetoothSettingsOpened;
    readonly PcTelemetry telemetry = new();
    readonly DictationService dictation;
    readonly FileSearch fileSearch = new();

    CliServer? cli;
    readonly AudioBarsHost bars = new();
    bool barsVisible;
    static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(500);
    readonly DispatcherTimer timer = new() { Interval = TickInterval };

    readonly Dictionary<string, string> icons = new();
    readonly HashSet<string> requested = new();
    readonly Dictionary<string, Entry> dockEntries = new();

    readonly SocialService social;
    readonly BookmarksService bookmarks;
    readonly OutsideDismiss dismissal;
    readonly string root, cache;
    Process? frontend;
    NamedPipeServerStream? pipe;
    // Version du protocole JSON lignes (docs/BRIDGE.md). Le client l'annonce dans args.protocol de chaque requête ;
    // une divergence est journalisée une fois par connexion, sans refuser la connexion.
    internal const int ProtocolVersion = 1;

    OsdWatcher? osdWatcher;
    PcReading? reading;
    MediaSnapshot? player;
    AudioSnapshot? sound;
    IReadOnlyList<DisplayBrightness> displays = Array.Empty<DisplayBrightness>();
    ConnectionSnapshot? connection;

    DateTime nextConnections, nextBrightness, nextTasks, lastReply;
    object[] tasks = Array.Empty<object>();

    bool displaysOpen, readingDisplays, dockVisible, active, controls, pageMusic, busy, disposed, startingMedia;
    int page, iconRevision;
    string artworkKey = "", artworkPath = "";

    static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    internal void DisplaysChanged() => frame.DisplaysChanged();
    internal bool Visible => active;
    internal bool DialogOpen;
    internal bool CapturingShortcut
    {
        get; private set;
    }
    internal void CaptureHotkey(IntPtr data)
    {
        uint value = unchecked((uint)data.ToInt64());
        Send("shortcut-key:" + (value & 15) + ":" + ((value >> 16) & 255));
    }

    internal static bool KeepEvent(string name, long queuedEpoch, long currentEpoch) => name != "close" || queuedEpoch == currentEpoch;

    internal QtShellBridge(IBridgeOwner coordinator, RunOptions options)
    {
        this.options = options;
        DialogOpen = Environment.GetEnvironmentVariable("PANNEAUX_VERIFY_MATERIAL84") == "1"
            || Environment.GetEnvironmentVariable("PANNEAUX_VERIFY_THICKNESS85") == "1";
        owner = coordinator;
        root = Program.QtRoot ?? Path.GetFullPath(Path.Combine(Store.Root, "..", "Caelestia-Windows"));
        cache = Path.Combine(Store.DataDir, "Caelestia");
        Directory.CreateDirectory(Path.Combine(cache, "icons"));
        Safe.Try(() =>
        {
            foreach (string stale in Directory.GetFiles(Path.Combine(cache, "icons"), "*.fb.png").Concat(Directory.GetFiles(Path.Combine(
                cache, "icons"), "win-*.png")))
                File.Delete(stale);
        });

        dismissal = new OutsideDismiss(() => Send("close"), () => active && !DialogOpen, h =>
        {
            GetWindowThreadProcessId(h, out uint pid);
            return frontend != null && (pid == frontend.Id || pid == frontendWindowPid);
        });

        dictation = new DictationService(dispatcher, Send);
        osdWatcher = new OsdWatcher(dispatcher,
                                    (volume, muted) => { if (Store.State.Osd && !active && !Quiet) Send("osd:volume:" + volume + ":" + (
                                        muted ? 1 : 0)); },
                                    line => { if (Store.State.Osd && !active && !Quiet) Send(line); });
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
        var start =
            new ProcessStartInfo(Path.Combine(root, ".venv", "Scripts", "pythonw.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root,
                RedirectStandardError = true
            };
        // Le PySide6 livré est le seul valable : un site-packages utilisateur ne doit pas s'intercaler.
        start.Environment["PYTHONNOUSERSITE"] = "1";
        start.ArgumentList.Add(Path.Combine(root, "frontend.py"));
        start.ArgumentList.Add("--backend-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        if (options.Mode == LaunchMode.Verify)
            start.ArgumentList.Add("--verify");
        else if (options.Mode == LaunchMode.Review)
            start.ArgumentList.Add("--preview");
        frontend = Process.Start(start) ?? throw new InvalidOperationException("Le moteur Qt n'a pas pu être lancé.");
        AttachFrontendJob(frontend);
        Store.Log("Frontend lancé · t+" + (int)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds + " ms");
        // Sans console, une trace Python disparaîtrait : on la garde dans glass.log.
        frontend.ErrorDataReceived += (_, line) => { if (!disposed && !string.IsNullOrWhiteSpace(line.Data)) Store.Log("Qt · "
            + line.Data); };
        frontend.BeginErrorReadLine();
        frontend.EnableRaisingEvents = true;
        frontend.Exited += (_, _) => _ = dispatcher.BeginInvoke((Action)(() =>
        {
            if (disposed)
                return;
            bool crashed = !active && (DateTime.UtcNow - launched).TotalSeconds < 10;
            Store.Log((crashed ? "Le moteur Qt n'a pas démarré." : "Le moteur Qt s'est arrêté.") + " La barre Windows est rétablie.");
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
            if (crashed && !options.IsAutomated)
                Fail();
        }));
        timer.Tick += Tick;
        timer.Start();
        social = new SocialService(dispatcher, () => Send("social"));
        _ = social.Start();
        bookmarks = new BookmarksService(dispatcher, () => Send("bookmarks"));
        bookmarks.Start();
        networkChanged = () => { if (!disposed) { try { wake.Set(); } catch (
            ObjectDisposedException) { /* Signal déjà libéré à l'arrêt : plus personne à réveiller. */ } } };
        NetworkKindWatcher.Changed += networkChanged;
        NetworkKindWatcher.Start();
        VirtualDesktops.Changed += networkChanged;
        VirtualDesktops.Start();
        FullscreenWatcher.IgnoredPids = () => new[] { (uint)(Volatile.Read(ref frontend)?.Id ?? 0), Volatile.Read(ref frontendWindowPid) };
        FullscreenWatcher.Changed += FullscreenChanged;
        FullscreenWatcher.Start();

        foreach (var entry in Store.HomeFavorites().Take(16))
            LoadIcon(entry);

    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (frontend is { HasExited: false })
        {
            Safe.Try(() =>
            {
                frontend.Kill();
                frontend.WaitForExit(2000);

            });
        }
        timer.Stop();
        wifi.Dispose();
        bars.Dispose();
        try
        {
            dictation.Dispose();
        }
        catch (Exception ex) { Store.Log("Dictée (fermeture): " + ex.GetType().Name + ": " + ex.Message); }
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
        frontend?.Dispose();
        wake.Dispose();
        cancellation.Dispose();
    }

}
