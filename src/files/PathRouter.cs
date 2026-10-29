// Pure routing of the command-line argument: open in Winlestia Fichiers (Own) or hand over to Explorer.
using System;
using System.Diagnostics;
using System.IO;

namespace WinlestiaFiles;

internal sealed record RouteDecision(bool IsOwn, string Value) {
    internal static RouteDecision Own(string normalizedFullPath) => new(true, normalizedFullPath);
    internal static RouteDecision Explorer(string originalArg) => new(false, originalArg);
}

internal static class PathRouter {
    // The registered command is "<exe>" "%1": for a root, %1 = C:\ ends with a backslash that escapes the closing quote, so the argument arrives as  C:"  (or  \\srv\share" ).
    // Strips ONE dangling closing quote (no opening one) and restores the root backslash. Anything else is returned untouched.
    internal static string Clean(string? arg) {
        string s = arg ?? "";
        string t = s.Trim();
        bool dangling = t.Length > 0 && t[^1] == '"' && t[0] != '"';
        if (dangling) t = t[..^1];
        bool drive = t.Length == 2 && t[1] == ':' && char.IsAsciiLetter(t[0]);
        bool unc = dangling && t.StartsWith(@"\\", StringComparison.Ordinal) && !t.EndsWith('\\') && t.Substring(2).Split('\\').Length == 2;
        if (drive && (dangling || t.Length == s.Trim().Length)) return t + "\\";
        if (unc) return t + "\\";
        return dangling ? t : s;
    }

    internal static RouteDecision Route(string? arg) {
        string original = Clean(arg);
        string value = original.Trim().Trim('"').Trim();
        if (value.Length == 0) return RouteDecision.Own(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("::{", StringComparison.Ordinal)
            || value.StartsWith(@"\\?\", StringComparison.Ordinal) || value.StartsWith(@"\\", StringComparison.Ordinal)
            || value.StartsWith("//", StringComparison.Ordinal))
            return RouteDecision.Explorer(original);
        if (value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return RouteDecision.Explorer(original);
        try {
            if (value.Length == 2 && value[1] == ':' && char.IsAsciiLetter(value[0])) value += "\\";
            string full = Path.GetFullPath(value);
            if (File.Exists(full) || !Directory.Exists(full)) return RouteDecision.Explorer(original);
            string root = Path.GetPathRoot(full) ?? "";
            if (full.Length > root.Length) full = full.TrimEnd('\\', '/');
            return RouteDecision.Own(full);
        } catch {
            return RouteDecision.Explorer(original);
        }
    }

    // Explicit explorer.exe path, never the Directory verb: no loop once we are the default handler.
    internal static void LaunchExplorer(string arg) {
        arg = Clean(arg);
        var info = new ProcessStartInfo(Path.Combine(Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows", "explorer.exe")) { UseShellExecute = false };
        if (!string.IsNullOrWhiteSpace(arg)) info.ArgumentList.Add(arg);
        Process.Start(info)?.Dispose();
    }
}
