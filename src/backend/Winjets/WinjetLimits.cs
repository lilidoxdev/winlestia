// language: C# 12, file: WinjetLimits.cs, target: Windows 11 / .NET 8, limites normatives du format Winjet v1 (docs/WINJETS-DESIGN.md §3 et §5)
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
namespace PanneauxGlass.Winjets;

internal static class WinjetLimits
{
    internal const long MaxZipBytes = 5L * 1024 * 1024;
    internal const long MaxUnpackedBytes = 20L * 1024 * 1024;
    internal const int MaxEntries = 200;
    internal const int MaxDepth = 4;
    internal const long MaxJsonBytes = 256L * 1024;
    internal const int MaxJsonDepth = 32;
    internal const long MaxPreviewBytes = 1024L * 1024;
    internal const int MaxPreviewWidth = 1600, MaxPreviewHeight = 1000;

    // Dimensions imposées par le moteur (en pixels logiques).
    internal const int MinSide = 120, MaxWidth = 1600, MaxHeight = 1000;

    internal const int MinRefreshSeconds = 60;
    internal const long MaxResponseBytes = 512L * 1024;
    internal static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

    internal static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".json", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".txt", ".md" };

    internal static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp", ".gif" };

    internal static readonly Regex IdPattern = new(@"^[a-z0-9]+(\.[a-z0-9-]+){1,4}$", RegexOptions.Compiled);
    internal static readonly Regex SemVerPattern = new(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.Compiled);
    internal static readonly Regex HostPattern = new(
        @"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$", RegexOptions.Compiled);
    internal static readonly Regex SettingIdPattern = new(@"^[A-Za-z][A-Za-z0-9_]{0,31}$", RegexOptions.Compiled);

    // Rôles de palette autorisés pour toute couleur.
    internal static readonly HashSet<string> PaletteRoles = new(StringComparer.Ordinal)
        { "accent", "primary", "secondary", "tertiary", "onSurface", "onSurfaceVariant", "positive", "negative" };

    internal static readonly HashSet<string> Containers = new(StringComparer.Ordinal) { "column", "row", "stack", "grid" };

    internal static readonly HashSet<string> Blocks = new(StringComparer.Ordinal)
    {
        "text", "image", "clock", "calendar", "list", "chart", "stat", "progress", "dots", "buttons", "spacer", "divider", "visualizer", "media"
    };

    // Noms réservés de Windows (avec ou sans extension).
    // Côté maximal d'une image sous assets/ (garde-fou contre les décodages géants côté Qt).
    internal const int MaxImageSide = 4096;

    internal static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };
}
