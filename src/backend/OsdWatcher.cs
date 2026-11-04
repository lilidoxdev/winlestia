// C# 12 / OsdWatcher.cs / Windows .NET 8. Event-driven (no polling) default-output volume/mute change notifications for the Qt OSD.
using System;
using System.Management;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace PanneauxGlass;

internal sealed class OsdWatcher : IDisposable, IMMNotificationClient
{
    readonly Dispatcher dispatcher;
    readonly Action<int, bool> changed;
    readonly Action<string>? extra;
    MMDeviceEnumerator? devices;
    MMDevice? endpoint;
    MMDevice? mic;
    ManagementEventWatcher? brightnessWatcher;
    int lastBrightness = -1,
        lastMic = -1;
    bool lastMicMuted;
    bool disposed;
    int lastVolume = -1;
    bool lastMuted;

    internal OsdWatcher(Dispatcher dispatcher, Action<int, bool> changed, Action<string>? extra = null)
    {
        this.dispatcher = dispatcher;
        this.changed = changed;
        this.extra = extra;
        try
        {
            devices = new MMDeviceEnumerator();
            devices.RegisterEndpointNotificationCallback(this);
            Attach();
            AttachMic();
        }
        catch (Exception ex)
        {
            Store.Log("OSD: " + ex.Message);
        }
        StartBrightness();
    }

    void Attach()
    {
        try
        {
            Detach();
            if (devices == null || disposed)
                return;
            endpoint = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var volume = endpoint.AudioEndpointVolume;
            lastVolume = (int)Math.Round(volume.MasterVolumeLevelScalar * 100);
            lastMuted = volume.Mute;
            volume.OnVolumeNotification += OnVolume;
        }
        catch (Exception ex)
        {
            Detach();
            Store.Log("OSD: sortie audio indisponible: " + ex.Message);
        }
    }

    void Detach()
    {
        if (endpoint == null)
            return;
        try
        {
            endpoint.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
        }
        catch { }
        try
        {
            endpoint.Dispose();
        }
        catch { }
        endpoint = null;
    }

    void AttachMic()
    {
        try
        {
            DetachMic();
            if (devices == null || disposed)
                return;
            mic = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            var volume = mic.AudioEndpointVolume;
            lastMic = (int)Math.Round(volume.MasterVolumeLevelScalar * 100);
            lastMicMuted = volume.Mute;
            volume.OnVolumeNotification += OnMic;
        }
        catch (Exception ex)
        {
            DetachMic();
            Store.Log("OSD: micro indisponible: " + ex.Message);
        }
    }

    void DetachMic()
    {
        if (mic == null)
            return;
        try
        {
            mic.AudioEndpointVolume.OnVolumeNotification -= OnMic;
        }
        catch { }
        try
        {
            mic.Dispose();
        }
        catch { }
        mic = null;
    }

    void OnMic(AudioVolumeNotificationData data)
    {
        int volume = (int)Math.Round(Math.Clamp(data.MasterVolume, 0, 1) * 100);
        bool muted = data.Muted;
        _ = dispatcher.BeginInvoke(
            (Action)(
                () =>
                {
                    if (disposed || (volume == lastMic && muted == lastMicMuted))
                        return;
                    lastMic = volume;
                    lastMicMuted = muted;
                    extra?.Invoke("osd:mic:" + volume + ":" + (muted ? 1 : 0));
                }
            )
        );
    }

    // Brightness: WMI event only (internal panels); external DDC-only screens raise nothing and nothing is shown.
    void StartBrightness()
    {
        try
        {
            var watcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            watcher.Scope = new ManagementScope(@"root\wmi");
            watcher.EventArrived += (_, e) =>
            {
                try
                {
                    int value = Math.Clamp(Convert.ToInt32(e.NewEvent["Brightness"]), 0, 100);
                    _ = dispatcher.BeginInvoke(
                        (Action)(
                            () =>
                            {
                                if (disposed || value == lastBrightness)
                                    return;
                                lastBrightness = value;
                                extra?.Invoke("osd:brightness:" + value);
                            }
                        )
                    );
                }
                catch { }
            };
            watcher.Start();
            brightnessWatcher = watcher;
        }
        catch (Exception ex)
        {
            brightnessWatcher = null;
            Store.Log("OSD: luminosité WMI indisponible: " + ex.Message);
        }
    }

    void StopBrightness()
    {
        var watcher = brightnessWatcher;
        brightnessWatcher = null;
        if (watcher == null)
            return;
        try
        {
            watcher.Stop();
        }
        catch { }
        try
        {
            watcher.Dispose();
        }
        catch { }
    }

    void OnVolume(AudioVolumeNotificationData data)
    {
        int volume = (int)Math.Round(Math.Clamp(data.MasterVolume, 0, 1) * 100);
        bool muted = data.Muted;
        _ = dispatcher.BeginInvoke(
            (Action)(
                () =>
                {
                    if (disposed || (volume == lastVolume && muted == lastMuted))
                        return;
                    lastVolume = volume;
                    lastMuted = muted;
                    changed(volume, muted);
                }
            )
        );
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (role != Role.Multimedia)
            return;
        if (flow == DataFlow.Render)
            _ = dispatcher.BeginInvoke((Action)Attach);
        else if (flow == DataFlow.Capture)
            _ = dispatcher.BeginInvoke((Action)AttachMic);
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }

    public void OnDeviceAdded(string pwstrDeviceId) { }

    public void OnDeviceRemoved(string deviceId) { }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        try
        {
            devices?.UnregisterEndpointNotificationCallback(this);
        }
        catch { }
        Detach();
        DetachMic();
        StopBrightness();
        devices?.Dispose();
        devices = null;
    }
}
