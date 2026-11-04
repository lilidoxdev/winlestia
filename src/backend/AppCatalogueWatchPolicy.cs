// Pure rules for the application catalogue watch (no Store, no FileSystemWatcher, no window): linked into tests/AppCatalogueWatch.
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace PanneauxGlass;

static class CatalogueWatchRules
{
    internal static readonly TimeSpan Debounce = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan PaletteStale = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan PaletteMinGap = TimeSpan.FromMinutes(1);
    internal const int MaxTargetWaits = 5;
    internal const string PseudoPrefix = "hwnd:";
    const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Shortcut files, or an entry without extension (a folder created, renamed or deleted under the Start menu).
    internal static bool IsShortcutEvent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        string ext = Path.GetExtension(path);
        return ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".url", StringComparison.OrdinalIgnoreCase)
            || ext.Length == 0;
    }

    internal static bool IsLnk(string? path) =>
        !string.IsNullOrEmpty(path) && path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

    // Palette opening: the catalogue is old enough and the last attempt is not too recent.
    internal static bool PaletteNeedsRefresh(DateTime? catalogueWriteUtc, DateTime? lastAttemptUtc, DateTime nowUtc)
    {
        if (lastAttemptUtc is DateTime attempt && nowUtc - attempt < PaletteMinGap)
            return false;
        return catalogueWriteUtc is not DateTime written || nowUtc - written >= PaletteStale;
    }

    internal static bool IsPseudoPath(string? path) =>
        !string.IsNullOrEmpty(path) && path.StartsWith(PseudoPrefix, StringComparison.Ordinal);

    internal static string PseudoPath(long handle) => PseudoPrefix + handle;

    // Windows of one program share its path; a window whose process cannot be read gets its own stable key.
    internal static string WindowGroupKey(string? path, long handle) =>
        string.IsNullOrWhiteSpace(path) ? PseudoPath(handle) : path;

    // Icon extraction that failed (shortcut or target not readable yet) is retried a bounded number of times.
    internal static TimeSpan? IconRetryDelay(int failures) =>
        failures switch
        {
            1 => TimeSpan.FromSeconds(5),
            2 => TimeSpan.FromSeconds(15),
            3 => TimeSpan.FromSeconds(45),
            4 => TimeSpan.FromMinutes(2),
            _ => null,
        };

    internal static bool IconRetryDue(int failures, DateTime failedAtUtc, DateTime nowUtc) =>
        IconRetryDelay(failures) is TimeSpan delay && nowUtc - failedAtUtc >= delay - TimeSpan.FromMilliseconds(500);

    // Same naming and filtering as Inventory.ps1 (Clean-Name, exclusion list, identity of a .lnk).
    // Thin wrapper kept for callers; the rules live in AppNameNormalizer.cs.
    internal static string CleanName(string name) => AppNameNormalizer.Normalize(name);

    static readonly Regex Excluded = new(
        @"uninstall|désinstall|documentation|\bhelp\b|\baide\b|release notes|changelog|readme|licen[cs]e|manuels?|pydoc|module docs|online documentation|Install Additional|update|mises? à jour|disponibilité de mises|Panneaux|^Secours -|Swish Panel|Navigation privée|Windows Kits Feedback|Donate|Support Center|EA Error Reporter",
        Ci
    );

    internal static bool IsExcludedName(string name) => Excluded.IsMatch(name);

    static readonly Regex Squirrel = new(
        @"^(Discord|Wand|Wand \(WeMod\)|Vyra( \(2\))?|Firefox|WinRAR|Ubisoft Connect|Steam|Content Manager)$",
        Ci
    );

    internal static string ShortcutId(string name, string resolved, string arguments)
    {
        if (Squirrel.IsMatch(name))
            return "named:" + Regex.Replace(name, @" \(WeMod\)| \(2\)", "", Ci).ToLowerInvariant();
        return string.IsNullOrEmpty(resolved)
            ? "name:" + name.ToLowerInvariant()
            : "app:" + (resolved + "|" + arguments).ToLowerInvariant();
    }

    // IconLocation without its ",index" suffix, else the resolved target (as Inventory.ps1 does).
    internal static string IconSource(string iconLocation, string resolved, Func<string, bool> exists)
    {
        string icon = Regex.Replace(iconLocation ?? "", @",\s*-?\d+$", "");
        return !string.IsNullOrEmpty(icon) && exists(icon) ? icon : resolved;
    }

    // Library files whose first icon is a generic one: the shortcut itself gives the right image.
    internal static bool PreferTargetForIcon(string? iconPath)
    {
        string ext = Path.GetExtension(iconPath ?? "");
        return ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".cpl", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mun", StringComparison.OrdinalIgnoreCase);
    }
}

// One relecture at a time; a request that arrives meanwhile is replayed once at the end (otherwise the application is lost).
sealed class RefreshGate
{
    bool busy,
        again;
    internal bool Busy => busy;

    internal bool Request()
    {
        if (busy)
        {
            again = true;
            return false;
        }
        busy = true;
        return true;
    }

    internal bool Complete()
    {
        busy = false;
        bool replay = again;
        again = false;
        return replay;
    }
}

// Minimum gap between two inventories started by the watcher; Delay() says how long the last request must wait.
sealed class RefreshThrottle
{
    DateTime? lastRun;

    internal TimeSpan Delay(DateTime nowUtc)
    {
        if (lastRun is not DateTime at)
            return TimeSpan.Zero;
        TimeSpan left = CatalogueWatchRules.PaletteMinGap - (nowUtc - at);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    internal void Ran(DateTime nowUtc) => lastRun = nowUtc;
}

// Trailing debounce: every event pushes the deadline; nothing is kept once the burst has been handled.
sealed class BurstDebounce
{
    DateTime? last;
    internal bool Active => last != null;

    internal void Touch(DateTime nowUtc) => last = nowUtc;

    internal TimeSpan? Remaining(DateTime nowUtc)
    {
        if (last is not DateTime at)
            return null;
        TimeSpan left = CatalogueWatchRules.Debounce - (nowUtc - at);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    internal void Reset() => last = null;
}
