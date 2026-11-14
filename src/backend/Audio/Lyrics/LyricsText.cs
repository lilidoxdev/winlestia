// C# 12, fichier LyricsText.cs, cible Windows 11 / .NET 8, pièces pures des paroles : normalisation, analyse LRC, choix du candidat
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PanneauxGlass;

internal sealed record LyricLine(double Time, string Text);

// État "synced" | "plain" | "instrumental" | "none" (+ "loading" / "off" côté pont).
internal sealed record LyricsResult(string State, IReadOnlyList<LyricLine> Lines, string Plain)
{
    internal static readonly LyricsResult None = new("none", Array.Empty<LyricLine>(), "");
    internal static readonly LyricsResult Loading = new("loading", Array.Empty<LyricLine>(), "");
    internal static readonly LyricsResult Off = new("off", Array.Empty<LyricLine>(), "");
    internal static readonly LyricsResult Instrumental = new("instrumental", Array.Empty<LyricLine>(), "");
}

// Un résultat de recherche LRCLIB, réduit à ce que le choix utilise.
internal sealed record LyricCandidate(string? Synced, string? Plain, bool Instrumental, double Duration);

internal static class LyricsText
{
    const int RegexTimeoutMs = 200;
    static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(RegexTimeoutMs);

    // "(feat. X)", "[ft. X]", "(with X)" : l'invité ne change pas le morceau.
    static readonly Regex Featuring = new(@"\s*[\(\[]\s*(?:feat\.?|ft\.?|featuring|with)\s[^\)\]]*[\)\]]",
                                          RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    static readonly Regex FeaturingTail = new(@"\s+(?:feat\.?|ft\.?|featuring)\s.*$",
                                              RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    // Bruit d'édition qui ne change pas les paroles : remasterisation, mono/stéréo, version single.
    const string Noise = @"(?:\d{4}\s+)?(?:remaster(?:ed)?(?:\s+\d{4})?|digital remaster|mono|stereo|single version|album version|radio edit|deluxe(?:\s+edition)?)";
    static readonly Regex NoiseSuffix = new(@"\s+[-–—]\s+[^-–—]*?\b" + Noise + @"\b[^-–—]*$",
                                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    static readonly Regex NoiseBracket = new(@"\s*[\(\[][^\)\]]*?\b" + Noise + @"\b[^\)\]]*[\)\]]",
                                             RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    // Variantes ("- Radio Remix", "(Live)", "- Acoustic Version") : retirées seulement pour la recherche large.
    static readonly Regex VariantSuffix = new(@"\s+[-–—]\s+[^-–—]*\b(?:remix|edit|version|live|acoustic|instrumental)\b[^-–—]*$",
                                              RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    static readonly Regex VariantBracket = new(@"\s*[\(\[][^\)\]]*\b(?:remix|edit|version|live|acoustic)\b[^\)\]]*[\)\]]",
                                               RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant, Timeout);

    static string Safe(Func<string> work, string fallback)
    {
        try
        {
            return work();
        }
        catch (RegexMatchTimeoutException)
        {
            return fallback;
        }
    }

    // Titre : retire invités et bruit d'édition ; `variants` retire aussi remix / live / acoustique (recherche large).
    internal static string CleanTitle(string? title, bool variants = false)
    {
        string text = (title ?? "").Trim();
        return Safe(() =>
        {
            string s = Featuring.Replace(text, "");
            s = NoiseBracket.Replace(s, "");
            s = NoiseSuffix.Replace(s, "");
            if (variants)
            {
                s = VariantBracket.Replace(s, "");
                s = VariantSuffix.Replace(s, "");
            }
            s = FeaturingTail.Replace(s, "");
            s = Spaces.Replace(s, " ").Trim();
            return s.Length == 0 ? text : s;
        }, text);
    }

    // Artiste : premier artiste listé, sans invité ("A, B", "A & B", "A feat. B" -> "A").
    internal static string CleanArtist(string? artist)
    {
        string text = (artist ?? "").Trim();
        return Safe(() =>
        {
            string s = Featuring.Replace(text, "");
            s = FeaturingTail.Replace(s, "");
            int cut = s.IndexOfAny(new[] { ',', ';', '&', '/' });
            if (cut > 0)
                s = s[..cut];
            s = Spaces.Replace(s, " ").Trim();
            return s.Length == 0 ? text : s;
        }, text);
    }

    // Forme de comparaison et de clé de cache : minuscules, sans bruit, espaces simples.
    internal static string Normalize(string? value, bool artist)
    {
        string s = artist ? CleanArtist(value) : CleanTitle(value);
        return Safe(() => Spaces.Replace(s.ToLowerInvariant(), " ").Trim(), s.ToLowerInvariant());
    }

    internal static string TrackKey(string? artist, string? title, double duration)
        => Normalize(artist, true) + "|" + Normalize(title, false) + "|"
           + (duration > 0 && double.IsFinite(duration) ? Math.Round(duration).ToString(CultureInfo.InvariantCulture) : "0");

    // Candidat le plus proche en durée (tolérance en secondes), avec paroles synchronisées de préférence.
    // Durée inconnue (<= 0) : aucune exclusion, on garde le premier avec paroles synchronisées.
    internal static LyricCandidate? Select(IEnumerable<LyricCandidate> candidates, double duration, double tolerance = 4)
    {
        bool known = duration > 0 && double.IsFinite(duration);
        var usable = candidates.Where(c => !string.IsNullOrWhiteSpace(c.Synced) || !string.IsNullOrWhiteSpace(c.Plain) || c.Instrumental)
                               .Where(c => !known || (c.Duration > 0 && Math.Abs(c.Duration - duration) <= tolerance))
                               .ToList();
        if (usable.Count == 0)
            return null;
        return usable.OrderByDescending(c => !string.IsNullOrWhiteSpace(c.Synced))
                     .ThenBy(c => known ? Math.Abs(c.Duration - duration) : 0)
                     .First();
    }

    static readonly Regex TimeTag = new(@"\[(\d+):(\d+(?:[.:]\d+)?)\]", RegexOptions.CultureInvariant, Timeout);
    static readonly Regex OffsetTag = new(@"^\s*\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                                          Timeout);
    static readonly Regex WordTag = new(@"<\d+:\d+(?:[.:]\d+)?>", RegexOptions.CultureInvariant, Timeout);
    static readonly string[] CreditKeywords =
    {
        "作词", "作曲", "编曲", "制作", "收录", "演奏", "词：", "曲：",
        "Lyricist", "Composer", "Arranger", "Producer", "Mixing", "Mastering"
    };

    // Format LRC : [mm:ss.xx] (plusieurs par ligne), [offset:ms], balises de métadonnées ignorées, lignes vides conservées
    // (silences). Les crédits en tête de morceau sont écartés comme dans le service de référence.
    internal static List<LyricLine> ParseLrc(string? text)
    {
        var result = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(text))
            return result;
        try
        {
            double offset = 0;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                var offsetMatch = OffsetTag.Match(line);
                if (offsetMatch.Success && double.TryParse(offsetMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                                                           out double ms))
                {
                    offset = ms / 1000.0;
                    continue;
                }
                var matches = TimeTag.Matches(line);
                if (matches.Count == 0)
                    continue;
                string lyric = WordTag.Replace(TimeTag.Replace(line, ""), "").Trim();
                double first = Seconds(matches[0]);
                if (first < 20 && lyric.Length > 0 && CreditKeywords.Any(k => lyric.Contains(k, StringComparison.OrdinalIgnoreCase))
                    && (lyric.Contains(':') || lyric.Contains('：') || lyric.Length < 25))
                    continue;
                foreach (Match m in matches)
                    result.Add(new LyricLine(Math.Max(0, Seconds(m) - offset), lyric));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return new List<LyricLine>();
        }
        // Tri stable : deux lignes au même instant gardent leur ordre d'écriture.
        return result.Select((l, i) => (l, i)).OrderBy(x => x.l.Time).ThenBy(x => x.i).Select(x => x.l).ToList();
    }

    static double Seconds(Match m)
        => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 60.0
           + double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);

    // Construit le résultat final depuis un candidat : synchronisé si l'analyse donne des lignes, sinon texte brut.
    internal static LyricsResult FromCandidate(LyricCandidate c)
    {
        if (!string.IsNullOrWhiteSpace(c.Synced))
        {
            var lines = ParseLrc(c.Synced);
            if (lines.Any(l => l.Text.Length > 0))
                return new LyricsResult("synced", lines, "");
        }
        if (!string.IsNullOrWhiteSpace(c.Plain))
            return new LyricsResult("plain", Array.Empty<LyricLine>(), c.Plain!.Replace("\r\n", "\n").Trim());
        return c.Instrumental ? LyricsResult.Instrumental : LyricsResult.None;
    }
}
