// Display-name normalisation for Start-menu shortcuts / apps. Pure (no I/O), linked into tests/AppNameNormalizer.
// Keep in sync with Clean-Name in src/backend/Inventory.ps1 (same rules, same order).
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PanneauxGlass.Platform;

internal static class AppNameNormalizer
{
    const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>
    /// Ordered rewrite rules, applied one after the other (order matters: each rule sees the output of the previous one).
    /// </summary>
    private static readonly (Regex Pattern, string Replacement, string Why)[] Rules =
    {
        (
            new Regex(@"\.exe\s*-\s*Raccourci$", Opts),
            "",
            "Trailing \".exe - Raccourci\" (French shortcut suffix): \"Foo.exe - Raccourci\" -> \"Foo\"."
        ),
        (new Regex(@"\s*-\s*Raccourci$", Opts), "", "Trailing \" - Raccourci\": \"Foo - Raccourci\" -> \"Foo\"."),
        (new Regex(@"^Lancer\s+", Opts), "", "Leading \"Lancer \" (French \"launch\"): \"Lancer Foo\" -> \"Foo\"."),
        (
            new Regex(@"^VLC Media Player\.exe$", Opts),
            "VLC Media Player",
            "Whole name \"VLC Media Player.exe\" -> \"VLC Media Player\"."
        ),
        (
            new Regex("Â?®|™", Opts),
            "",
            "Registered/trademark signs, including the mojibake \"Â®\": \"Foo®\" -> \"Foo\"."
        ),
        (new Regex(@"\s*-\s*Windows10$", Opts), "", "Trailing \" - Windows10\": \"Foo - Windows10\" -> \"Foo\"."),
    };

    /// <summary>
    /// Games whose Steam/launcher shortcuts are named without spaces. Exact whole-name match, case-insensitive,
    /// applied after <see cref="Rules"/> (and before the final trim).
    /// </summary>
    private static readonly Dictionary<string, string> KnownDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CrimsonDesert"] = "Crimson Desert",
        ["HogwartsLegacy"] = "Hogwarts Legacy",
    };

    /// <summary>
    /// Applies the display-name rules in a fixed order, then trims. A null input yields "".
    /// Example: "Lancer Foo.exe - Raccourci" -> "Foo"; "HogwartsLegacy" -> "Hogwarts Legacy".
    /// </summary>
    internal static string Normalize(string raw)
    {
        string n = raw ?? "";
        foreach (var rule in Rules)
            n = rule.Pattern.Replace(n, rule.Replacement);
        if (KnownDisplayNames.TryGetValue(n, out var known))
            n = known;
        return n.Trim();
    }
}
