// language: C# 12, file: MediaSourceIdentity.cs, target: Windows 11 / .NET 8
// Pure resolution of a Windows media-session AppUserModelID (AUMID) to a display name and an executable stem.
// The raw AUMID stays the stable identifier everywhere; the display name never shows a bare hash.
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PanneauxGlass;

/// <summary>A resolved media source. <see cref="Aumid"/> is the raw, stable identifier.</summary>
internal sealed record MediaSource(string Aumid, string DisplayName, string ExeStem);

internal sealed class MediaSourceIdentity
{
    // Source text is French; the frontend translates it through localization.py.
    internal const string UnknownBrowserName = "Navigateur";
    internal const string UnknownSourceName = "Lecteur Windows";

    sealed record KnownApp(string Needle, string DisplayName, string ExeStem);

    // Order matters: the first needle contained in the AUMID wins.
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

    // Gecko browsers whose AUMID can be an opaque 16-hex-digit hash (Firefox installer scheme).
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

    /// <param name="registryDisplayName">AUMID to registered DisplayName (HKCU or HKLM AppUserModelId key), or null.</param>
    /// <param name="isProcessRunning">Executable stem (no extension) to "a process with this name is running".</param>
    /// <param name="packagedDisplayName">Packaged AUMID to shell display name, or null.</param>
    internal MediaSourceIdentity(
        Func<string, string?> registryDisplayName,
        Func<string, bool> isProcessRunning,
        Func<string, string?> packagedDisplayName)
    {
        this.registryDisplayName = registryDisplayName;
        this.isProcessRunning = isProcessRunning;
        this.packagedDisplayName = packagedDisplayName;
    }

    internal MediaSource Resolve(string? aumid)
    {
        return Resolve(aumid, out _);
    }

    /// <param name="stable">False when the result is a guess that must be re-evaluated soon (short TTL only).</param>
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

            // A registered name that is not a known browser is still better than a hash.
            stable = true;
            return new MediaSource(id, SafeName(registered, UnknownBrowserName), "");
        }

        var running = HashStyleBrowsers.Where(browser => isProcessRunning(browser.ExeStem)).ToArray();
        return ChooseHeuristicBrowser(id, running.Select(browser => browser.ExeStem).ToArray(), out stable);
    }

    /// <summary>
    /// Process-based guess: only a single running candidate browser is trusted (and cacheable). Zero or several
    /// candidates give the generic label, which is better than a wrong name, and must be re-evaluated soon.
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

    /// <summary>True for opaque identifiers such as 308046B0AF4A39CB (16 hexadecimal characters).</summary>
    internal static bool IsHashStyle(string value)
    {
        return value.Length == 16 && value.All(Uri.IsHexDigit);
    }

    // Never let a bare hash reach the UI.
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

    /// <summary>"SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify" becomes "Spotify Music".</summary>
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
