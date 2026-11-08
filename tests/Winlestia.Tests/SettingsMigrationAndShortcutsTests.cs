// Settings redesign backend checks: schema migration against a real legacy preferences file, and the shortcut pipeline (catalogue, reserved, conflicts, rebind, rollback, reset).
// RegisterHotKey is replaced by an in-memory fake (GlobalShortcut.TestRegister), so no real hotkey of the running shell is touched. Data lives under %TEMP%\WinlestiaSettingsRefonte.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PanneauxGlass;
namespace Winlestia.Tests;
public class SettingsMigrationAndShortcutsTests
{
    static void Check(bool ok, string name) => Assert.True(ok, name);
    static bool Same(JsonElement a, JsonElement b)
    {
        bool boolA = a.ValueKind is JsonValueKind.True or JsonValueKind.False, boolB = b.ValueKind is JsonValueKind.True or JsonValueKind.False;
        if (a.ValueKind != b.ValueKind)
            return false;
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    var pa = a.EnumerateObject().ToList();
                    var pb = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                    return pa.Count == pb.Count && pa.All(p => pb.TryGetValue(p.Name, out var v) && Same(p.Value, v));
                }
            case JsonValueKind.Array:
                {
                    var la = a.EnumerateArray().ToList();
                    var lb = b.EnumerateArray().ToList();
                    return la.Count == lb.Count && la.Zip(lb).All(t => Same(t.First, t.Second));
                }
            case JsonValueKind.Number:
                return a.GetDouble() == b.GetDouble();
            case JsonValueKind.String:
                return a.GetString() == b.GetString();
            default:
                return true;
        }
    }
    static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "src", "backend", "Bridge", "QtShellBridge.cs")))
                return d.FullName;
        throw new DirectoryNotFoundException("racine du depot introuvable depuis " + AppContext.BaseDirectory);
    }
    static JsonDocument Doc(string file) => JsonDocument.Parse(File.ReadAllText(file));
    [Fact]
    public void AllChecks()
    {
        string root = Path.Combine(Path.GetTempPath(), "WinlestiaSettingsRefonte");
        if (Directory.Exists(root))
            Directory.Delete(root, true);
        Directory.CreateDirectory(root);
        // Fixture : fichier de preferences pre-refonte. WINLESTIA_LEGACY_PREFS peut pointer vers une vraie sauvegarde ;
        // sinon un fichier synthetique minimal est genere (sans SettingsSchema ni DockVisible).
        string legacy = Environment.GetEnvironmentVariable("WINLESTIA_LEGACY_PREFS") ?? "";
        if (!File.Exists(legacy))
        {
            legacy = Path.Combine(root, "legacy-preferences-glass.json");
            File.WriteAllText(legacy, "{\"ReplaceTaskbar\":true,\"TopActivationMargin\":5}");
        }
        try
        {
            Migration(root, legacy);
            Shortcuts();
        }
        finally { TempDir.Delete(root); }
    }
    static void Migration(string root, string legacy)
    {
        string data = Path.Combine(root, "data");
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", data);
        string file = Path.Combine(data, "preferences-glass.json");
        File.Copy(legacy, file);
        using var before = Doc(file);
        Check(!before.RootElement.TryGetProperty("SettingsSchema", out _) && !before.RootElement.TryGetProperty("DockVisible", out _),
              "(m) fixture is a pre-refonte file (no SettingsSchema / DockVisible)");
        Store.Load();
        var s = Store.State;
        bool replace = before.RootElement.GetProperty("ReplaceTaskbar").GetBoolean();
        Check(s.SettingsSchema == 2, "(m) schema becomes 2");
        Check(s.DockVisible == replace, "(m) DockVisible = legacy ReplaceTaskbar (" + replace + ")");
        Check(s.ReplaceTaskbar == replace, "(m) ReplaceTaskbar unchanged");
        Check(s.BookmarksRailVisible && s.SocialRailVisible, "(m) rails visible by default");
        Check(s.SettingsShortcut.Modifiers == 9 && s.SettingsShortcut.Key == 0x53, "(m) SettingsShortcut default Win+Alt+S");
        Store.Save();
        using var after = Doc(file);
        var changed = new List<string>();
        var dropped = new List<string>();
        foreach (var p in before.RootElement.EnumerateObject())
        {
            if (!after.RootElement.TryGetProperty(p.Name, out var now))
            {
                dropped.Add(p.Name);
                continue;
            }
            if (!Same(p.Value, now))
                changed.Add(p.Name);
        }
        Console.WriteLine("  info: "
            + before.RootElement.EnumerateObject().Count()
            + " legacy keys, changed=["
            + string.Join(",", changed)
            + "] dropped=["
            + string.Join(",", dropped)
            + "]");
        Check(changed.Count == 0, "(m) every pre-existing key keeps its value after load+save");
        Check(dropped.Count == 0, "(m) no pre-existing key dropped from the JSON");
        Check(after.RootElement.GetProperty("SettingsSchema").GetInt32() == 2 && after.RootElement.GetProperty("DockVisible").GetBoolean() == replace,
              "(m) new keys written");
        string text1 = File.ReadAllText(file);
        Store.Load();
        Store.Save();
        string text2 = File.ReadAllText(file);
        Check(text1 == text2, "(m) save/reload round-trip is byte-stable");
        Check(Store.State.DockVisible == replace && Store.State.SettingsSchema == 2, "(m) reload keeps migrated values");
        Store.State.DockVisible = !replace;
        Store.Save();
        Store.Load();
        Check(Store.State.DockVisible == !replace && Store.State.ReplaceTaskbar == replace, "(m) DockVisible and ReplaceTaskbar are independent and persisted");
        var p2 = new Preferences { ReplaceTaskbar = false };
        Store.MigrateSettings(p2);
        Check(p2.SettingsSchema == 2 && !p2.DockVisible, "(m) legacy ReplaceTaskbar=false -> DockVisible=false");
        var p3 = new Preferences { SettingsSchema = 2, ReplaceTaskbar = true, DockVisible = false };
        Store.MigrateSettings(p3);
        Check(!p3.DockVisible, "(m) schema 2 is never migrated again");
        var p4 = new Preferences();
        p4.SettingsShortcut = new()
        {
            Modifiers = 0,
            Key = 0x41
        };
        p4.Normalize();
        Check(p4.SettingsShortcut.Modifiers == 9 && p4.SettingsShortcut.Key == 0x53, "(m) invalid SettingsShortcut repaired to the default");
    }
    // ---- shortcuts, with an in-memory RegisterHotKey ----
    static readonly Dictionary<int, (uint m, uint k)> reg = new();
    static readonly HashSet<(uint, uint)> foreign = new();
    static (GlobalShortcut g, ShortcutManager m, Preferences p, int[] saves) Rig()
    {
        reg.Clear();
        foreign.Clear();
        GlobalShortcut.TestRegister = (w, id, m, k) =>
        {
            var key = (m & ~0x4000u, k);
            if (foreign.Contains(key) || reg.ContainsKey(id) || reg.Values.Contains(key))
                return 1409;
            reg[id] = key;
            return 0;
        };
        GlobalShortcut.TestUnregister = (w, id) => reg.Remove(id);
        var p = new Preferences();
        var g = new GlobalShortcut(IntPtr.Zero, p.Shortcut, p.PaletteShortcut, p.PinShortcut, p.CaptureShortcut, p.WallpaperShortcut, p.SettingsShortcut);
        var saves = new[] { 0 };
        var m = new ShortcutManager(g, () => p, () => saves[0]++);
        return (g, m, p, saves);
    }
    static string Combo(ShortcutManager m, string id)
    {
        foreach (dynamic o in m.List())
        {
            if ((string)o.id == id)
                return (string)o.combo;
        }
        return "?";
    }
    static int KeyOf(uint mods, uint key) => reg.First(kv => kv.Value == (mods, key)).Key;
    static void Shortcuts()
    {
        var (g, m, p, saves) = Rig();
        var ids = m.List().Select(o => (string)((dynamic)o).id).ToArray();
        Check(string.Join(",",
                          ids) == "dashboard,dashboardFixed,settings,palette,wallpaper,capture,pin,dictation,rescue,tileFocus,tileSwap,tileFloat,tileGrow,tileShrink,tileExclude",
              "(s) catalogue ids and order");
        Check(Combo(m, "dashboard") == "Ctrl+F1"
            && Combo(m, "settings") == "Win+Alt+S"
            && Combo(m, "wallpaper") == "Win+Alt+W"
            && Combo(m, "capture") == "Ctrl+F3"
            && Combo(m, "pin") == "Ctrl+F8"
            && Combo(m, "palette") == "Ctrl+F2",
              "(s) default combos displayed");
        Check(Combo(m, "dictation") == "AltGr" && Combo(m, "rescue") == "Ctrl+Alt+Backspace" && Combo(m, "dashboardFixed") == "Ctrl+Alt+Espace",
              "(s) dictation / rescue / fixed dashboard displayed");
        Check(g.SettingsActive && g.MatchesSettings(KeyOf(9, 0x53)), "(s) settings hotkey registered at startup and dispatched");
        Check(ShortcutCombo.TryParse("ctrl+shift+f5", out var pm, out var pk) && pm == 6 && pk == 0x74 && ShortcutCombo.Format(pm, pk) == "Ctrl+Maj+F5",
              "(s) parse/format Ctrl+Maj+F5");
        Check(ShortcutCombo.TryParse("Ctrl+Alt+Space", out pm, out pk) && pm == 3 && pk == 0x20, "(s) Space accepted");
        Check(!ShortcutCombo.TryParse("Ctrl+", out _, out _) && !ShortcutCombo.TryParse("K", out _, out _) && !ShortcutCombo.TryParse("Ctrl+A+B", out _, out _),
              "(s) malformed combos refused");
        foreach (var (id, c) in new[]
        {
            ("settings", "Win+L"),
            ("settings", "Win+1"),
            ("wallpaper", "Win+Alt+R"),
            ("settings", "Win+Ctrl+Left"),
            ("settings", "Win+Shift+S"),
            ("wallpaper", "Win+Alt+PrtSc"),
            ("capture", "Alt+F4"),
            ("settings", "Win+Tab"),
            ("settings", "Win+Alt+D")
        })
        {
            var r = m.Apply(id, c);
            Check(!r.Ok && r.Message == "Combinaison réservée par Windows.", $"(s) reserved rejected: {id} <- {c}");
        }
        Check(m.Apply("settings", "Win+Alt+Q").Ok && reg.Values.Contains((9u, 0x51u)) && !reg.Values.Contains((9u, 0x53u)),
              "(s) Win+Alt+Q is not reserved; settings rebound (old released, new registered)");
        Check(m.Apply("settings", "Win+Alt+S").Ok, "(s) settings back to Win+Alt+S");
        int baseSaves = saves[0];
        var bad = m.Apply("dashboard", "Win+Alt+Q");
        Check(!bad.Ok, "(s) Win refused for the dashboard shortcut (" + bad.Message + ")");
        Check(!m.Apply("pin", "Ctrl+F12").Ok, "(s) F12 refused");
        var c1 = m.Apply("palette", "Ctrl+F1");
        Check(!c1.Ok
            && c1.ConflictId == "dashboard"
            && c1.Message == "Déjà utilisé par « Ouvrir le tableau de bord ». Choisissez une autre combinaison ou modifiez d’abord « Ouvrir le tableau de bord ».",
              "(s) conflict with configurable dashboard, exact message");
        var c2 = m.Apply("capture", "Ctrl+Alt+Space");
        Check(!c2.Ok && c2.ConflictId == "dashboardFixed", "(s) conflict with the fixed dashboard shortcut");
        var c3 = m.Apply("pin", "Ctrl+Alt+Maj+F");
        Check(!c3.Ok && c3.ConflictId == "tileFloat", "(s) conflict with tiling float");
        var c4 = m.Apply("pin", "Ctrl+Alt+Maj+6");
        Check(!c4.Ok && c4.ConflictId == "tileShrink", "(s) conflict with tiling shrink (digit 6)");
        Check(m.FindConflict("pin", 3, 0x25)?.Id == "tileFocus"
            && m.FindConflict("pin", 7, 0x28)?.Id == "tileSwap"
            && m.FindConflict("pin", 7, 0xBB)?.Id == "tileGrow"
            && m.FindConflict("pin", 3, 0x08)?.Id == "rescue",
              "(s) tiling arrows / plus and rescue detected by FindConflict");
        var c5 = m.Apply("capture", "Ctrl+F8");
        Check(!c5.Ok && c5.ConflictId == "pin", "(s) conflict capture/pin");
        var c6 = m.Apply("wallpaper", "Win+Alt+S");
        Check(!c6.Ok && c6.ConflictId == "settings", "(s) conflict wallpaper/settings");
        Check(saves[0] == baseSaves && p.PaletteShortcut.Key == 0x71, "(s) failed attempts neither saved nor changed prefs");
        int before = saves[0];
        var ok = m.Apply("palette", "Ctrl+Alt+K");
        Check(ok.Ok && Combo(m, "palette") == "Ctrl+Alt+K" && p.PaletteShortcut.Modifiers == 3 && p.PaletteShortcut.Key == 0x4B && saves[0] == before + 1,
              "(s) palette -> Ctrl+Alt+K saved");
        Check(reg.Values.Contains((3u, 0x4Bu)) && !reg.Values.Contains((2u, 0x71u)) && g.MatchesPalette(KeyOf(3, 0x4B)),
              "(s) old palette hotkey released, new registered and dispatched");
        Check(m.Apply("palette", "Ctrl+Alt+K").Ok && saves[0] == before + 1, "(s) same combo is a no-op (no save)");
        Check(m.Apply("capture", "Ctrl+F2").Ok && reg.Values.Contains((2u, 0x71u)), "(s) freed combo reusable");
        var rc = m.Reset("palette");
        Check(!rc.Ok && rc.ConflictId == "capture", "(s) reset blocked by the action now using the default combo");
        Check(m.Reset("capture").Ok && Combo(m, "capture") == "Ctrl+F3", "(s) reset capture -> Ctrl+F3");
        var rp = m.Reset("palette");
        Check(rp.Ok && Combo(m, "palette") == "Ctrl+F2" && p.PaletteShortcut.Modifiers == 2 && p.PaletteShortcut.Key == 0x71, "(s) reset palette -> Ctrl+F2");
        Check(!m.Reset("rescue").Ok && !m.Apply("rescue", "Ctrl+F5").Ok && !m.Apply("dictation", "Ctrl+F5").Ok && !m.Apply("tileFloat", "Ctrl+F5").Ok,
              "(s) non-configurable actions refuse changes");
        foreign.Add((3u, 0x4Au));
        int nonceBefore = (int)((dynamic)m.Status()).nonce;
        before = saves[0];
        var fail = m.Apply("pin", "Ctrl+Alt+J");
        Check(!fail.Ok && fail.Message == "Une autre application utilise déjà cette combinaison." && fail.ConflictId == "",
              "(s) 1409 -> French message, no conflict id");
        Check(reg.Values.Contains((2u, 0x77u))
            && !reg.Values.Contains((3u, 0x4Au))
            && p.PinShortcut.Key == 0x77
            && p.PinShortcut.Modifiers == 2
            && saves[0] == before
            && Combo(m, "pin") == "Ctrl+F8",
              "(s) rollback: old pin hotkey still registered, prefs and save untouched");
        Check(g.MatchesPin(KeyOf(2, 0x77)), "(s) old pin hotkey still dispatches after rollback");
        dynamic st = m.Status();
        Check((int)st.nonce == nonceBefore + 1 && (string)st.id == "pin" && !(bool)st.ok, "(s) shortcutStatus nonce increments, carries id/ok");
        GlobalShortcut.TestRegister = (w, id, mm, k) => (mm & ~0x4000u) == 3 && k == 0x4C ? 5 : 0;
        var denied = m.Apply("capture", "Ctrl+Alt+L");
        Check(!denied.Ok && denied.Message.Contains("(5)") && Combo(m, "capture") == "Ctrl+F3", "(s) other Win32 error reported, previous binding kept");
        Inactive();
        Collision();
        Dock();
        GlobalShortcut.TestRegister = null;
        GlobalShortcut.TestUnregister = null;
    }
    // (a) a shortcut whose startup registration failed is reported inactive, and re-applying the same combo retries instead of saying "unchanged".
    static void Inactive()
    {
        var (g0, m0, p0, s0) = Rig();
        g0.Dispose();
        reg.Clear();
        foreign.Clear();
        foreign.Add((2u, 0x77u));   // another app owns Ctrl+F8 at startup
        var p = new Preferences();
        var g = new GlobalShortcut(IntPtr.Zero, p.Shortcut, p.PaletteShortcut, p.PinShortcut, p.CaptureShortcut, p.WallpaperShortcut, p.SettingsShortcut);
        var saves = new[] { 0 };
        var m = new ShortcutManager(g, () => p, () => saves[0]++);
        Func<string, bool?> active = id => m.List().Select(o => (dynamic)o).Where(o => (string)o.id == id).Select(o => (bool?)(bool)o.active).FirstOrDefault();
        Check(active("pin") == false && active("palette") == true && active("rescue") == true, "(a) failed registration listed active=false, others true");
        var r = m.Apply("pin", "Ctrl+F8");
        Check(!r.Ok && r.Message == "Une autre application utilise déjà cette combinaison.",
              "(a) same combo while inactive retries and reports the real error, not 'inchangé'");
        foreign.Clear();
        var r2 = m.Apply("pin", "Ctrl+F8");
        Check(r2.Ok && r2.Message != "Raccourci inchangé." && active("pin") == true,
              "(a) retry succeeds once the combo is free and the shortcut becomes active");
        Check(m.Apply("pin", "Ctrl+F8").Message == "Raccourci inchangé.", "(a) active shortcut with same combo is 'inchangé'");
    }
    // (b) Normalize: Settings shortcut equal to another configured combo is moved to the first free default.
    static void Collision()
    {
        var p = new Preferences();
        p.WallpaperShortcut = new()
        {
            Modifiers = 9,
            Key = 0x53
        };
        p.Normalize();
        Check(p.SettingsShortcut.Modifiers == 3 && p.SettingsShortcut.Key == 0x79, "(c) Settings = Wallpaper combo -> Ctrl+Alt+F10");
        var q = new Preferences();
        q.WallpaperShortcut = new()
        {
            Modifiers = 9,
            Key = 0x53
        };
        q.PinShortcut = new()
        {
            Modifiers = 3,
            Key = 0x79
        };
        q.Normalize();
        Check(q.SettingsShortcut.Modifiers == 3 && q.SettingsShortcut.Key == 0x7A, "(c) F10 also taken -> Ctrl+Alt+F11");
        var ok = new Preferences();
        ok.Normalize();
        Check(ok.SettingsShortcut.Modifiers == 9 && ok.SettingsShortcut.Key == 0x53, "(c) no collision keeps Win+Alt+S");
    }
    static void Dock()
    {
        // Source layout is not part of the contract: compare with all whitespace removed.
        string src = System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(Path.Combine(RepoRoot(), "src", "backend", "Bridge", "QtShellBridge.Actions.Settings.cs")),
            @"\s+",
            "");
        Check(src.Contains("case\"dockVisible\":Store.State.DockVisible=Flag(c.Args,\"enabled\");if(!Store.State.DockVisible&&owner.TaskbarEnabled)owner.SetTaskbar(false);"),
              "(d) hiding the dock while it replaces the Windows bar restores the Windows bar");
    }
}
