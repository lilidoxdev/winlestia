// language: C# 12, file: MediaSourceIdentity.cs, target: Windows 11 / .NET 8
// Résolution pure d'un AppUserModelID (AUMID) de session média Windows en nom d'affichage et radical d'exécutable.
// L'AUMID brut reste partout l'identifiant stable ; le nom d'affichage n'est jamais un simple hash.
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PanneauxGlass;

/// <summary>Une source média résolue. <see cref="Aumid"/> est l'identifiant brut et stable.</summary>
internal sealed record MediaSource(string Aumid, string DisplayName, string ExeStem);

internal sealed class MediaSourceIdentity
{
    // Le texte source est en français ; le frontend le traduit via localization.py.
    internal const string UnknownBrowserName = "Navigateur";
    internal const string UnknownSourceName = "Lecteur Windows";

    sealed record KnownApp(string Needle, string DisplayName, string ExeStem);

    // L'ordre compte : la première sous-chaîne contenue dans l'AUMID l'emporte.
    static readonly KnownApp[] KnownApps =
    {
        new("spotify", "Spotify", "spotify"),
        new("msedge", "Microsoft Edge", "msedge"),
        new("microsoftedge", "Microsoft Edge", "msedge"),
        new("brave", "Brave", "brave"),
        new("vivaldi", "Vivaldi", "vivaldi"),
        new("opera", "Opera", "opera"),
        new("chrome", "Google Chrome", "chrome"),
        new("librewolf", "LibreWolf", "librewolf"),
        new("floorp", "Floorp", "floorp"),
        new("firefox", "Firefox", "firefox"),
    };

    // Navigateurs Gecko dont l'AUMID peut être un hash opaque de 16 chiffres hexadécimaux (schéma de l'installeur Firefox).
    static readonly KnownApp[] HashStyleBrowsers =
    {
        new("firefox", "Firefox", "firefox"),
        new("librewolf", "LibreWolf", "librewolf"),
        new("floorp", "Floorp", "floorp"),
        new("zen", "Zen Browser", "zen"),
    };

    readonly Func<string, string?> registryDisplayName;
    readonly Func<string, bool> isProcessRunning;
    readonly Func<string, string?> packagedDisplayName;

    /// <param name="registryDisplayName">AUMID vers DisplayName enregistré (clé AppUserModelId de HKCU ou HKLM), ou null.</param>
    /// <param name="isProcessRunning">Radical d'exécutable (sans extension) vers « un processus de ce nom tourne ».</param>
    /// <param name="packagedDisplayName">AUMID empaqueté vers nom d'affichage du shell, ou null.</param>
    internal MediaSourceIdentity(
        Func<string, string?> registryDisplayName,
        Func<string, bool> isProcessRunning,
        Func<string, string?> packagedDisplayName
    )
    {
        this.registryDisplayName = registryDisplayName;
        this.isProcessRunning = isProcessRunning;
        this.packagedDisplayName = packagedDisplayName;
    }

    internal MediaSource Resolve(string? aumid)
    {
        return Resolve(aumid, out _);
    }

    /// <param name="stable">False quand le résultat est une supposition à réévaluer bientôt (TTL court uniquement).</param>
    internal MediaSource Resolve(string? aumid, out bool stable)
    {
        stable = true;
        string id = aumid ?? "";
        if (string.IsNullOrWhiteSpace(id))
        {
            return new MediaSource("", UnknownSourceName, "");
        }

        if (IsHashStyle(id))
        {
            return ResolveHashStyle(id, out stable);
        }

        foreach (var known in KnownApps)
        {
            if (id.Contains(known.Needle, StringComparison.OrdinalIgnoreCase))
            {
                return new MediaSource(id, known.DisplayName, known.ExeStem);
            }
        }

        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            string stem = Path.GetFileNameWithoutExtension(id);
            return new MediaSource(id, SafeName(Capitalize(stem), UnknownSourceName), stem.ToLowerInvariant());
        }

        if (id.Contains('!') || id.Contains('_'))
        {
            string? shellName = packagedDisplayName(id);
            string name = !string.IsNullOrWhiteSpace(shellName) ? shellName.Trim() : CleanPackageName(id);
            return new MediaSource(id, SafeName(name, UnknownSourceName), "");
        }

        return new MediaSource(id, SafeName(Capitalize(id), UnknownSourceName), "");
    }

    MediaSource ResolveHashStyle(string id, out bool stable)
    {
        string? registered = registryDisplayName(id);
        if (!string.IsNullOrWhiteSpace(registered))
        {
            foreach (var browser in HashStyleBrowsers)
            {
                if (registered.Contains(browser.Needle, StringComparison.OrdinalIgnoreCase))
                {
                    stable = true;
                    return new MediaSource(id, browser.DisplayName, browser.ExeStem);
                }
            }

            // Un nom enregistré qui n'est pas un navigateur connu vaut quand même mieux qu'un hash.
            stable = true;
            return new MediaSource(id, SafeName(registered, UnknownBrowserName), "");
        }

        var running = HashStyleBrowsers.Where(browser => isProcessRunning(browser.ExeStem)).ToArray();
        return ChooseHeuristicBrowser(id, running.Select(browser => browser.ExeStem).ToArray(), out stable);
    }

    /// <summary>
    /// Supposition fondée sur les processus : seul un unique navigateur candidat en cours d'exécution est cru (et mis en cache). Zéro ou
    /// plusieurs candidats donnent le libellé générique, préférable à un mauvais nom, et doivent être réévalués bientôt.
    /// </summary>
    internal static MediaSource ChooseHeuristicBrowser(string id, string[] runningStems, out bool stable)
    {
        if (runningStems.Length == 1)
        {
            var browser = HashStyleBrowsers.First(candidate => candidate.ExeStem == runningStems[0]);
            stable = true;
            return new MediaSource(id, browser.DisplayName, browser.ExeStem);
        }

        stable = false;
        return new MediaSource(id, UnknownBrowserName, "");
    }

    /// <summary>Vrai pour les identifiants opaques comme 308046B0AF4A39CB (16 caractères hexadécimaux).</summary>
    internal static bool IsHashStyle(string value)
    {
        return value.Length == 16 && value.All(Uri.IsHexDigit);
    }

    // Ne jamais laisser un simple hash atteindre l'UI.
    static string SafeName(string name, string fallback)
    {
        string trimmed = name.Trim();
        bool looksLikeHash = trimmed.Length >= 12 && trimmed.All(Uri.IsHexDigit);
        return trimmed.Length == 0 || looksLikeHash ? fallback : trimmed;
    }

    static string Capitalize(string value)
    {
        return value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];
    }

    /// <summary>"SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" devient "Spotify Music".</summary>
    internal static string CleanPackageName(string aumid)
    {
        string family = aumid.Split('!')[0];
        int underscore = family.IndexOf('_');
        if (underscore > 0)
        {
            family = family[..underscore];
        }

        string lastSegment = family.Split('.').LastOrDefault(segment => segment.Length > 0) ?? "";
        var words = new StringBuilder();
        for (int index = 0; index < lastSegment.Length; index++)
        {
            char current = lastSegment[index];
            bool startsWord = index > 0 && char.IsUpper(current) && char.IsLower(lastSegment[index - 1]);
            if (startsWord)
            {
                words.Append(' ');
            }

            words.Append(current);
        }

        return words.ToString();
    }
}
