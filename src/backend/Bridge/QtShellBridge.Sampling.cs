// language: C# 12, file: QtShellBridge.Sampling.cs, target: Windows 11 / .NET 8, mode silencieux et échantillonnage périodique
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
    // Mode silencieux plein écran : l'état forcé (CLI seulement, jamais enregistré) prime sur la détection
    // (préférence active et un moniteur en plein écran, n'importe lequel).
    volatile QuietForce quietForce = QuietForce.Auto;
    bool QuietDetected => QuietPolicy.Detected(Store.State.QuietFullscreen, FullscreenWatcher.Current.Length);
    bool Quiet => QuietPolicy.Effective(quietForce, QuietDetected);
    void FullscreenChanged()
    {
        networkChanged?.Invoke();
        ApplyQuiet();
    }
    void ApplyQuiet()
    {
        if (disposed)
            return;
        try
        {
            wake.Set();
        }
        catch (ObjectDisposedException)
        {
            // Signal déjà libéré à l'arrêt : plus personne à réveiller.
        }
        dispatcher.BeginInvoke((Action)(() => { if (!disposed) UpdateSampling(); }));
    }
    static bool LightRendering => Store.State.ReducedEffects ?? GC.GetGCMemoryInfo().TotalAvailableMemoryBytes <= 8L * 1024 * 1024 * 1024;
    void UpdateSampling()
    {
        bars.SetActive(active && barsVisible && page == 0 && Store.State.ReducedEffects != true && !Quiet);
        mixer.SetActive(active && (page == 1 || page == 7 || mixerVisible));
        if (!active || page != 1)
            spectrum.Stop();
        bool need = active && page is 0 or 2;
        if (need)
        {
            if (!telemetry.Running)
                telemetry.Start(value => dispatcher.BeginInvoke((Action)(() => reading = value)), LightRendering ? 2000 : 1000,
                    quick: true);
        }
        else if (telemetry.Running)
            _ = telemetry.StopAsync();
        pageMusic = active && page is 0 or 1 || controls;
        if (pageMusic && !startingMedia)
        {
            startingMedia = true;
            _ = StartMedia();
        }
        else if (!pageMusic && startingMedia)
        {
            media.Stop();
            startingMedia = false;
        }
    }

    async Task StartMedia()
    {
        try
        {
            await media.StartAsync();
        }
        catch (Exception ex) { Store.Log("Musique Qt: " + ex.Message); }
    }

    void StopSampling()
    {
        wifi.SetOpen(false);
        displaysOpen = false;
        bars.SetActive(false);
        mixer.SetActive(false);
        spectrum.Stop();
        _ = telemetry.StopAsync();
        media.Stop();
        startingMedia = false;
    }

    async void Tick(object? sender, EventArgs e)
    {
        if (disposed || busy)
            return;
        if (active && (DateTime.UtcNow - lastReply).TotalSeconds > 5)
        {
            active = false;
            StopSampling();
            dismissal.SetActive(false);
        }
        if (!active)
            return;
        busy = true;
        try
        {
            if (mixer.Running)
                RefreshMixer();
            if (pageMusic)
            {
                var snapshot = await media.ReadAsync();
                if (snapshot != null)
                {
                    player = snapshot;
                    mixer.MediaApp = snapshot.ExeStem;
                    mixer.MediaAumid = snapshot.Aumid;
                    // Technique : capture WASAPI process loopback (ProcessAudio.dll) sur le PID racine du lecteur choisi, avec
                    // INCLUDE_TARGET_PROCESS_TREE, puis FFT de 2048 points fenêtrée de Hann dans WindowsSpectrum (48 bandes log).
                    // Ni un vu-mètre, ni la sortie par défaut : le son d'une autre application (un appel) n'est pas capturé.
                    // Au repos : un lecteur en pause ou terminé donne la cible 0, la capture est détruite et les barres retombent à zéro.
                    spectrum.SetTarget(active && page == 1 && snapshot.Playing ? mixer.MediaPid : 0);
                    string key = snapshot.Title + snapshot.Artist;
                    if (snapshot.Artwork != null && key != artworkKey)
                    {
                        artworkKey = key;
                        artworkPath = Path.Combine(cache, "cover-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                            key)))[..16] + ".png");
                        SaveImage(snapshot.Artwork, artworkPath);
                    }
                }
            }
            if (controls || page == 7)
            {
                sound = audio.Read();
                if (DateTime.UtcNow >= nextConnections)
                {
                    nextConnections = DateTime.UtcNow.AddSeconds(5);
                    connection = await connections.ReadAsync();
                }
                if (!displaysOpen)
                {
                    displaysOpen = true;
                    MonitorBrightness.NewSession();
                    nextBrightness = DateTime.MinValue;
                }
                if (!readingDisplays && DateTime.UtcNow >= nextBrightness)
                {
                    nextBrightness = DateTime.UtcNow.AddSeconds(BrightnessPolicy.RefreshSeconds);
                    _ = ReadDisplays();
                }
            }
            else
                displaysOpen = false;
        }
        catch (Exception ex) { Store.Log("Services Qt: " + ex.Message); }
        finally { busy = false; }
    }
}
