// language: C# 12, file: WinjetSemVer.cs, target: Windows 11 / .NET 8, version x.y.z et plage de compatibilité (">=0.9.2 <2.0.0")
using System;
using System.Linq;
namespace PanneauxGlass.Winjets;

internal static class WinjetSemVer
{
    // « 0.9.2 », « 0.9.2-beta » ou « 0.9.2+build » : seule la partie x.y.z compte pour la compatibilité.
    internal static bool TryParseCore(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string core = text.Trim().TrimStart('v').Split('+')[0].Split('-')[0];
        var parts = core.Split('.');
        if (parts.Length != 3 || !parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)) || parts.Any(p => p.Length > 9))
            return false;
        version = new Version(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
        return true;
    }

    // Plage = comparateurs séparés par des espaces, tous requis. Opérateurs : >= > <= < =. Faux si la plage est mal formée.
    internal static bool TryMatches(string? range, Version current, out bool matches)
    {
        matches = false;
        if (string.IsNullOrWhiteSpace(range))
            return false;
        var terms = range.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool all = true;
        foreach (string term in terms)
        {
            string op = term.StartsWith(">=", StringComparison.Ordinal) || term.StartsWith("<=", StringComparison.Ordinal) ? term[..2]
                : term[0] is '>' or '<' or '=' ? term[..1] : "";
            string rest = term[op.Length..];
            if (!TryParseCore(rest, out var bound) || rest.Contains('-') || rest.Contains('+'))
                return false;
            int cmp = current.CompareTo(bound);
            all &= op switch
            {
                ">=" => cmp >= 0,
                ">" => cmp > 0,
                "<=" => cmp <= 0,
                "<" => cmp < 0,
                _ => cmp == 0
            };
        }
        matches = all;
        return true;
    }
}
