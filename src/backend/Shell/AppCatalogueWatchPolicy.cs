// Règles pures de la surveillance du catalogue d'applications (ni Store, ni FileSystemWatcher, ni fenêtre) : liées dans
// tests/AppCatalogueWatch.
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace PanneauxGlass;

static class CatalogueWatchRules
{
    // Tolérance sur l'échéance : la gigue du minuteur ne doit pas faire manquer un essai.
    const int IconRetryToleranceMs = 500;
    internal static readonly TimeSpan Debounce = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan PaletteStale = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan PaletteMinGap = TimeSpan.FromMinutes(1);
    internal const int MaxTargetWaits = 5;
    internal const string PseudoPrefix = "hwnd:";
    const RegexOptions Ci = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Fichiers de raccourci, ou entrée sans extension (dossier créé, renommé ou supprimé sous le menu Démarrer).
    internal static bool IsShortcutEvent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        string ext = Path.GetExtension(path);
        return ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".url", StringComparison.OrdinalIgnoreCase)
            || ext.Length == 0;
    }
    internal static bool IsLnk(string? path) => !string.IsNullOrEmpty(path) && path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);

    // Ouverture de la palette : le catalogue est assez ancien et la dernière tentative n'est pas trop récente.
    internal static bool PaletteNeedsRefresh(DateTime? catalogueWriteUtc, DateTime? lastAttemptUtc, DateTime nowUtc)
    {
        if (lastAttemptUtc is DateTime attempt && nowUtc - attempt < PaletteMinGap)
            return false;
        return catalogueWriteUtc is not DateTime written || nowUtc - written >= PaletteStale;
    }

    internal static bool IsPseudoPath(string? path) => !string.IsNullOrEmpty(path) && path.StartsWith(PseudoPrefix,
        StringComparison.Ordinal);
    internal static string PseudoPath(long handle) => PseudoPrefix + handle;
    // Les fenêtres d'un même programme partagent son chemin ; une fenêtre dont le processus est illisible reçoit sa propre clé
    // stable.
    internal static string WindowGroupKey(string? path, long handle) => string.IsNullOrWhiteSpace(path) ? PseudoPath(handle) : path;

    // Une extraction d'icône qui a échoué (raccourci ou cible pas encore lisible) est retentée un nombre borné de fois.
    internal static TimeSpan? IconRetryDelay(int failures) => failures switch
    {
        1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(15),
        3 => TimeSpan.FromSeconds(45),
        4 => TimeSpan.FromMinutes(2),
        _ => null
    };
    internal static bool IconRetryDue(int failures, DateTime failedAtUtc, DateTime nowUtc) =>
        IconRetryDelay(failures) is TimeSpan delay && nowUtc - failedAtUtc >= delay - TimeSpan.FromMilliseconds(IconRetryToleranceMs);

    // Mêmes noms et même filtrage qu'Inventory.ps1 (Clean-Name, liste d'exclusion, identité d'un .lnk).
    internal static string CleanName(string name)
    {
        string n = Regex.Replace(name ?? "", @"\.exe\s*-\s*Raccourci$", "", Ci);
        n = Regex.Replace(n, @"\s*-\s*Raccourci$", "", Ci);
        n = Regex.Replace(n, @"^Lancer\s+", "", Ci);
        n = Regex.Replace(n, @"^VLC Media Player\.exe$", "VLC Media Player", Ci);
        n = Regex.Replace(n, "Â?®|™", "", Ci);
        n = Regex.Replace(n, @"\s*-\s*Windows10$", "", Ci);
        n = Regex.Replace(n, "^CrimsonDesert$", "Crimson Desert", Ci);
        n = Regex.Replace(n, "^HogwartsLegacy$", "Hogwarts Legacy", Ci);
        return n.Trim();
    }
    static readonly Regex Excluded = new(
        @"uninstall|désinstall|documentation|\bhelp\b|\baide\b|release notes|changelog|readme|licen[cs]e|" +
        @"manuels?|pydoc|module docs|online documentation|Install Additional|update|mises? à jour|" +
        @"disponibilité de mises|Panneaux|^Secours -|Swish Panel|Navigation privée|Windows Kits Feedback|Donate|" +
        @"Support Center|EA Error Reporter", Ci);
    internal static bool IsExcludedName(string name) => Excluded.IsMatch(name);
    static readonly Regex Squirrel = new(
        @"^(Discord|Wand|Wand \(WeMod\)|Vyra( \(2\))?|Firefox|WinRAR|Ubisoft Connect|Steam|Content Manager)$", Ci);
    internal static string ShortcutId(string name, string resolved, string arguments)
    {
        if (Squirrel.IsMatch(name))
            return "named:" + Regex.Replace(name, @" \(WeMod\)| \(2\)", "", Ci).ToLowerInvariant();
        return string.IsNullOrEmpty(resolved) ? "name:" + name.ToLowerInvariant() : "app:" + (resolved + "|"
            + arguments).ToLowerInvariant();
    }
    // IconLocation sans son suffixe « ,index », sinon la cible résolue (comme le fait Inventory.ps1).
    internal static string IconSource(string iconLocation, string resolved, Func<string, bool> exists)
    {
        string icon = Regex.Replace(iconLocation ?? "", @",\s*-?\d+$", "");
        return !string.IsNullOrEmpty(icon) && exists(icon) ? icon : resolved;
    }
    // Fichiers de bibliothèque dont la première icône est générique : le raccourci lui-même donne la bonne image.
    internal static bool PreferTargetForIcon(string? iconPath)
    {
        string ext = Path.GetExtension(iconPath ?? "");
        return ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) || ext.Equals(".cpl", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mun", StringComparison.OrdinalIgnoreCase);
    }
}

// Une relecture à la fois ; une demande arrivée entre-temps est rejouée une fois à la fin (sinon l'application serait perdue).
sealed class RefreshGate
{
    bool busy, again;
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

// Écart minimal entre deux inventaires lancés par le watcher ; Delay() dit combien de temps la dernière demande doit attendre.
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

// Anti-rebond de queue : chaque événement repousse l'échéance ; rien n'est conservé une fois la rafale traitée.
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
