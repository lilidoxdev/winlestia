// ControlCenterWindow : lecture et commandes média, connexions et luminosité.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Devices.Radios;

namespace PanneauxGlass;

internal sealed partial class ControlCenterWindow
{
    async Task StartMedia()
    {
        try
        {
            await media.StartAsync();
            await RefreshMedia();
        }
        catch (Exception ex)
        {
            mediaTitle.Text = "Lecture indisponible";
            artist.Text = "Ouvrir les commandes audio Windows";
            Store.Log("SMTC: " + ex.Message);
        }
    }
    void RefreshAudio()
    {
        if (!IsVisible)
            return;
        audioReads++;
        var updated = audio.Read();
        if (updated == audioState)
            return;
        audioState = updated;
        syncing = true;
        try
        {
            volume.IsEnabled = audioState.Available;
            if (!volume.Dragging)
                volume.Value = audioState.Volume;
            volume.Glyph = audioState.Muted || audioState.Volume < 1 ? "\uE74F" : "\uE767";
            volume.InvalidateVisual();
            volume.ToolTip = audioState.Available ? "Volume système · glisser ou utiliser la molette" : "Aucune sortie audio disponible";
            device.Text = audioState.Device;
            device.ToolTip = audioState.Device;
            mute.IsEnabled = audioState.Available;
            mute.Content = IconLabel(audioState.Muted ? "\uE74F" : "\uE767", audioState.Muted ? "Son coupé" : "Son activé", 12);
        }
        finally { syncing = false; }
    }
    async Task RefreshMedia()
    {
        if (!IsVisible || !Enabled("media") || refreshingMedia)
            return;
        refreshingMedia = true;
        mediaReads++;
        int expected = revision;
        try
        {
            var state = await media.ReadAsync();
            if (state != null && expected == revision && IsVisible)
            {
                var before = mediaState;
                mediaState = state;
                ApplyMedia(before);
            }
        }
        catch (Exception ex)
        {
            Feedback("Lecture indisponible");
            Store.Log("SMTC lecture: " + ex.Message);
        }
        finally { refreshingMedia = false; }
    }
    void ApplyMedia(MediaSnapshot? before = null)
    {
        var s = mediaState;
        if (s == null)
            return;
        mediaTitle.Text = s.Title;
        mediaTitle.ToolTip = s.Title;
        artist.Text = s.Artist;
        artist.ToolTip = s.Artist;
        appName.Text = s.Available ? s.App.ToUpperInvariant() : "MUSIQUE";
        cover.Source = s.Artwork;
        cover.Visibility = s.Artwork == null ? Visibility.Hidden : Visibility.Visible;
        play.IsEnabled = s.Available && s.CanToggle;
        previous.IsEnabled = s.Available && s.CanPrevious;
        next.IsEnabled = s.Available && s.CanNext;
        if (before?.Playing != s.Playing)
            play.Content = UI.Glyph(s.Playing ? "\uE769" : "\uE768", 20);
        if (play.Content is TextBlock glyph)
            glyph.Foreground = UI.Brush("#254D74");
        if (!seeking)
        {
            progress.Maximum = Math.Max(1, s.Duration);
            progress.Value = s.Position;
        }
        progress.IsEnabled = s.Available && s.CanSeek && s.Duration > 0;
        elapsed.Text = Time(s.Position);
        duration.Text = Time(s.Duration);
    }
    internal static string Time(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0));
        return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
    }
    void EndSeek()
    {
        if (!seeking)
            return;
        seeking = false;
        owner.EndLayoutInteraction();
        _ = MediaCommand("seek", progress.Value);
    }
    async Task MediaCommand(string action, double position = 0)
    {
        try
        {
            if (!await media.CommandAsync(action, position))
                Feedback("Cette commande n’est pas disponible pour cette lecture.");
            await RefreshMedia();
        }
        catch (Exception ex) { Feedback(ex.Message); }
    }
    void OpenSessions()
    {
        var sessions = media.Sessions();
        var actions = new List<MenuAction> { new("Automatique · priorité à Spotify", "\uE8D6", () => {
            Store.State.Controls.MediaApp = "";
        Store.Save();
        _ = RefreshMedia(); }, string.IsNullOrEmpty(Store.State.Controls.MediaApp)) };
        foreach (var s in sessions)
            actions.Add(new(s.name, "\uE8D6", () =>
            {
                Store.State.Controls.MediaApp = s.id;
                Store.Save();
                _ = RefreshMedia();
            }, Store.State.Controls.MediaApp == s.id));
        GlassMenu.Open(owner, this, sessionPicker, "Source musicale", actions);
    }
    async Task RefreshConnections()
    {
        if (!IsVisible || !Enabled("connections") || refreshingConnections)
            return;
        refreshingConnections = true;
        int expected = revision;
        try
        {
            var result = await connections.ReadAsync();
            if (IsVisible && expected == revision)
            {
                connectionState = result;
                UpdateConnections();
            }
        }
        catch (Exception ex)
        {
            Feedback("Connexions indisponibles");
            Store.Log("Radios: " + ex.Message);
        }
        finally { refreshingConnections = false; }
    }
    void UpdateConnections()
    {
        if (connectionState is not { } s)
            return;
        subtitle.Text = s.Internet ? s.Network + " · Connecté à Internet" : "Windows · Hors connexion";
        foreach (string id in new[] { "wifi", "bluetooth", "network" })
            if (radios.TryGetValue(id, out var tile))
            {
                bool on = id == "wifi" ? s.Wifi.On : id == "bluetooth" ? s.Bluetooth.On : s.Internet;
                bool present = id == "wifi" ? s.Wifi.Present : id == "bluetooth" ? s.Bluetooth.Present : true;
                tile.button.Background = UI.Brush(on ? id == "network" ? "#D93BB283" : "#DE247BF2" : "#20FFFFFF");
                tile.status.Text = !present
                    ? "Absent"
                    : id == "network" ? s.Ethernet ? "Ethernet" : s.Internet ? "Connecté" : "Hors ligne" : on ? "Activé" : "Désactivé";
                tile.button.ToolTip = id == "wifi"
                    ? s.Wifi.Present ? "Activer/désactiver le Wi-Fi · clic droit pour les réseaux"
                        : "Wi-Fi absent · ouvrir les réglages"
                    : id == "bluetooth"
                        ? s.Bluetooth.Present ? "Activer/désactiver Bluetooth · clic droit pour les appareils"
                            : "Bluetooth absent · ouvrir les réglages"
                        : "Ouvrir les connexions Windows";
            }
    }
    async void ConnectionAction(string id)
    {
        try
        {
            if (id == "wifi" || id == "bluetooth")
            {
                bool present = id == "wifi" ? connectionState?.Wifi.Present == true : connectionState?.Bluetooth.Present == true;
                if (!present)
                {
                    OpenSetting(id == "wifi" ? "network-wifi" : "bluetooth");
                    return;
                }
                if (radios.TryGetValue(id, out var tile))
                    tile.button.IsEnabled = false;
                try
                {
                    await owner.RunControlTask(async () =>
                    {
                        await connections.ToggleAsync(id == "wifi" ? RadioKind.WiFi : RadioKind.Bluetooth);
                        await RefreshConnections();
                    });
                }
                finally { if (radios.TryGetValue(id, out tile)) tile.button.IsEnabled = true; }
            }
            else
                OpenSetting(id == "network" ? "network-status" : "");
        }
        catch (Exception ex) { Feedback(ex.Message); }
    }
    Native.Point ScreenPoint()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Native.Point { X = (int)((Left + Width / 2) * dpi.DpiScaleX), Y = (int)((Top + Height / 2) * dpi.DpiScaleY) };
    }
    async Task ReadBrightness()
    {
        int expected = revision;
        brightness.IsEnabled = false;
        brightness.ToolTip = "Lecture du réglage de l’écran…";
        try
        {
            var state = await MonitorBrightness.ReadAsync(ScreenPoint());
            if (!IsVisible || expected != revision)
                return;
            syncing = true;
            try
            {
                brightness.IsEnabled = state.Available;
                brightness.Value = state.Value;
                brightness.ToolTip = state.Available
                    ? state.Device + " · glisser pour régler"
                    : state.Error + " Clique sur Luminosité pour ouvrir les réglages écran.";
            }
            finally { syncing = false; }
        }
        catch (Exception ex)
        {
            brightness.ToolTip = "Réglage non disponible · clique sur Luminosité pour les réglages écran";
            Store.Log("Luminosité: " + ex.Message);
        }
    }
    async Task WriteBrightness()
    {
        if (writingBrightness || !pendingBrightness.HasValue)
            return;
        writingBrightness = true;
        double value = pendingBrightness.Value;
        pendingBrightness = null;
        var point = ScreenPoint();
        try
        {
            await MonitorBrightness.SetAsync(point, value);
        }
        catch (Exception ex) { Feedback(ex.Message); }
        finally
        {
            writingBrightness = false;
            if (pendingBrightness.HasValue && !brightnessTimer.IsEnabled)
                brightnessTimer.Start();
        }
    }
    void Feedback(string message)
    {
        note.Text = message;
        note.ToolTip = message;
    }
    void OpenSetting(string page) => owner.LaunchWindowsShortcut(new() { Name = "Réglages Windows", Target = "ms-settings:" + page });
}
