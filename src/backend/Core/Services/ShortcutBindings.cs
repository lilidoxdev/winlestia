// language: C# 12, file: ShortcutBindings.cs, target: Windows 11 / .NET 8, raccourcis globaux : libellés, états, réaffectation, aiguillage
using System;

namespace PanneauxGlass;

internal enum HotkeyAction { None, Rescue, Palette, Settings, Wallpaper, Pin, Capture, Toggle }

// Règle pure : quand les réglages sont ouverts, Palette, Capture et Toggle sont ignorés ; les autres actions passent toujours.
internal static class ShortcutGateRules
{
    public static bool IsBlockedBySettings(HotkeyAction action, bool settingsOpen) => settingsOpen
        && action is HotkeyAction.Palette or HotkeyAction.Capture or HotkeyAction.Toggle;
}

internal interface IShortcutBindings : IDisposable
{
    GlobalShortcut Global { get; }
    string ShortcutStatus { get; }
    string PaletteStatus { get; }
    string PinStatus { get; }
    string WallpaperStatus { get; }
    string CaptureStatus { get; }
    string PaletteLabel { get; }
    string PinShortcutLabel { get; }
    string WallpaperLabel { get; }
    string CaptureShortcutLabel { get; }
    bool SetShortcut(uint modifiers, uint key, string view, out string error);
    bool SetPaletteShortcut(uint modifiers, uint key, out string error);
    bool SetPinShortcut(uint modifiers, uint key, out string error);
    bool SetCaptureShortcut(uint modifiers, uint key, out string error);
    bool SetWallpaperShortcut(uint modifiers, uint key, out string error);
    HotkeyAction Resolve(int id);
}

// Les réaffectations passent par ici pour que l'état persisté et l'enregistrement Windows ne divergent jamais : la préférence n'est
// écrite que si Windows a accepté la combinaison.
internal sealed class ShortcutBindings : IShortcutBindings
{
    // Identifiant du raccourci de secours du Bureau, enregistré à part des quatre liaisons modifiables.
    internal const int RescueId = 2;
    readonly Action rebuildRail, refreshLabels;
    internal ShortcutBindings(GlobalShortcut global, Action rebuildRail, Action refreshLabels)
    {
        Global = global;
        this.rebuildRail = rebuildRail;
        this.refreshLabels = refreshLabels;
    }
    public GlobalShortcut Global { get; }
    public string ShortcutStatus => Global.Error;
    public string PaletteStatus => Global.PaletteError;
    public string PinStatus => Global.PinError;
    public string WallpaperStatus => Global.WallpaperError;
    public string CaptureStatus => Global.CaptureError;
    public string PaletteLabel => Label(Store.State.PaletteShortcut);
    public string PinShortcutLabel => Label(Store.State.PinShortcut);
    public string CaptureShortcutLabel => Label(Store.State.CaptureShortcut);
    // Le libellé suit la combinaison réellement active : une combinaison refusée ne doit pas s'afficher comme valide.
    public string WallpaperLabel
        => Global.WallpaperActive ? GlobalShortcut.Label(Global.WallpaperModifiers, Global.WallpaperKey)
            : Label(Store.State.WallpaperShortcut);
    static string Label(ShortcutPreferences preferences) => GlobalShortcut.Label(preferences.Modifiers, preferences.Key);
    public bool SetWallpaperShortcut(uint modifiers, uint key, out string error)
    {
        if (!Global.TrySetWallpaper(modifiers, key, out error))
            return false;
        Store.State.WallpaperShortcut = new() { Modifiers = modifiers, Key = key };
        Store.Save();
        return true;
    }
    public bool SetPaletteShortcut(uint modifiers, uint key, out string error)
    {
        if (!Global.TrySetPalette(modifiers, key, out error))
            return false;
        Store.State.PaletteShortcut = new() { Modifiers = modifiers, Key = key };
        Store.Save();
        refreshLabels();
        return true;
    }
    public bool SetPinShortcut(uint modifiers, uint key, out string error)
    {
        if (!Global.TrySetPin(modifiers, key, out error))
            return false;
        Store.State.PinShortcut = new() { Modifiers = modifiers, Key = key };
        Store.Save();
        refreshLabels();
        return true;
    }
    public bool SetCaptureShortcut(uint modifiers, uint key, out string error)
    {
        if (!Global.TrySetCapture(modifiers, key, out error))
            return false;
        Store.State.CaptureShortcut = new() { Modifiers = modifiers, Key = key };
        Store.Save();
        refreshLabels();
        return true;
    }
    public bool SetShortcut(uint modifiers, uint key, string view, out string error)
    {
        if (!Global.TrySet(modifiers, key, out error))
            return false;
        Store.State.Shortcut = new() { Modifiers = modifiers, Key = key, View = view };
        Store.State.Shortcut.Normalize();
        Store.Save();
        rebuildRail();
        return true;
    }
    // L'ordre compte : le secours l'emporte sur toute liaison, et la bascule du lanceur (id 1 historique) vient en dernier.
    public HotkeyAction Resolve(int id)
        => id == RescueId ? HotkeyAction.Rescue
            : Global.MatchesPalette(id) ? HotkeyAction.Palette
            : Global.MatchesSettings(id) ? HotkeyAction.Settings
            : Global.MatchesWallpaper(id) ? HotkeyAction.Wallpaper
            : Global.MatchesPin(id) ? HotkeyAction.Pin
            : Global.MatchesCapture(id) ? HotkeyAction.Capture
            : Global.Matches(id) ? HotkeyAction.Toggle
            : HotkeyAction.None;
    public void Dispose() => Global.Dispose();
}
