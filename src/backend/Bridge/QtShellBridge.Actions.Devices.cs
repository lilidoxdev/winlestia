// language: C# 12, file: QtShellBridge.Actions.Devices.cs, target: Windows 11 / .NET 8, actions du pont : média, volume, luminosité,
// Wi-Fi et radios
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Devices.Radios;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    [BridgeAction("dictationCapture")]
    void OnDictationCapture(BridgeCall c)
    {
        if (Flag(c.Args, "enabled"))
            dictation.BeginCapture();
        else
            dictation.EndCapture();
    }

    [BridgeAction("media")]
    async Task OnMedia(BridgeCall c)
    {
        await media.CommandAsync(Text(c.Args, "action"), Number(c.Args, "value"));
    }

    [BridgeAction("mediaVolume")]
    async Task OnMediaVolume(BridgeCall c)
    {
        await mixer.SetMediaVolume(Math.Clamp(Number(c.Args, "value"), 0, 100));
    }

    [BridgeAction("volume")]
    void OnVolume(BridgeCall c)
    {
        audio.SetVolume(Math.Clamp(Number(c.Args, "value"), 0, 100));
        sound = audio.Read();
    }

    [BridgeAction("brightness")]
    async Task OnBrightness(BridgeCall c)
    {
        double brightLevel = Math.Clamp(Number(c.Args, "value"), 0, 100);
        if (c.Args.TryGetProperty("id", out _))
            _ = SetDisplay((int)Number(c.Args, "id"), brightLevel, false);
        else
        {
            Native.GetCursorPos(out var point);
            await MonitorBrightness.SetAsync(point, brightLevel);
            nextBrightness = DateTime.MinValue;
        }
    }

    [BridgeAction("wifiConnect")]
    void OnWifiConnect(BridgeCall c)
    {
        _ = wifi.ConnectAsync(Text(c.Args, "ssid"), Text(c.Args, "password"));
    }

    [BridgeAction("wifiForget")]
    void OnWifiForget(BridgeCall c)
    {
        _ = wifi.ForgetAsync(Text(c.Args, "ssid"));
    }

    [BridgeAction("wifiPortal")]
    void OnWifiPortal(BridgeCall c)
    {
        WifiHost.OpenPortal();
    }

    [BridgeAction("wifiSettings")]
    void OnWifiSettings(BridgeCall c)
    {
        WifiHost.OpenSettings(Text(c.Args, "page") == "location");
    }

    [BridgeAction("radio")]
    async Task OnRadio(BridgeCall c)
    {
        {
            var radioWifi = Text(c.Args, "kind") == "wifi";
            try
            {
                await connections.ToggleAsync(radioWifi ? RadioKind.WiFi : RadioKind.Bluetooth);
            }
            catch (RadioAccessDeniedException)
            {
                if (radioWifi
                ? !wifiSettingsOpened
                : !bluetoothSettingsOpened)
                {
                    if (radioWifi)
                        wifiSettingsOpened = true;
                    else
                        bluetoothSettingsOpened = true;
                    try
                    {
                        Process.Start(new ProcessStartInfo(radioWifi
                            ? "ms-settings:network-wifi"
                            : "ms-settings:bluetooth")
                        {
                            UseShellExecute = true
                        })?.Dispose();
                    }
                    catch (Exception ex) { Store.Log("Réglages connexion: " + ex.Message); }
                }
                throw;
            }
        }
        connection = await connections.ReadAsync();
    }

    [BridgeAction("audio")]
    void OnAudio(BridgeCall c)
    {
        Send("audio");
    }

    [BridgeAction("microphone")]
    void OnMicrophone(BridgeCall c)
    {
        Send("microphone");
    }

    [BridgeAction("sessionVolume")]
    async Task OnSessionVolume(BridgeCall c)
    {
        await mixer.SetApplication(c.Id, Number(c.Args, "value"), null);
    }

    [BridgeAction("sessionMute")]
    async Task OnSessionMute(BridgeCall c)
    {
        await mixer.SetApplication(c.Id, null, Flag(c.Args, "muted"));
    }

    [BridgeAction("deviceVolume")]
    async Task OnDeviceVolume(BridgeCall c)
    {
        await mixer.SetDevice(c.Id, Number(c.Args, "value"), null, false);
    }

    [BridgeAction("deviceMute")]
    async Task OnDeviceMute(BridgeCall c)
    {
        await mixer.SetDevice(c.Id, null, Flag(c.Args, "muted"), false);
    }

    [BridgeAction("deviceSelect")]
    async Task OnDeviceSelect(BridgeCall c)
    {
        await mixer.SetDevice(c.Id, null, null, true);
        sound = audio.Read();
    }

    [BridgeAction("masterMute")]
    void OnMasterMute(BridgeCall c)
    {
        audio.SetMute(Flag(c.Args, "muted"));
        sound = audio.Read();
    }
}
