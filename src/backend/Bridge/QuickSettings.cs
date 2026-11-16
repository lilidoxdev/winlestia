// C# 12, fichier QuickSettings.cs, cible Windows 11 / .NET 8, réglages rapides : radios, mode avion, micro, maintien éveillé
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using Windows.Devices.Radios;
namespace PanneauxGlass;

// L'état est un cache : l'instantané du pont ne fait jamais d'appel WinRT. Les radios alimentent le cache par événement.
internal sealed class QuickSettings
{
    internal static QuickSettings Shared { get; } = new();

    readonly object gate = new();
    List<Radio> radios = new();
    bool radiosKnown, radiosFailed, started;
    List<string>? airplaneMemory;
    bool micAvailable, micMuted;
    MMDevice? micDevice;
    // SetThreadExecutionState est propre au thread : un thread dédié, vivant tant que le maintien est actif.
    Thread? awakeThread;
    ManualResetEventSlim? awakeRelease;

    QuickSettings() => AppDomain.CurrentDomain.ProcessExit += (_, _) => SetKeepAwake(false);

    // Énumération initiale, une seule fois, hors du thread du pont.
    internal void Start()
    {
        lock (gate)
        {
            if (started)
                return;
            started = true;
        }
        _ = Task.Run(async () =>
        {
            await RefreshRadios();
            RefreshMic();
            Changed?.Invoke();
        });
    }

    internal object Snapshot()
    {
        Start();
        lock (gate)
        {
            bool Has(RadioKind k) => radios.Any(r => r.Kind == k);
            bool On(RadioKind k) => radios.Any(r => r.Kind == k && r.State == RadioState.On);
            bool radioOk = radiosKnown && !radiosFailed;
            bool anyRadio = radioOk && radios.Count > 0;
            return new
            {
                wifi = new { available = radioOk && Has(RadioKind.WiFi), on = On(RadioKind.WiFi) },
                bluetooth = new { available = radioOk && Has(RadioKind.Bluetooth), on = On(RadioKind.Bluetooth) },
                airplane = new { available = anyRadio, on = anyRadio && radios.All(r => r.State != RadioState.On) },
                micMute = new { available = micAvailable, on = micAvailable && micMuted },
                keepAwake = new { available = true, on = awakeThread != null }
            };
        }
    }

    async Task RefreshRadios()
    {
        try
        {
            var access = await Radio.RequestAccessAsync();
            if (access != RadioAccessStatus.Allowed)
                throw new UnauthorizedAccessException("accès aux radios : " + access);
            var found = (await Radio.GetRadiosAsync()).ToList();
            lock (gate)
            {
                foreach (var old in radios)
                    old.StateChanged -= OnRadioChanged;
                radios = found;
                foreach (var r in radios)
                    r.StateChanged += OnRadioChanged;
                radiosKnown = true;
                radiosFailed = false;
            }
        }
        catch (Exception ex)
        {
            lock (gate)
            {
                radiosKnown = true;
                radiosFailed = true;
            }
            Store.Log("Réglages rapides: radios indisponibles: " + ex.Message);
        }
    }

    // L'événement met à jour l'état du radio lui-même (r.State) : le cache est déjà à jour, il n'y a rien à copier.
    void OnRadioChanged(Radio sender, object args) => Changed?.Invoke();

    // Levé quand un état en cache a changé sans action du pont.
    internal event Action? Changed;

    internal async Task Toggle(string id, bool? enabled)
    {
        try
        {
            switch (id)
            {
                case "wifi":
                    await SetRadio(RadioKind.WiFi, enabled);
                    break;
                case "bluetooth":
                    await SetRadio(RadioKind.Bluetooth, enabled);
                    break;
                case "airplane":
                    await SetAirplane(enabled);
                    break;
                case "micMute":
                    await Task.Run(() => SetMic(enabled));
                    break;
                case "keepAwake":
                    SetKeepAwake(enabled);
                    break;
                case "nightLight":
                    OpenSettings("ms-settings:nightlight");
                    break;
                case "focus":
                    OpenSettings("ms-settings:quiethours");
                    break;
            }
        }
        catch (Exception ex)
        {
            Store.Log("Réglages rapides " + id + ": " + ex.Message);
        }
        Changed?.Invoke();
    }

    static void OpenSettings(string uri)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();

    List<Radio> RadiosOf(RadioKind? kind)
    {
        lock (gate)
            return radios.Where(r => kind == null || r.Kind == kind).ToList();
    }

    async Task SetRadio(RadioKind kind, bool? enabled)
    {
        await EnsureRadios();
        var list = RadiosOf(kind);
        if (list.Count == 0)
            return;
        bool on = enabled ?? !list.Any(r => r.State == RadioState.On);
        foreach (var r in list)
            await r.SetStateAsync(on ? RadioState.On : RadioState.Off);
    }

    static string Key(Radio r) => (int)r.Kind + ":" + r.Name;

    async Task SetAirplane(bool? enabled)
    {
        await EnsureRadios();
        var all = RadiosOf(null);
        if (all.Count == 0)
            return;
        bool currentlyOn = all.All(r => r.State != RadioState.On);
        if (enabled ?? !currentlyOn)
        {
            if (currentlyOn)
                return;
            lock (gate)
                airplaneMemory ??= all.Where(r => r.State == RadioState.On).Select(Key).ToList();
            foreach (var r in all)
                await r.SetStateAsync(RadioState.Off);
        }
        else
        {
            List<string>? remembered;
            lock (gate)
                remembered = airplaneMemory;
            var toRestore = DockItems.RadiosToRestore(remembered, all, Key, r => r.Kind is RadioKind.WiFi or RadioKind.Bluetooth);
            foreach (var r in toRestore)
                await r.SetStateAsync(RadioState.On);
            lock (gate)
                airplaneMemory = null;
        }
    }

    async Task EnsureRadios()
    {
        bool need;
        lock (gate)
            need = !radiosKnown || radiosFailed;
        if (need)
            await RefreshRadios();
    }

    void RefreshMic()
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            var device = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            lock (gate)
            {
                if (micDevice != null && micDevice.ID == device.ID)
                {
                    device.Dispose();
                    micMuted = micDevice.AudioEndpointVolume.Mute;
                    micAvailable = true;
                    return;
                }
                var previous = micDevice;
                if (previous != null)
                {
                    Safe.Try(() => previous.AudioEndpointVolume.OnVolumeNotification -= OnMic);
                    Safe.Dispose(previous);
                }
                var volume = device.AudioEndpointVolume;
                micDevice = device;
                micAvailable = true;
                micMuted = volume.Mute;
                volume.OnVolumeNotification += OnMic;
            }
        }
        catch (Exception ex)
        {
            lock (gate)
                micAvailable = false;
            Store.Log("Réglages rapides: micro indisponible: " + ex.Message);
        }
    }

    void OnMic(AudioVolumeNotificationData data)
    {
        lock (gate)
            micMuted = data.Muted;
        Changed?.Invoke();
    }

    void SetMic(bool? enabled)
    {
        RefreshMic();
        MMDevice? device;
        lock (gate)
            device = micDevice;
        if (device == null)
            return;
        bool mute = enabled ?? !device.AudioEndpointVolume.Mute;
        device.AudioEndpointVolume.Mute = mute;
        lock (gate)
            micMuted = mute;
    }

    [DllImport("kernel32.dll")]
    static extern uint SetThreadExecutionState(uint flags);
    const uint EsContinuous = 0x80000000, EsSystemRequired = 0x1, EsDisplayRequired = 0x2;

    void SetKeepAwake(bool? request)
    {
        lock (gate)
        {
            bool on = request ?? awakeThread == null;
            if (on == (awakeThread != null))
                return;
            if (!on)
            {
                awakeRelease?.Set();
                awakeThread = null;
                awakeRelease = null;
                return;
            }
            var release = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
                release.Wait();
                SetThreadExecutionState(EsContinuous);
            })
            { IsBackground = true, Name = "KeepAwake" };
            awakeRelease = release;
            awakeThread = thread;
            thread.Start();
        }
    }
}
