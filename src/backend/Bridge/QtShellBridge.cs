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
    readonly EventWaitHandle wake;
    readonly Queue<(string name, long epoch)> events = new();
    long frontendEpoch;
    readonly Action networkChanged;

    // Services système lourds (registre, threads audio, Explorateur) : créés par le constructeur de production seulement ;
    // les tests passent par les ports (BridgePorts.cs), jamais par ces champs.
    readonly ExplorerAppearance explorer = null!;
    readonly WindowTiler tiler;
    readonly ScreenFrameReservations frame;
    readonly WindowsWallpaper wallpaper = null!;
    readonly AudioMixerHost mixer = null!;
    bool mixerVisible;
    readonly WindowsSpectrum spectrum = null!;
    readonly MediaController media = null!;
    readonly SystemAudio audio = null!;
    readonly WindowsConnections connections = null!;
    readonly WifiHost wifi = null!;
    bool wifiVisible;
    bool wifiSettingsOpened, bluetoothSettingsOpened;
    readonly PcTelemetry telemetry = null!;
    readonly DictationService dictation;
    readonly FileSearch fileSearch;
    readonly IAudioPort audioPort;
    readonly IWifiPort wifiPort;
    readonly IDesktopPort desktopPort;
    readonly IWallpaperPort wallpaperPort;
    readonly IDisplayPort displayPort;
    readonly IShellPort shellPort;

    CliServer? cli;
    readonly AudioBarsHost bars;
    bool barsVisible;
    // Un Winjet de bureau lit `media.` : la sonde SMTC tourne alors même qu'aucun panneau n'est ouvert.
    bool mediaVisible;
    static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(500);
    readonly DispatcherTimer timer = new() { Interval = TickInterval };

    readonly Dictionary<string, string> icons = new();
    readonly HashSet<string> requested = new();
    readonly Dictionary<string, Entry> dockEntries = new();

    readonly SocialService social;
    readonly global::PanneauxGlass.Winjets.WinjetsService? winjets;
    readonly BookmarksService bookmarks;
    readonly OutsideDismiss dismissal;
    readonly string root, cache;
    Process? frontend;
    readonly FrontendRestartPolicy restartPolicy = new();
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

    // Constructeur d'essai : ni pipe, ni processus Qt, ni hooks ; tout ce qui touche l'OS vient de `parts`.
    internal QtShellBridge(IBridgeOwner coordinator, RunOptions options, BridgeParts parts)
    {
        this.options = options;
        owner = coordinator;
        audioPort = parts.Audio;
        wifiPort = parts.Wifi;
        desktopPort = parts.Desktop;
        wallpaperPort = parts.Wallpaper;
        displayPort = parts.Display;
        shellPort = parts.Shell;
        wake = parts.Wake;
        tiler = parts.Tiler;
        frame = parts.Frame;
        fileSearch = parts.FileSearch;
        bars = parts.Bars;
        social = parts.Social;
        bookmarks = parts.Bookmarks;
        winjets = parts.Winjets;
        dictation = null!;
        dismissal = null!;
        networkChanged = null!;
        root = cache = parts.Directory;
    }

    internal QtShellBridge(IBridgeOwner coordinator, RunOptions options)
    {
        this.options = options;
        var real = new RealSystem(this);
        audioPort = real;
        wifiPort = real;
        desktopPort = real;
        wallpaperPort = real;
        displayPort = real;
        shellPort = real;
        wake = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PanneauxCaelestiaWake");
        explorer = new ExplorerAppearance();
        tiler = new WindowTiler();
        frame = new ScreenFrameReservations();
        wallpaper = new WindowsWallpaper();
        mixer = new AudioMixerHost();
        spectrum = new WindowsSpectrum();
        media = new MediaController();
        audio = new SystemAudio();
        connections = new WindowsConnections();
        wifi = new WifiHost();
        telemetry = new PcTelemetry();
        fileSearch = new FileSearch();
        bars = new AudioBarsHost();
        DialogOpen = Environment.GetEnvironmentVariable("PANNEAUX_VERIFY_MATERIAL84") == "1"
            || Environment.GetEnvironmentVariable("PANNEAUX_VERIFY_THICKNESS85") == "1";
        owner = coordinator;
        winjets = coordinator.Winjets;
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
                                    (volume, muted) =>
                                    {
                                        if (Store.State.Osd && !active && !Quiet)
                                            Send("osd:volume:" + volume + ":" + (
                                        muted ? 1 : 0));
                                    },
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
        StartFrontend();
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
                catch (
            ObjectDisposedException)
                { /* Signal déjà libéré à l'arrêt : plus personne à réveiller. */
                }
            }
        };
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

    // Démarre (ou relance) le processus du frontend. Lève si le processus ne peut pas être créé.
    void StartFrontend()
    {
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
        // Le nouveau frontend n'est « connecté » qu'à sa première réponse sur le tube.
        Volatile.Write(ref firstReply, 0);
        var process = Process.Start(start) ?? throw new InvalidOperationException("Le moteur Qt n'a pas pu être lancé.");
        frontend = process;
        try
        {
            AttachFrontendJob(process);
            Store.Log("Frontend lancé · t+" + (int)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds + " ms");
            // Sans console, une trace Python disparaîtrait : on la garde dans glass.log.
            process.ErrorDataReceived += (_, line) =>
            {
                if (!disposed && !string.IsNullOrWhiteSpace(line.Data))
                    Store.Log("Qt · "
                + line.Data);
            };
            process.BeginErrorReadLine();
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => _ = dispatcher.BeginInvoke((Action)(() => FrontendExited(process, launched)));
        }
        catch
        {
            // Démarrage à moitié fait : jamais deux frontends à la fois, la relance éventuelle repart d'un champ vide.
            frontend = null;
            StopProcess(process);
            throw;
        }
    }

    // Arrête puis libère un processus du frontend ; sans effet (et sans exception) s'il est déjà libéré. L'appelant met son champ à null.
    static void StopProcess(Process? process)
    {
        if (process == null)
            return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(2000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        { /* Déjà terminé ou déjà libéré : rien à arrêter. */
        }
        finally
        {
            Safe.Dispose(process);
        }
    }

    // Arrêt inattendu du frontend : la barre Windows est rétablie aussitôt (bureau utilisable), puis le frontend est relancé avec un
    // délai croissant. Après trop d'arrêts rapprochés, on abandonne : message d'échec et fin de Winlestia.
    void FrontendExited(Process process, DateTime launched)
    {
        if (disposed || !ReferenceEquals(frontend, process))
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
        if (options.IsAutomated)
            return;
        if (Volatile.Read(ref firstReply) == 1)
            restartPolicy.MarkConnected();
        ScheduleFrontendRestart();
    }

    void ScheduleFrontendRestart()
    {
        if (restartPolicy.RecordCrash(DateTime.UtcNow) is not { } delay)
        {
            Store.Log(restartPolicy.EverConnected ? "Le moteur Qt s'arrête trop souvent : abandon des relances."
                : "Le moteur Qt ne s'est jamais connecté : pas de relance.");
            Fail();
            return;
        }
        Store.Log("Relance du moteur Qt dans " + (int)delay.TotalSeconds + " s.");
        var wait = new DispatcherTimer { Interval = delay };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            if (disposed)
                return;
            try
            {
                var previous = frontend;
                frontend = null;
                StopProcess(previous);
                StartFrontend();
            }
            catch (Exception ex)
            {
                Store.Log("Relance du moteur Qt impossible : " + ex.Message);
                ScheduleFrontendRestart();
                return;
            }
            // Barre et icônes ne sont masquées de nouveau que si le frontend a tenu 3 s ET répondu sur le tube : pas de scintillement
            // en boucle, et jamais de bureau nu devant un frontend muet. Sans connexion au bout de 60 s, la barre reste visible.
            var relaunched = frontend;
            int ticks = 0;
            var settle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            settle.Tick += (_, _) =>
            {
                ticks++;
                if (disposed || !ReferenceEquals(frontend, relaunched) || relaunched is not { HasExited: false } || ticks > 60)
                {
                    settle.Stop();
                    return;
                }
                if (ticks < 3 || Volatile.Read(ref firstReply) == 0)
                    return;
                settle.Stop();
                (owner as Coordinator)?.ResumeShell(); // hors IBridgeOwner : les doublures de test n'ont pas à le connaître
            };
            settle.Start();
        };
        wait.Start();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        var running = frontend;
        frontend = null;
        StopProcess(running);
        timer.Stop();
        wifi.Dispose();
        peripheralService?.Dispose();
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
        wake.Dispose();
        cancellation.Dispose();
    }

}
