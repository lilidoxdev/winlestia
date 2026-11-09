// C# 12, fichier GlobalShortcut.cs, cible Windows 11 / .NET 8 WPF, deux liaisons RegisterHotKey atomiques indépendantes
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace PanneauxGlass;

public sealed class ShortcutPreferences
{
    public uint Modifiers { get; set; } = 2; public uint Key { get; set; } = 0x70; public string View { get; set; } = "controls";
    public void Normalize()
    {
        if (!GlobalShortcut.Valid(Modifiers, Key))
        {
            Modifiers = 2;
            Key = 0x70;
        }
        if (!new[] { "home", "controls", "pc", "workspace", "last" }.Contains(View))
            View = "controls";
    }
}
// Quatre liaisons utilisateur partagent une fenêtre de messages : le lanceur (3/4), la palette (5/6), l'épingle (7/8) et la capture
// (9/10). Chaque remplacement réserve la nouvelle combinaison avant de libérer la précédente.
internal sealed class GlobalShortcut : IDisposable
{
    readonly IntPtr window; int customId, paletteId, pinId, captureId, wallpaperId, settingsId; uint modifiers, key, paletteModifiers,
        paletteKey, pinModifiers, pinKey, captureModifiers, captureKey, wallpaperModifiers, wallpaperKey, settingsModifiers, settingsKey;
    bool legacy, rescue;
    // Point d'injection de l'enregistrement : les tests les remplacent (code d'erreur 0 = enregistré) pour ne toucher aucun vrai
    // raccourci. LastCode contient l'erreur Win32 du dernier enregistrement échoué (1409 = déjà pris).
    internal static Func<IntPtr, int, uint, uint, int>? TestRegister; internal static Func<IntPtr, int, bool>? TestUnregister;
    internal static int LastCode;
    static bool Reg(IntPtr w, int id, uint m, uint k)
    {
        if (TestRegister != null)
        {
            LastCode = TestRegister(w, id, m, k);
            return LastCode == 0;
        }
        bool ok = Native.RegisterHotKey(w, id, m, k);
        LastCode = ok ? 0 : Marshal.GetLastWin32Error();
        return ok;
    }
    static void Unreg(IntPtr w, int id)
    {
        if (TestUnregister != null)
        {
            TestUnregister(w, id);
            return;
        }
        Native.UnregisterHotKey(w, id);
    }
    internal string Error { get; private set; } = ""; internal string PaletteError { get; private set; } = ""; internal string PinError
    {
        get; private set;
    } = ""; internal string CaptureError { get; private set; } = ""; internal string WallpaperError
    {
        get;
        private set;
    } = ""; internal string SettingsError { get; private set; } = "";
    internal GlobalShortcut(IntPtr hwnd, ShortcutPreferences preferences) : this(hwnd, preferences, new()
    {
        Modifiers = 2,
        Key = 0x71
    })
    {
    }
    internal GlobalShortcut(IntPtr hwnd, ShortcutPreferences preferences, ShortcutPreferences? palette = null,
        ShortcutPreferences? pin = null, ShortcutPreferences? capture = null, ShortcutPreferences? wallpaper = null,
        ShortcutPreferences? settings = null)
    {
        window = hwnd;
        legacy = Reg(window, 1, 0x4003, 0x20);
        rescue = Reg(window, 2, 0x4003, 0x08);
        TrySet(preferences.Modifiers, preferences.Key, out _);
        if (palette != null)
            TrySetPalette(palette.Modifiers, palette.Key, out _);
        if (pin != null)
            TrySetPin(pin.Modifiers, pin.Key, out _);
        if (capture != null)
            TrySetCapture(capture.Modifiers, capture.Key, out _);
        if (settings != null && !TrySetSettings(settings.Modifiers, settings.Key, out var settingsFailure))
            Store.Log("Raccourci des paramètres · " + Label(settings.Modifiers, settings.Key) + " refusé : " + settingsFailure);
        if (wallpaper != null && !TrySetWallpaper(wallpaper.Modifiers, wallpaper.Key, out var failure))
        {
            Store.Log("Sélecteur de fonds d’écran · " + Label(wallpaper.Modifiers, wallpaper.Key) + " refusé : " + failure
                + " · repli sur Ctrl + Alt + W");
            if (!TrySetWallpaper(3, 0x57, out failure))
                Store.Log("Sélecteur de fonds d’écran · repli refusé : " + failure);
        }
    }
    internal bool Matches(int id) => id == 1 && legacy || id == customId && customId != 0;
    internal bool MatchesPalette(int id) => id == paletteId && paletteId != 0;
    internal bool MatchesPin(int id) => id == pinId && pinId != 0;
    internal bool MatchesCapture(int id) => id == captureId && captureId != 0;
    internal bool MatchesWallpaper(int id) => id == wallpaperId && wallpaperId != 0;
    internal bool MatchesSettings(int id) => id == settingsId && settingsId != 0;
    internal bool SettingsActive => settingsId != 0; internal bool IsActive(string id) => id switch
    {
        "dashboard" => customId != 0,
        "palette" => paletteId != 0,
        "wallpaper" => wallpaperId != 0,
        "capture" => captureId != 0,
        "pin" => pinId != 0,
        "settings"
        => settingsId != 0,
        _ => true
    }; internal bool WallpaperRegistered => wallpaperId != 0;
    internal uint WallpaperModifiers => wallpaperModifiers; internal uint WallpaperKey => wallpaperKey; internal bool WallpaperActive
        => wallpaperId != 0;
    internal bool TrySetWallpaper(uint nextModifiers, uint nextKey, out string error)
    {
        error = "";
        if (!ValidWallpaper(nextModifiers, nextKey))
        {
            error = "Choisis Ctrl, Alt ou Windows et une touche. Ctrl + Alt + Retour arrière est réservé au secours.";
            return false;
        }
        if ((nextModifiers == modifiers && nextKey == key && customId != 0) || (nextModifiers == paletteModifiers && nextKey == paletteKey
            && paletteId != 0) || (nextModifiers == pinModifiers && nextKey == pinKey && pinId != 0) || (nextModifiers == captureModifiers
            && nextKey == captureKey && captureId != 0))
        {
            error = "Cette combinaison est déjà utilisée par Winlestia.";
            return false;
        }
        if (wallpaperId != 0 && nextModifiers == wallpaperModifiers && nextKey == wallpaperKey)
        {
            WallpaperError = "";
            return true;
        }
        int candidate = wallpaperId == 11 ? 12 : 11;
        if (!Reg(window, candidate, nextModifiers | 0x4000, nextKey))
        {
            int code = LastCode;
            error = code == 1409 ? "Cette combinaison est déjà utilisée par une autre application."
                : "Windows refuse cette combinaison ("
                + code + ").";
            WallpaperError = error;
            return false;
        }
        if (wallpaperId != 0)
            Unreg(window, wallpaperId);
        wallpaperId = candidate;
        wallpaperModifiers = nextModifiers;
        wallpaperKey = nextKey;
        WallpaperError = "";
        return true;
    }
    internal bool TrySetSettings(uint nextModifiers, uint nextKey, out string error)
    {
        error = "";
        if (!ValidWallpaper(nextModifiers, nextKey))
        {
            error = "Choisis Ctrl, Alt ou Windows et une touche.";
            return false;
        }
        if (settingsId != 0 && nextModifiers == settingsModifiers && nextKey == settingsKey)
        {
            SettingsError = "";
            return true;
        }
        int candidate = settingsId == 13 ? 14 : 13;
        if (!Reg(window, candidate, nextModifiers | 0x4000, nextKey))
        {
            int code = LastCode;
            error = code == 1409 ? "Cette combinaison est déjà utilisée par une autre application."
                : "Windows refuse cette combinaison ("
                + code + ").";
            SettingsError = error;
            return false;
        }
        if (settingsId != 0)
            Unreg(window, settingsId);
        settingsId = candidate;
        settingsModifiers = nextModifiers;
        settingsKey = nextKey;
        SettingsError = "";
        return true;
    }
    // Un seul point d'entrée pour la page de réglages : la nouvelle combinaison est enregistrée d'abord, l'ancienne n'est libérée
    // qu'une fois Windows d'accord, donc un refus laisse la liaison précédente intacte (retour arrière par construction).
    // LastCode == 1409 après un retour false signifie qu'une autre application possède la combinaison.
    internal bool TryRebind(string id, uint nextModifiers, uint nextKey, out string error)
    {
        LastCode = 0;
        switch (id)
        {
            case "dashboard":
                return TrySet(nextModifiers, nextKey, out error);
            case "palette":
                return TrySetPalette(nextModifiers, nextKey, out error);
            case "wallpaper":
                return TrySetWallpaper(nextModifiers, nextKey, out error);
            case "capture":
                return TrySetCapture(nextModifiers, nextKey, out error);
            case "pin":
                return TrySetPin(nextModifiers, nextKey, out error);
            case "settings":
                return TrySetSettings(nextModifiers, nextKey, out error);
            default:
                error = "Ce raccourci n’est pas modifiable.";
                return false;
        }
    }
    internal bool TrySetCapture(uint nextModifiers, uint nextKey, out string error)
    {
        error = "";
        if (!Valid(nextModifiers, nextKey))
        {
            error = "Choisis Ctrl ou Alt et une touche de fonction.";
            return false;
        }
        if ((nextModifiers == modifiers && nextKey == key && customId != 0) || (nextModifiers == paletteModifiers && nextKey == paletteKey
            && paletteId != 0) || (nextModifiers == pinModifiers && nextKey == pinKey && pinId != 0))
        {
            error = "Cette combinaison est déjà utilisée par Winlestia.";
            return false;
        }
        if (captureId != 0 && nextModifiers == captureModifiers && nextKey == captureKey)
        {
            CaptureError = "";
            return true;
        }
        int candidate = captureId == 9 ? 10 : 9;
        if (!Reg(window, candidate, nextModifiers | 0x4000, nextKey))
        {
            int code = LastCode;
            error = code == 1409 ? "Cette combinaison est déjà utilisée par une autre application."
                : "Windows refuse cette combinaison ("
                + code + ").";
            CaptureError = error;
            return false;
        }
        if (captureId != 0)
            Unreg(window, captureId);
        captureId = candidate;
        captureModifiers = nextModifiers;
        captureKey = nextKey;
        CaptureError = "";
        return true;
    }
    internal bool TrySetPin(uint nextModifiers, uint nextKey, out string error)
    {
        error = "";
        if (!Valid(nextModifiers, nextKey))
        {
            error = "Choisis Ctrl ou Alt et une touche de fonction.";
            return false;
        }
        if ((nextModifiers == modifiers && nextKey == key && customId != 0) || (nextModifiers == paletteModifiers && nextKey == paletteKey
            && paletteId != 0))
        {
            error = "Cette combinaison est déjà utilisée par Winlestia.";
            return false;
        }
        if ((nextModifiers == captureModifiers && nextKey == captureKey && captureId != 0))
        {
            error = "Cette combinaison lance déjà la capture.";
            return false;
        }
        if (pinId != 0 && nextModifiers == pinModifiers && nextKey == pinKey)
        {
            PinError = "";
            return true;
        }
        int candidate = pinId == 7 ? 8 : 7;
        if (!Reg(window, candidate, nextModifiers | 0x4000, nextKey))
        {
            int code = LastCode;
            error = code == 1409 ? "Cette combinaison est déjà utilisée par une autre application."
                : "Windows refuse cette combinaison ("
                + code + ").";
            PinError = error;
            return false;
        }
        if (pinId != 0)
            Unreg(window, pinId);
        pinId = candidate;
        pinModifiers = nextModifiers;
        pinKey = nextKey;
        PinError = "";
        return true;
    }
    internal bool TrySet(uint nextModifiers, uint nextKey, out string error)
    {
        error = "";
        if (!Valid(nextModifiers, nextKey))
        {
            error = "Choisis Ctrl ou Alt et une touche. Ctrl + Alt + Retour arrière est réservé au secours.";
            return false;
        }
        if (nextModifiers == paletteModifiers && nextKey == paletteKey && paletteId != 0)
        {
            error = "Ce raccourci ouvre déjà la commande rapide. Choisis une autre combinaison.";
            return false;
        }
        if (nextModifiers == modifiers && nextKey == key && (customId != 0 || nextModifiers == 3 && nextKey == 0x20 && legacy))
        {
            Error = "";
            return true;
        }
        if (nextModifiers == 3 && nextKey == 0x20 && legacy)
        {
            if (customId != 0)
                Unreg(window, customId);
            customId = 0;
            modifiers = nextModifiers;
            key = nextKey;
            Error = "";
            return true;
        }
        int candidate = customId == 3 ? 4 : 3;
        if (!Reg(window, candidate, nextModifiers | 0x4000, nextKey))
        {
            int code = LastCode;
            error = code == 1409 ? "Ce raccourci est déjà utilisé. Choisis une autre combinaison ; le précédent reste actif."
                : "Windows refuse ce raccourci (" + code + "). Le précédent reste actif.";
            Error = error;
            return false;
        }
        if (customId != 0)
            Unreg(window, customId);
        customId = candidate;
        modifiers = nextModifiers;
        key = nextKey;
        Error = "";
        return true;
    }
    internal bool TrySetPalette(uint nextModifiers, uint nextKey, out string error)
    {
        error = "";
        if (!Valid(nextModifiers, nextKey))
        {
            error = "Choisis Ctrl ou Alt et une touche. Ctrl + Alt + Retour arrière est réservé au secours.";
            return false;
        }
        if (nextModifiers == modifiers && nextKey == key && customId != 0)
        {
            error = "Cette combinaison ouvre déjà ton espace. Choisis une autre touche.";
            return false;
        }
        if (paletteId != 0 && nextModifiers == paletteModifiers && nextKey == paletteKey)
        {
            PaletteError = "";
            return true;
        }
        int candidate = paletteId == 5 ? 6 : 5;
        if (!Reg(window, candidate, nextModifiers | 0x4000, nextKey))
        {
            int code = LastCode;
            error = code == 1409 ? "Ce raccourci est déjà utilisé. Choisis une autre combinaison ; le précédent reste actif."
                : "Windows refuse ce raccourci (" + code + "). Le précédent reste actif.";
            PaletteError = error;
            return false;
        }
        if (paletteId != 0)
            Unreg(window, paletteId);
        paletteId = candidate;
        paletteModifiers = nextModifiers;
        paletteKey = nextKey;
        PaletteError = "";
        return true;
    }
    internal static bool Valid(uint modifiers, uint key) => (modifiers & ~7u) == 0 && (modifiers & 3) != 0 && (key is >= 0x70 and <= 0x87
        && key != 0x7B || key is >= 0x41 and <= 0x5A || key is >= 0x30 and <= 0x39 || key == 0x20) && !(modifiers == 3 && key == 0x08);
    internal static bool ValidWallpaper(uint modifiers, uint key) => (modifiers & ~15u) == 0 && (modifiers & 11u) != 0
        && (key is >= 0x70 and <= 0x87 && key != 0x7B || key is >= 0x41 and <= 0x5A || key is >= 0x30 and <= 0x39 || key == 0x20)
        && !(modifiers == 3 && key == 0x08);
    internal static IEnumerable<uint> Keys => Enumerable.Range(0x70, 24).Where(k => k != 0x7B).Concat(Enumerable.Range(0x41,
        26)).Concat(Enumerable.Range(0x30, 10)).Append(0x20).Select(k => (uint)k);
    internal static string KeyName(uint key) => key is >= 0x70 and <= 0x87 ? "F" + (key - 0x6F) : key == 0x20 ? "Espace"
        : ((char)key).ToString();
    internal static string Label(uint modifiers, uint key) => ((modifiers & 2) != 0 ? "Ctrl + " : "") + ((modifiers & 1) != 0 ? "Alt + "
        : "") + ((modifiers & 4) != 0 ? "Maj + " : "") + ((modifiers & 8) != 0 ? "Win + " : "") + KeyName(key);
    public void Dispose()
    {
        if (customId != 0)
            Unreg(window, customId);
        if (paletteId != 0)
            Unreg(window, paletteId);
        if (pinId != 0)
            Unreg(window, pinId);
        if (captureId != 0)
            Unreg(window, captureId);
        if (wallpaperId != 0)
            Unreg(window, wallpaperId);
        if (settingsId != 0)
            Unreg(window, settingsId);
        if (legacy)
            Unreg(window, 1);
        if (rescue)
            Unreg(window, 2);
    }
}
