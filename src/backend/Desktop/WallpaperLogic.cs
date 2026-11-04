// C# 12 / WallpaperLogic.cs / Pure helpers for the wallpaper picker: animated-wallpaper process names and display labels. No I/O, no Windows API.
using System;
using System.Collections.Generic;
using System.Linq;

namespace PanneauxGlass.Desktop;

internal static class WallpaperLogic
{
    // Executable name (lower case, with .exe) -> product name. Process names only; no window or content is read. Add a line to cover another product.
    internal static readonly (string exe, string label)[] AnimatedHosts =
    {
        ("desktophut studio.exe", "DesktopHut"),
        ("wallpaper32.exe", "Wallpaper Engine"),
        ("wallpaper64.exe", "Wallpaper Engine"),
        ("lively.exe", "Lively Wallpaper"),
        ("livelywpf.exe", "Lively Wallpaper"),
        ("wallpaper.service.exe", "Wallpaper X"),
        ("wallpaper x launcher.exe", "Wallpaper X"),
    };

    // Process names may come with or without ".exe" and in any case (Process.ProcessName has no extension).
    internal static string[] Animated(IEnumerable<string?> processNames)
    {
        var seen = new HashSet<string>(
            processNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => Normalize(n!)),
            StringComparer.Ordinal
        );
        return AnimatedHosts.Where(h => seen.Contains(h.exe)).Select(h => h.label).Distinct().ToArray();
    }

    static string Normalize(string name)
    {
        name = name.Trim().ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name : name + ".exe";
    }

    // Neutral label for the CLI; the picker builds its own translated label from the same fields.
    internal static string DisplayName(int id, int width, int height, bool primary) =>
        "Display " + id + " (" + width + "x" + height + ")" + (primary ? " primary" : "");
}
