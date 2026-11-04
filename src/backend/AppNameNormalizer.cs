// Display-name normalisation for Start-menu shortcuts / apps. Pure (no I/O), linked into tests/AppNameNormalizer.
// Keep in sync with Clean-Name in src/backend/Inventory.ps1 (same rules, same order).
using System;
using System.Text.RegularExpressions;

namespace PanneauxGlass;

internal static class AppNameNormalizer
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>Trailing ".exe - Raccourci" (French shortcut suffix): "Foo.exe - Raccourci" -> "Foo".</summary>
    static readonly Regex ExeShortcutSuffix = new(@"\.exe\s*-\s*Raccourci$", Opts);

    /// <summary>Trailing " - Raccourci": "Foo - Raccourci" -> "Foo".</summary>
    static readonly Regex ShortcutSuffix = new(@"\s*-\s*Raccourci$", Opts);

    /// <summary>Leading "Lancer " (French "launch"): "Lancer Foo" -> "Foo".</summary>
    static readonly Regex LaunchPrefix = new(@"^Lancer\s+", Opts);

    /// <summary>Whole name "VLC Media Player.exe": -> "VLC Media Player".</summary>
    static readonly Regex VlcExe = new(@"^VLC Media Player\.exe$", Opts);

    /// <summary>Registered/trademark signs, including the mojibake "Â®": "Foo®" -> "Foo".</summary>
    static readonly Regex TrademarkSigns = new("Â?®|™", Opts);

    /// <summary>Trailing " - Windows10": "Foo - Windows10" -> "Foo".</summary>
    static readonly Regex Windows10Suffix = new(@"\s*-\s*Windows10$", Opts);

    /// <summary>Whole name "CrimsonDesert": -> "Crimson Desert".</summary>
    static readonly Regex CrimsonDesert = new("^CrimsonDesert$", Opts);

    /// <summary>Whole name "HogwartsLegacy": -> "Hogwarts Legacy".</summary>
    static readonly Regex HogwartsLegacy = new("^HogwartsLegacy$", Opts);

    /// <summary>
    /// Applies the display-name rules in a fixed order, then trims. A null input yields "".
    /// Example: "Lancer Foo.exe - Raccourci" -> "Foo"; "HogwartsLegacy" -> "Hogwarts Legacy".
    /// </summary>
    internal static string Normalize(string raw)
    {
        string n = ExeShortcutSuffix.Replace(raw ?? "", "");
        n = ShortcutSuffix.Replace(n, "");
        n = LaunchPrefix.Replace(n, "");
        n = VlcExe.Replace(n, "VLC Media Player");
        n = TrademarkSigns.Replace(n, "");
        n = Windows10Suffix.Replace(n, "");
        n = CrimsonDesert.Replace(n, "Crimson Desert");
        n = HogwartsLegacy.Replace(n, "Hogwarts Legacy");
        return n.Trim();
    }
}
