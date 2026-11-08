// Routage pur de l'argument de ligne de commande : ouvrir dans Winlestia Fichiers (Own) ou passer la main à l'Explorateur.
using System;
using System.Diagnostics;
using System.IO;

namespace WinlestiaFiles;

internal sealed record RouteDecision(bool IsOwn, string Value)
{
    internal static RouteDecision Own(string normalizedFullPath) => new(true, normalizedFullPath);
    internal static RouteDecision Explorer(string originalArg) => new(false, originalArg);
}

internal static class PathRouter
{
    // La commande enregistrée est "<exe>" "%1" : pour une racine, %1 = C:\ finit par un antislash qui échappe le guillemet fermant, donc
    // l'argument arrive sous la forme  C:"  (ou  \\srv\share" ).
    // Retire UN guillemet fermant orphelin (sans guillemet ouvrant) et rétablit l'antislash de la racine. Tout le reste est renvoyé tel
    // quel.
    internal static string Clean(string? arg)
    {
        string s = arg ?? "";
        string t = s.Trim();
        bool dangling = t.Length > 0 && t[^1] == '"' && t[0] != '"';
        if (dangling)
            t = t[..^1];
        bool drive = t.Length == 2 && t[1] == ':' && char.IsAsciiLetter(t[0]);
        bool unc = dangling && t.StartsWith(@"\\", StringComparison.Ordinal) && !t.EndsWith('\\') && t.Substring(2).Split('\\').Length == 2;
        if (drive && (dangling || t.Length == s.Trim().Length))
            return t + "\\";
        if (unc)
            return t + "\\";
        return dangling ? t : s;
    }

    internal static RouteDecision Route(string? arg)
    {
        string original = Clean(arg);
        string value = original.Trim().Trim('"').Trim();
        if (value.Length == 0)
            return RouteDecision.Own(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("::{", StringComparison.Ordinal)
            || value.StartsWith(@"\\?\", StringComparison.Ordinal) || value.StartsWith(@"\\", StringComparison.Ordinal)
            || value.StartsWith("//", StringComparison.Ordinal))
            return RouteDecision.Explorer(original);
        if (value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return RouteDecision.Explorer(original);
        try
        {
            if (value.Length == 2 && value[1] == ':' && char.IsAsciiLetter(value[0]))
                value += "\\";
            string full = Path.GetFullPath(value);
            if (File.Exists(full) || !Directory.Exists(full))
                return RouteDecision.Explorer(original);
            string root = Path.GetPathRoot(full) ?? "";
            if (full.Length > root.Length)
                full = full.TrimEnd('\\', '/');
            return RouteDecision.Own(full);
        }
        catch
        {
            return RouteDecision.Explorer(original);
        }
    }

    // Chemin explicite d'explorer.exe, jamais le verbe Directory : pas de boucle une fois que nous sommes le gestionnaire par défaut.
    internal static void LaunchExplorer(string arg)
    {
        arg = Clean(arg);
        var info = new ProcessStartInfo(Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", "explorer.exe")) {
            UseShellExecute = false };
        if (!string.IsNullOrWhiteSpace(arg))
            info.ArgumentList.Add(arg);
        Process.Start(info)?.Dispose();
    }
}
