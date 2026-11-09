// language: C# 12, file: QtShellBridge.Testing.cs, target: Windows 11 / .NET 8, point d'entrée du dispatch pour les tests
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    // Même chemin que Handle (table de dispatch réelle, sortie dans `call`) sans l'assemblage d'état, qui lit l'audio, le réseau
    // et la télémétrie. Renvoie la réponse brute éventuelle (null = réponse d'état à assembler).
    internal Task<string?> RunAction(string command, BridgeCall call) => actionTable.Run(this, command, call, Store.Log);

    // Messages différés (réponse au prochain échange), lus par les tests.
    internal string? LateNotice => lateNotice;
    internal object? LateWallpaperPicker => lateWallpaperPicker;
    internal AudioSnapshot? LastSound => sound;
    internal Dictionary<string, Entry> DockEntries => dockEntries;

    // Évènements en attente, filtrés comme dans Handle, puis vidés.
    internal string[] DrainEvents()
    {
        var queued = events.Where(e => KeepEvent(e.name, e.epoch, frontendEpoch)).Select(e => e.name).ToArray();
        events.Clear();
        return queued;
    }
}

// Pièces injectées dans le constructeur d'essai du pont : le banc de test les fournit, la production n'y passe pas.
internal sealed record BridgeParts(
    IAudioPort Audio,
    IWifiPort Wifi,
    IDesktopPort Desktop,
    IWallpaperPort Wallpaper,
    IDisplayPort Display,
    IShellPort Shell,
    System.Threading.EventWaitHandle Wake,
    WindowTiler Tiler,
    ScreenFrameReservations Frame,
    FileSearch FileSearch,
    AudioBarsHost Bars,
    SocialService Social,
    BookmarksService Bookmarks,
    string Directory);
