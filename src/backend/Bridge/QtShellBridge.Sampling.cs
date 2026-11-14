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
    // La Pilule (capsule en haut de l'écran) demande le spectre tant qu'elle est visible et que le lecteur joue.
    bool pillSpectrum;
    // Événement SMTC reçu depuis la dernière lecture (vrai au départ pour la première), et dernier état de lecture connu.
    volatile bool mediaEvent = true;
    bool mediaPlaying, mediaSubscribed;
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
        bars.SetActive(barsVisible && Store.State.ReducedEffects != true && !Quiet);
        mixer.SetActive(active && (page == 1 || page == 7 || mixerVisible) || pillSpectrum);
        if (!SpectrumPolicy.Surface(active, page, pillSpectrum))
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
        // La sonde média suit trois consommateurs : le tableau de bord, le centre de contrôle, et un Winjet de bureau qui
        // lit `media.` (sinon le lecteur resterait figé sur son dernier instantané panneau fermé).
        pageMusic = active && page is 0 or 1 || controls || mediaVisible;
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
            if (!mediaSubscribed)
            {
                mediaSubscribed = true;
                media.Changed += () => mediaEvent = true;
            }
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
        // Sans frontend, ni la Pilule ni la sonde média ne demandent plus rien : le prochain Tick doit sortir tout de suite.
        pillSpectrum = false;
        mediaVisible = false;
        pageMusic = false;
    }

    async void Tick(object? sender, EventArgs e)
    {
        if (disposed || busy)
            return;
        // Frontend muet depuis plus de 5 s : plus personne pour consommer, y compris la Pilule (sans panneau ouvert).
        if ((active || pillSpectrum || mediaVisible) && (DateTime.UtcNow - lastReply).TotalSeconds > 5)
        {
            bool wasActive = active;
            active = false;
            StopSampling();
            if (wasActive)
                dismissal.SetActive(false);
        }
        // Le bureau peut demander la sonde média sans panneau ouvert : ne pas sortir avant elle.
        if (!active && !mediaVisible)
            return;
        busy = true;
        try
        {
            if (mixer.Running)
                RefreshMixer();
            // Seule la Pilule veut le média (ni tableau de bord, ni centre de contrôle) : sans lecture en cours, on ne relit que sur
            // un événement SMTC (session apparue ou disparue, lecture, métadonnées), jamais en boucle.
            bool pillOnly = !(active && page is 0 or 1) && !controls;
            if (pageMusic && (!pillOnly || mediaEvent || mediaPlaying))
            {
                mediaEvent = false;
                var snapshot = await media.ReadAsync();
                if (snapshot == null)
                    mediaEvent = true;
                else
                {
                    player = snapshot;
                    mediaPlaying = snapshot.Playing;
                    mixer.MediaApp = snapshot.ExeStem;
                    mixer.MediaAumid = snapshot.Aumid;
                    // Technique : capture WASAPI process loopback (ProcessAudio.dll) sur le PID racine du lecteur choisi, avec
                    // INCLUDE_TARGET_PROCESS_TREE, puis FFT de 2048 points fenêtrée de Hann dans WindowsSpectrum (48 bandes log).
                    // Ni un vu-mètre, ni la sortie par défaut : le son d'une autre application (un appel) n'est pas capturé.
                    // Au repos : un lecteur en pause ou terminé donne la cible 0, la capture est détruite et les barres retombent à zéro.
                    spectrum.SetTarget(SpectrumPolicy.Wanted(active, page, pillSpectrum, snapshot.Playing) ? mixer.MediaPid : 0);
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
