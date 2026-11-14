// C# 12 / .NET 8. Règles pures du service Social : lecture du compteur de non-lus dans le titre d'un onglet et découpe des textes.
// Séparées de SocialService pour être testables sans WebView2 ni dispatcher.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PanneauxGlass;

internal static class SocialRules
{
    internal static readonly Regex TitleCount = new(@"^\s*\(([\d\s\u00a0\u202f,.]*\d\+?)\)", RegexOptions.Compiled);
    internal static readonly Regex TitleCountGmail = new(@"\(([\d\s\u00a0\u202f,.]*\d\+?)\)\s*$", RegexOptions.Compiled);

    // null = titre non informatif (Gmail : mail ouvert, autre vue) : on ne touche à rien.
    static readonly HashSet<string> GmailLists = new(StringComparer.OrdinalIgnoreCase) { "", "inbox", "starred", "snoozed", "sent",
        "drafts", "imp", "chats", "scheduled", "all", "spam", "trash", "category", "label", "search" };
    static readonly Regex GmailMessageId = new("^[A-Za-z0-9]{16,}$", RegexOptions.Compiled);

    // Vue de liste Gmail (#inbox, #category/..., #label/x, #search/...) sans identifiant de message en dernier segment.
    internal static bool GmailListView(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var u))
            return false;
        string[] parts = u.Fragment.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return true;
        if (!GmailLists.Contains(parts[0]))
            return false;
        return !(parts.Length >= 2 && GmailMessageId.IsMatch(parts[^1]));
    }

    internal static int? ParseUnread(string id, string title, string? source = null)
    {
        if (id == "gmail")
        {
            if (!GmailListView(source) || !title.TrimEnd().EndsWith("Gmail", StringComparison.Ordinal))
                return null;
            var g = TitleCountGmail.Match(title.Split(" - ")[0]);
            if (!g.Success)
                return 0;
            string gd = new string(g.Groups[1].Value.Where(char.IsDigit).ToArray());
            return int.TryParse(gd, out int gv) ? Math.Min(gv, 99999) : null;
        }
        var m = TitleCount.Match(title);
        if (m.Success)
        {
            string digits = new string(m.Groups[1].Value.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out int v))
                return Math.Min(v, 99999);
            return 1;
        }
        string t = title.TrimStart();
        return t.Length > 0 && "•*●".IndexOf(t[0]) >= 0 ? 1 : 0;
    }

    internal static string Clip(string? text, int max)
    {
        text = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length > max ? text[..max] : text;
    }

    // Historique relu du disque : on écarte les réseaux disparus et les entrées nulles, on borne les textes (un fichier édité à la main
    // ne doit pas gonfler l'interface) et on garde les 100 plus récentes.
    internal static List<SocialService.HistoryItem> RestoreHistory(
        IEnumerable<SocialService.HistoryItem?> saved, Func<string, bool> knownNet)
        => saved.Where(h => h != null && knownNet(h.Net)).Select(h => new SocialService.HistoryItem
        {
            Seq = h!.Seq,
            Net = h.Net,
            Title = Clip(h.Title, 120),
            Body = Clip(h.Body, 140),
            Kind = h.Kind == "count" ? "count" : "notif",
            At = h.At,
            N = h.Kind == "count" ? RestoreN(h.N, h.Title) : 0
        }).OrderByDescending(h => h.Seq).Take(100).ToList();

    // Ancienne ligne "count" sans N (0) : on relit l'entier en tête du titre ("5 nouveaux messages"), à défaut 1.
    static int RestoreN(int saved, string? title)
    {
        if (saved > 0)
            return Math.Min(saved, MaxMerged);
        int digits = 0;
        string t = (title ?? "").TrimStart();
        while (digits < t.Length && digits < 6 && t[digits] >= '0' && t[digits] <= '9')
            digits++;
        return digits > 0 ? Math.Clamp(int.Parse(t[..digits]), 1, MaxMerged) : 1;
    }

    // Lignes génériques consécutives : une hausse du titre fusionne dans la dernière ligne "count" intacte du même réseau si elle
    // date de moins de CountMergeMs et du même jour local ; la ligne remonte en tête avec un nouveau Seq.
    internal const long CountMergeMs = 30L * 60 * 1000;
    internal const int MaxMerged = 99999;

    internal static string CountTitle(int n) => n == 1 ? "1 nouveau message" : n + " nouveaux messages";

    internal static SocialService.HistoryItem? MergeCount(
        List<SocialService.HistoryItem> history, string net, int added, long nowMs, Func<int> newSeq)
    {
        var last = history.FirstOrDefault(h => h.Net == net);
        if (added <= 0 || last == null || last.Kind != "count")
            return null;
        long age = nowMs - last.At;
        if (age < 0 || age >= CountMergeMs || LocalDay(last.At) != LocalDay(nowMs))
            return null;
        last.N = Math.Min(MaxMerged, Math.Max(1, last.N) + added);
        last.Title = CountTitle(last.N);
        last.At = nowMs;
        last.Seq = newSeq();
        history.Remove(last);
        history.Insert(0, last);
        return last;
    }

    static DateTime LocalDay(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().Date;

    // Une vraie notification reçue dans les 3 s consomme un message de la ligne générique. true = la ligne n'en couvrait qu'un
    // (elle devient la notification) ; false = le message est retiré du regroupement et la notification a sa propre ligne.
    internal static bool ReleaseOne(SocialService.HistoryItem entry)
    {
        if (entry.N <= 1)
            return true;
        entry.N--;
        entry.Title = CountTitle(entry.N);
        return false;
    }

    internal static bool RemoveBySeq(List<SocialService.HistoryItem> history, int seq, out SocialService.HistoryItem? removed)
    {
        removed = history.FirstOrDefault(h => h.Seq == seq);
        return removed != null && history.Remove(removed);
    }

    // Le prochain numéro doit dépasser tout Seq déjà présent, même si le champ NextSeq du fichier est absent ou en retard.
    internal static int NextSeqAfter(int savedNext, IEnumerable<int> seqs)
        => Math.Max(Math.Max(1, savedNext), seqs.DefaultIfEmpty(0).Max() + 1);

    // Compteur relu : borné, et plafonné à 99 quand il n'est pas issu d'un titre fiable (ancien fichier ou réseau à titre).
    internal static int RestoreCount(int saved, bool capped)
    {
        int c = Math.Clamp(saved, 0, 999999);
        return capped ? Math.Min(c, 99) : c;
    }
}
