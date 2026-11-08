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
            system.BeginDictationCapture();
        else
            system.EndDictationCapture();
    }

    [BridgeAction("media")]
    async Task OnMedia(BridgeCall c)
    {
        await system.MediaCommand(Text(c.Args, "action"), Number(c.Args, "value"));
    }

    [BridgeAction("mediaVolume")]
    async Task OnMediaVolume(BridgeCall c)
    {
        await system.SetMediaVolume(Math.Clamp(Number(c.Args, "value"), 0, 100));
    }

    [BridgeAction("volume")]
    void OnVolume(BridgeCall c)
    {
        system.SetVolume(Math.Clamp(Number(c.Args, "value"), 0, 100));
        sound = system.ReadAudio();
    }

    [BridgeAction("brightness")]
    async Task OnBrightness(BridgeCall c)
    {
        double brightLevel = Math.Clamp(Number(c.Args, "value"), 0, 100);
        if (c.Args.TryGetProperty("id", out _))
            _ = SetDisplay((int)Number(c.Args, "id"), brightLevel, false);
        else
        {
            await system.SetBrightnessAtCursor(brightLevel);
            nextBrightness = DateTime.MinValue;
        }
    }

    [BridgeAction("wifiConnect")]
    void OnWifiConnect(BridgeCall c)
    {
        _ = system.WifiConnect(Text(c.Args, "ssid"), Text(c.Args, "password"));
    }

    [BridgeAction("wifiForget")]
    void OnWifiForget(BridgeCall c)
    {
        _ = system.WifiForget(Text(c.Args, "ssid"));
    }

    [BridgeAction("wifiPortal")]
    void OnWifiPortal(BridgeCall c)
    {
        system.OpenWifiPortal();
    }

    [BridgeAction("wifiSettings")]
    void OnWifiSettings(BridgeCall c)
    {
        system.OpenWifiSettings(Text(c.Args, "page") == "location");
    }

    [BridgeAction("radio")]
    async Task OnRadio(BridgeCall c)
    {
        {
            var radioWifi = Text(c.Args, "kind") == "wifi";
            try
            {
                await system.ToggleRadio(radioWifi ? RadioKind.WiFi : RadioKind.Bluetooth);
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
        connection = await system.ReadConnections();
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
        await system.SetApplication(c.Id, Number(c.Args, "value"), null);
    }

    [BridgeAction("sessionMute")]
    async Task OnSessionMute(BridgeCall c)
    {
        await system.SetApplication(c.Id, null, Flag(c.Args, "muted"));
    }

    [BridgeAction("deviceVolume")]
    async Task OnDeviceVolume(BridgeCall c)
    {
        await system.SetDevice(c.Id, Number(c.Args, "value"), null, false);
    }

    [BridgeAction("deviceMute")]
    async Task OnDeviceMute(BridgeCall c)
    {
        await system.SetDevice(c.Id, null, Flag(c.Args, "muted"), false);
    }

    [BridgeAction("deviceSelect")]
    async Task OnDeviceSelect(BridgeCall c)
    {
        await system.SetDevice(c.Id, null, null, true);
        sound = system.ReadAudio();
    }

    [BridgeAction("masterMute")]
    void OnMasterMute(BridgeCall c)
    {
        system.SetMute(Flag(c.Args, "muted"));
        sound = system.ReadAudio();
    }
}
