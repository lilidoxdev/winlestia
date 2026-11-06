// language: C# 12, file: ShortcutCatalog.cs, target: Windows 11 / .NET 8, settings-page view of every keyboard shortcut: catalogue, combo text, Windows-reserved rejection, conflicts, rebind with rollback
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("SettingsRefonte")]
namespace PanneauxGlass;
// Combo text shared with the QML page: "Win+Alt+W", "Ctrl+F1", "Ctrl+Alt+Maj+F". Output order Win, Ctrl, Alt, Maj; parsing accepts any order, "Shift"/"Maj", "Space"/"Espace".
// Modifier bits are the RegisterHotKey ones: Alt 1, Ctrl 2, Shift 4, Win 8.
internal static class ShortcutCombo
{
    internal static string Format(uint modifiers, uint key) => string.Join("+", new[] { (modifiers & 8) != 0 ? "Win" : "", (modifiers & 2) != 0 ? "Ctrl" : "", (modifiers & 1) != 0 ? "Alt" : "", (modifiers & 4) != 0 ? "Maj" : "", KeyText(key) }.Where(t => t.Length > 0));
    internal static string KeyText(uint key) => key switch { >= 0x70 and <= 0x87 => "F" + (key - 0x6F), 0x20 => "Espace", 0x08 => "Backspace", 0x09 => "Tab", 0x0D => "Entrée", 0x25 => "←", 0x26 => "↑", 0x27 => "→", 0x28 => "↓", 0x2C => "Impr. écran", 0xBB => "Plus", 0xBD => "Moins", _ => key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A ? ((char)key).ToString() : "0x" + key.ToString("X2") };
    static uint KeyFrom(string token)
    {
        string t = token.Trim().ToLowerInvariant();
        if (t.Length == 1 && (t[0] is >= 'a' and <= 'z'))
            return (uint)char.ToUpperInvariant(t[0]);
        if (t.Length == 1 && t[0] is >= '0' and <= '9')
            return t[0];
        if (t.Length >= 2 && t[0] == 'f' && int.TryParse(t[1..], out int f) && f is >= 1 and <= 24)
            return (uint)(0x6F + f);
        return t switch
        {
            "space" or "espace" => 0x20u,
            "backspace" or "retour arrière" or "retour arriere" => 0x08u,
            "tab" => 0x09u,
            "enter" or "entrée" or "entree" or "retour" => 0x0Du,
            "left" or "gauche" or "←" => 0x25u,
            "up" or "haut" or "↑" => 0x26u,
            "right" or "droite" or "→" => 0x27u,
            "down" or "bas" or "↓" => 0x28u,
            "printscreen" or "prtsc" or "impr. écran" or "impr. ecran" => 0x2Cu,
            "plus" or "+" => 0xBBu,
            "moins" or "minus" or "-" => 0xBDu,
            _ => 0u
        };
    }
    internal static bool TryParse(string? text, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var parts = text.Trim().Split('+').Select(p => p.Trim()).ToList();
        if (parts.Count >= 2 && parts[^1].Length == 0 && parts[^2].Length == 0)
        {
            parts.RemoveRange(parts.Count - 2, 2);
            parts.Add("+");
        }
        uint found = 0;
        bool haveKey = false;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control" or "ctl":
                    modifiers |= 2;
                    break;
                case "alt":
                    modifiers |= 1;
                    break;
                case "shift" or "maj" or "majuscule":
                    modifiers |= 4;
                    break;
                case "win" or "windows" or "meta" or "super":
                    modifiers |= 8;
                    break;
                default:
                    if (haveKey)
                        return false;
                    found = KeyFrom(part);
                    if (found == 0)
                        return false;
                    haveKey = true;
                    break;
            }
        }
        key = found;
        return haveKey && modifiers != 0;
    }
    // Combinations Windows (or the app being used) owns outright. A refusal here never reaches RegisterHotKey.
    internal static bool IsWindowsReserved(uint modifiers, uint key)
    {
        if ((modifiers & 8) == 0)
            return modifiers == 1 && key is 0x73 or 0x20;   // Alt+F4, Alt+Space (system menu)
        uint rest = modifiers & ~8u;
        bool arrow = key is >= 0x25 and <= 0x28;
        return rest switch
        {
            0 => true,                                                                            // Win + anything: Start, Search, Explorer, Run, Settings, snap, widgets, digits…
            2 => arrow || key is 0x44 or 0x73 or 0x0D,                                              // Win+Ctrl: virtual desktops, narrator, new desktop, close desktop
            4 => key is 0x53 or 0x4D || arrow,                                                      // Win+Shift: screenshot, restore, move window
            1 => key is 0x52 or 0x47 or 0x42 or 0x4B or 0x44 or 0x2C or 0x0D,                     // Win+Alt: record, Game Bar, HDR, cast, agenda, screenshot, media
            _ => false
        };
    }
}
internal sealed record ShortcutDef(string Id, string Group, string Label, string Scope, bool Configurable, string Reason, uint DefaultModifiers, uint DefaultKey, string FixedCombo, (uint m, uint k)[] Fixed);
internal sealed record ShortcutResult(bool Ok, string Message, string ConflictId);
internal static class ShortcutCatalog
{
    const string TileReason = "Fixe pour l’instant ; actif uniquement quand la disposition automatique est activée.";
    static (uint, uint)[] Keys(uint m, params uint[] k) => k.Select(x => (m, x)).ToArray();
    internal static readonly ShortcutDef[] All ={
        new("dashboard","Shell","Ouvrir le tableau de bord","global",true,"",2,0x70,"",Array.Empty<(uint,uint)>()),
        new("dashboardFixed","Shell","Tableau de bord (secours)","global",false,"Raccourci de secours fixe : il ouvre toujours le tableau de bord, même si l’autre raccourci est réassigné.",3,0x20,"Ctrl+Alt+Espace",Keys(3,0x20)),
        new("settings","Shell","Ouvrir les paramètres","global",true,"",9,0x53,"",Array.Empty<(uint,uint)>()),
        new("palette","Shell","Commande rapide","global",true,"",2,0x71,"",Array.Empty<(uint,uint)>()),
        new("wallpaper","Apparence","Changer de fond d’écran","global",true,"",9,0x57,"",Array.Empty<(uint,uint)>()),
        new("capture","Outils","Capturer l’écran","global",true,"",2,0x72,"",Array.Empty<(uint,uint)>()),
        new("pin","Outils","Épingler au premier plan","global",true,"",2,0x77,"",Array.Empty<(uint,uint)>()),
        new("dictation","Outils","Dictée (touche maintenue)","global",false,"Se règle dans Médias et audio › Dictée (touche maintenue).",0,0,"",Array.Empty<(uint,uint)>()),
        new("rescue","Secours","Restaurer le bureau","global",false,"Réservé à la restauration du bureau : il doit fonctionner même si Winlestia ne répond plus.",3,0x08,"Ctrl+Alt+Backspace",Keys(3,0x08)),
        new("tileFocus","Disposition automatique","Passer le focus à la fenêtre voisine","tiling",false,TileReason,3,0,"Ctrl+Alt+Flèches",Keys(3,0x25,0x26,0x27,0x28)),
        new("tileSwap","Disposition automatique","Échanger avec la fenêtre voisine","tiling",false,TileReason,7,0,"Ctrl+Alt+Maj+Flèches",Keys(7,0x25,0x26,0x27,0x28)),
        new("tileFloat","Disposition automatique","Basculer la fenêtre en flottant","tiling",false,TileReason,7,0x46,"Ctrl+Alt+Maj+F",Keys(7,0x46)),
        new("tileGrow","Disposition automatique","Agrandir la zone de la fenêtre","tiling",false,TileReason,7,0xBB,"Ctrl+Alt+Maj+Plus",Keys(7,0xBB,0x6B)),
        new("tileShrink","Disposition automatique","Réduire la zone de la fenêtre","tiling",false,TileReason,7,0xBD,"Ctrl+Alt+Maj+Moins",Keys(7,0xBD,0x6D,0x36)),
        new("tileExclude","Disposition automatique","Exclure l’application de la disposition","tiling",false,TileReason,7,0x58,"Ctrl+Alt+Maj+X",Keys(7,0x58)),
    };
    internal static ShortcutDef? Find(string id) => All.FirstOrDefault(d => d.Id == id);
    // Display name of the dictation key (virtual-key code).
    internal static string DictationKeyName(int vk) => vk switch { 0xA5 => "AltGr", 0xA4 => "Alt gauche", 0xA2 => "Ctrl gauche", 0xA3 => "Ctrl droit", 0xA0 => "Maj gauche", 0xA1 => "Maj droit", 0x5B => "Win gauche", 0x5C => "Win droit", 0x14 => "Verr. maj.", >= 0x70 and <= 0x87 => "F" + (vk - 0x6F), >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(), _ => "Touche " + vk };
}
// Applies a shortcut change coming from the settings page: validate, reject Windows-reserved, detect every conflict (configurable, fixed and tiling), rebind with rollback, persist.
internal sealed class ShortcutManager
{
    readonly GlobalShortcut hotkeys; readonly Func<Preferences> prefs; readonly Action save; readonly Action<string>? changed;
    int nonce; ShortcutResult? last; string lastId = "";
    internal ShortcutManager(GlobalShortcut hotkeys, Func<Preferences> prefs, Action save, Action<string>? changed = null)
    {
        this.hotkeys = hotkeys;
        this.prefs = prefs;
        this.save = save;
        this.changed = changed;
    }
    ShortcutPreferences? Pref(string id)
    {
        var p = prefs();
        return id switch
        {
            "dashboard" => p.Shortcut,
            "palette" => p.PaletteShortcut,
            "wallpaper" => p.WallpaperShortcut,
            "capture" => p.CaptureShortcut,
            "pin" => p.PinShortcut,
            "settings" => p.SettingsShortcut,
            _ => null
        };
    }
    internal (uint m, uint k) Current(string id)
    {
        if (id == "wallpaper" && hotkeys.WallpaperRegistered)
            return (hotkeys.WallpaperModifiers, hotkeys.WallpaperKey);
        var p = Pref(id);
        return p == null ? (0, 0) : (p.Modifiers, p.Key);
    }
    internal string ComboOf(ShortcutDef d)
    {
        if (d.Id == "dictation")
            return ShortcutCatalog.DictationKeyName(prefs().DictationKey);
        if (!d.Configurable)
            return d.FixedCombo;
        var c = Current(d.Id);
        return ShortcutCombo.Format(c.m, c.k);
    }
    internal static string DefaultCombo(ShortcutDef d) => d.Id == "dictation" ? "" : d.Configurable ? ShortcutCombo.Format(d.DefaultModifiers, d.DefaultKey) : d.FixedCombo;
    internal object[] List() => ShortcutCatalog.All.Select(d => (object)new { id = d.Id, group = d.Group, label = d.Label, combo = ComboOf(d), defaultCombo = DefaultCombo(d), configurable = d.Configurable, reason = d.Reason, scope = d.Scope, active = !d.Configurable || hotkeys.IsActive(d.Id) }).ToArray();
    internal object Status() => new { nonce, id = lastId, ok = last?.Ok ?? true, message = last?.Message ?? "", conflictId = last?.ConflictId ?? "" };
    // First other binding that already uses this combination (fixed and tiling ones included), or null.
    internal ShortcutDef? FindConflict(string id, uint m, uint k)
    {
        foreach (var d in ShortcutCatalog.All)
        {
            if (d.Id == id)
                continue;
            if (d.Configurable)
            {
                var c = Current(d.Id);
                if (c.m == m && c.k == k)
                    return d;
            }
            else if (d.Fixed.Any(f => f.m == m && f.k == k))
                return d;
        }
        return null;
    }
    ShortcutResult Done(string id, ShortcutResult r)
    {
        nonce++;
        last = r;
        lastId = id;
        return r;
    }
    internal ShortcutResult Reset(string id)
    {
        var d = ShortcutCatalog.Find(id);
        if (d == null || !d.Configurable)
            return Done(id, new(false, "Ce raccourci n’est pas modifiable.", ""));
        return Apply(id, ShortcutCombo.Format(d.DefaultModifiers, d.DefaultKey), true);
    }
    internal ShortcutResult Apply(string id, string? combo, bool reset = false)
    {
        var def = ShortcutCatalog.Find(id);
        if (def == null || !def.Configurable)
            return Done(id, new(false, def?.Reason is { Length: > 0 } r ? r : "Ce raccourci n’est pas modifiable.", ""));
        if (!ShortcutCombo.TryParse(combo, out uint m, out uint k))
            return Done(id, new(false, "Combinaison non reconnue. Maintenez un ou plusieurs modificateurs puis une touche.", ""));
        if (ShortcutCombo.IsWindowsReserved(m, k))
            return Done(id, new(false, "Combinaison réservée par Windows.", ""));
        bool win = id is "wallpaper" or "settings";
        if (!(win ? GlobalShortcut.ValidWallpaper(m, k) : GlobalShortcut.Valid(m, k)))
            return Done(id, new(false, win ? "Choisissez Ctrl, Alt ou Windows avec une touche (F1 à F24 sauf F12, lettre, chiffre ou Espace)." : "Choisissez Ctrl ou Alt avec une touche (F1 à F24 sauf F12, lettre, chiffre ou Espace).", ""));
        var now = Current(id);
        if (now.m == m && now.k == k && hotkeys.IsActive(id))
            return Done(id, new(true, reset ? "Déjà au réglage d’origine." : "Raccourci inchangé.", ""));
        var clash = FindConflict(id, m, k);
        if (clash != null)
            return Done(id, new(false, "Déjà utilisé par « " + clash.Label + " ». Choisissez une autre combinaison ou modifiez d’abord « " + clash.Label + " ».", clash.Id));
        if (!hotkeys.TryRebind(id, m, k, out string error))
            return Done(id, new(false, GlobalShortcut.LastCode == 1409 ? "Une autre application utilise déjà cette combinaison." : error, ""));
        var p = prefs();
        switch (id)
        {
            case "dashboard":
                p.Shortcut = new()
                {
                    Modifiers = m,
                    Key = k,
                    View = p.Shortcut.View
                };
                p.Shortcut.Normalize();
                break;
            case "palette":
                p.PaletteShortcut = new()
                {
                    Modifiers = m,
                    Key = k
                };
                break;
            case "wallpaper":
                p.WallpaperShortcut = new()
                {
                    Modifiers = m,
                    Key = k
                };
                break;
            case "capture":
                p.CaptureShortcut = new()
                {
                    Modifiers = m,
                    Key = k
                };
                break;
            case "pin":
                p.PinShortcut = new()
                {
                    Modifiers = m,
                    Key = k
                };
                break;
            case "settings":
                p.SettingsShortcut = new()
                {
                    Modifiers = m,
                    Key = k
                };
                break;
        }
        save();
        changed?.Invoke(id);
        return Done(id, new(true, (reset ? "Raccourci rétabli : " : "Raccourci enregistré : ") + ShortcutCombo.Format(m, k) + ".", ""));
    }
}
