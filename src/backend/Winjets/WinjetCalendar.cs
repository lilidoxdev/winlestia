// language: C# 12, file: WinjetCalendar.cs, target: Windows 11 / .NET 8, source de données « calendar » : lecture d'un agenda ICS
// (fonctions pures, aucun accès réseau)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
namespace PanneauxGlass.Winjets;

internal sealed record CalendarEvent(string Title, string Location, DateTime Start, DateTime End, bool AllDay);

internal static class WinjetCalendar
{
    internal const int MaxEvents = 12, WindowDays = 7, MaxVevents = 5000, MaxOccurrences = 5000;

    // Adresse ICS : https:// ou webcal:// (réécrit en https://), rien d'autre ; null si refusée.
    internal static string? NormalizeUrl(string? raw)
    {
        string url = (raw ?? "").Trim();
        if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url[9..];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || uri.Port
            != 443)
            return null;
        return uri.AbsoluteUri;
    }

    // Nom du réglage si `url` est une expression {{settings.x}} seule, sinon null.
    internal static string? SettingKey(string url)
    {
        if (!WinjetDefinition.SettingsExpression.IsMatch(url))
            return null;
        return url.Trim().Trim('{', '}').Trim()[9..];
    }

    // Libellés « Aujourd'hui / Demain / Toute la journée » par langue d'interface à deux lettres (repli : anglais).
    static readonly Dictionary<string, (string Today, string Tomorrow, string AllDay)> Labels = new()
    {
        ["en"] = ("Today", "Tomorrow", "All day"),
        ["fr"] = ("Aujourd’hui", "Demain", "Toute la journée"),
        ["es"] = ("Hoy", "Mañana", "Todo el día"),
        ["ru"] = ("Сегодня", "Завтра", "Весь день"),
    };

    internal static JsonElement BuildPayload(string status, IReadOnlyList<CalendarEvent> events, DateTime todayLocal, TimeZoneInfo tz,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        var words = Labels.TryGetValue(culture.TwoLetterISOLanguageName, out var found) ? found : Labels["en"];
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("status", status);
            w.WriteNumber("count", events.Count);
            w.WriteStartArray("events");
            foreach (var e in events)
            {
                w.WriteStartObject();
                w.WriteString("title", e.Title);
                w.WriteString("location", e.Location);
                w.WriteString("start", Iso(e.Start, tz));
                w.WriteString("end", Iso(e.End, tz));
                w.WriteBoolean("allDay", e.AllDay);
                w.WriteString("timeLabel", e.AllDay ? words.AllDay : e.End > e.Start
                    ? e.Start.ToString("HH:mm", CultureInfo.InvariantCulture) + "–" + e.End.ToString("HH:mm", CultureInfo.InvariantCulture)
                    : e.Start.ToString("HH:mm", CultureInfo.InvariantCulture));
                var day = e.Start.Date < todayLocal ? todayLocal : e.Start.Date;
                w.WriteString("dayLabel", day == todayLocal ? words.Today : day == todayLocal.AddDays(1) ? words.Tomorrow
                    : day.ToString("dddd", culture));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        using var doc = JsonDocument.Parse(ms.ToArray());
        return doc.RootElement.Clone();
    }

    static string Iso(DateTime local, TimeZoneInfo tz)
    {
        var wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        TimeSpan offset;
        try
        {
            offset = tz.GetUtcOffset(wall);
        }
        catch (ArgumentException) { offset = tz.BaseUtcOffset; }
        return new DateTimeOffset(wall, offset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    // ---- Analyse ICS ----

    sealed record Prop(string Name, Dictionary<string, string> Params, string Value);

    // Lignes dépliées (RFC 5545 : une ligne qui commence par espace ou tabulation prolonge la précédente).
    internal static List<string> Unfold(string ics)
    {
        var lines = new List<string>();
        foreach (string raw in ics.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && lines.Count > 0)
                lines[^1] += raw[1..];
            else if (raw.Length > 0)
                lines.Add(raw);
        }
        return lines;
    }

    static Prop? ParseLine(string line)
    {
        bool quoted = false;
        int colon = -1;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
                quoted = !quoted;
            else if (line[i] == ':' && !quoted)
            {
                colon = i;
                break;
            }
        }
        if (colon <= 0)
            return null;
        var parts = new List<string>();
        var sb = new StringBuilder();
        quoted = false;
        foreach (char c in line[..colon])
        {
            if (c == '"')
                quoted = !quoted;
            if (c == ';' && !quoted)
            {
                parts.Add(sb.ToString());
                sb.Clear();
            }
            else
                sb.Append(c);
        }
        parts.Add(sb.ToString());
        var pars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string p in parts.Skip(1))
        {
            int eq = p.IndexOf('=');
            if (eq > 0)
                pars[p[..eq]] = p[(eq + 1)..].Trim('"');
        }
        return new Prop(parts[0].ToUpperInvariant(), pars, line[(colon + 1)..]);
    }

    internal static string Unescape(string v)
    {
        if (v.IndexOf('\\') < 0)
            return v;
        var sb = new StringBuilder();
        for (int i = 0; i < v.Length; i++)
        {
            if (v[i] == '\\' && i + 1 < v.Length)
            {
                char n = v[++i];
                sb.Append(n is 'n' or 'N' ? '\n' : n);
            }
            else
                sb.Append(v[i]);
        }
        return sb.ToString();
    }

    static string Clean(string v, int max)
    {
        string s = Unescape(v).Replace("\r", " ").Replace('\n', ' ').Replace('\t', ' ').Trim();
        return s.Length > max ? s[..max] : s;
    }

    // Date/heure ICS -> (heure murale dans son fuseau d'origine, fuseau d'origine, journée entière). Null si illisible.
    static (DateTime wall, TimeZoneInfo zone, bool allDay)? ParseDate(Prop p, TimeZoneInfo local)
    {
        string v = p.Value.Trim();
        bool dateOnly = (p.Params.TryGetValue("VALUE", out var vt) && vt.Equals("DATE", StringComparison.OrdinalIgnoreCase)) || v.Length
            == 8;
        if (dateOnly)
            return DateTime.TryParseExact(v[..Math.Min(8, v.Length)], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var d)
                ? (d, local, true) : null;
        bool utc = v.EndsWith('Z');
        if (utc)
            v = v[..^1];
        if (!DateTime.TryParseExact(v, new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm" }, CultureInfo.InvariantCulture, DateTimeStyles.None,
            out var dt))
            return null;
        if (utc)
            return (dt, TimeZoneInfo.Utc, false);
        if (p.Params.TryGetValue("TZID", out var tzid))
        {
            try
            {
                return (dt, TimeZoneInfo.FindSystemTimeZoneById(tzid.TrimStart('/')), false);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        return (dt, local, false);
    }

    static DateTime ToLocal(DateTime wall, TimeZoneInfo from, TimeZoneInfo local)
    {
        if (from == local)
            return wall;
        try
        {
            return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(wall, DateTimeKind.Unspecified), from, local);
        }
        catch (ArgumentException) { return wall; }
    }

    sealed class Rule
    {
        internal string Freq = "";
        internal int Interval = 1, Count = int.MaxValue;
        internal DateTime? UntilLocal;
        internal List<DayOfWeek> ByDay = new();
        internal bool Unsupported;
    }

    static readonly Dictionary<string, DayOfWeek> Days = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MO"] = DayOfWeek.Monday,
        ["TU"] = DayOfWeek.Tuesday,
        ["WE"] = DayOfWeek.Wednesday,
        ["TH"] = DayOfWeek.Thursday,
        ["FR"] = DayOfWeek.Friday,
        ["SA"] = DayOfWeek.Saturday,
        ["SU"] = DayOfWeek.Sunday
    };

    // Règle minimale : DAILY/WEEKLY(+BYDAY)/MONTHLY avec INTERVAL, COUNT, UNTIL. Tout autre cas (BYMONTHDAY, BYSETPOS, YEARLY...) -> null,
    // l'événement est alors traité comme une occurrence unique.
    static Rule? ParseRule(string value, TimeZoneInfo local)
    {
        var r = new Rule();
        foreach (string part in value.Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            string k = part[..eq].ToUpperInvariant(), v = part[(eq + 1)..];
            switch (k)
            {
                case "FREQ":
                    r.Freq = v.ToUpperInvariant();
                    break;
                case "INTERVAL":
                    r.Interval = int.TryParse(v, out int iv) && iv >= 1 ? Math.Min(iv, 1000) : 1;
                    break;
                case "COUNT":
                    r.Count = int.TryParse(v, out int c) && c >= 1 ? c : int.MaxValue;
                    break;
                case "UNTIL":
                    {
                        var u = ParseDate(new Prop("UNTIL", new Dictionary<string, string>(), v), local);
                        if (u is { } uv)
                            r.UntilLocal = uv.allDay ? uv.wall.AddDays(1) : ToLocal(uv.wall, uv.zone, local).AddSeconds(1);
                        break;
                    }
                case "BYDAY":
                    foreach (string d in v.Split(','))
                    {
                        string code = d.Length >= 2 ? d[^2..] : d;
                        if (Days.TryGetValue(code, out var dow))
                            r.ByDay.Add(dow);
                    }
                    break;
                case "WKST":
                    break;
                default:
                    if (k.StartsWith("BY", StringComparison.Ordinal))
                        r.Unsupported = true;
                    break;
            }
        }
        if (r.Freq is not ("DAILY" or "WEEKLY" or "MONTHLY"))
            return null;
        if (r.Freq != "WEEKLY" && r.ByDay.Count > 0)
            return null;
        return r.Unsupported ? null : r;
    }

    // Occurrences en heure murale d'origine, jusqu'à `stopWall` (inclus) ou COUNT.
    // `fromWall` : début utile (heure murale d'origine) ; l'indice d'itération saute arithmétiquement jusqu'juste avant, en
    // comptant les occurrences sautées pour que COUNT s'applique toujours. Au plus MaxWindowIterations itérations ensuite.
    internal const int MaxWindowIterations = 400;

    static IEnumerable<DateTime> Occurrences(DateTime start, Rule r, DateTime stopWall, DateTime fromWall)
    {
        long emitted = 0;
        int k0 = 0;
        if (fromWall > start)
        {
            int interval = Math.Max(1, r.Interval);
            bool byDay = r.Freq == "WEEKLY" && r.ByDay.Count > 0;
            if (r.Freq == "MONTHLY")
            {
                int months = (fromWall.Year - start.Year) * 12 + fromWall.Month - start.Month;
                k0 = Math.Max(0, months / interval - 1);
                for (int k = 0; k < k0; k++)
                    if (start.AddMonths(k * interval).Day == start.Day)
                        emitted++;
            }
            else if (byDay)
            {
                var mondayOf = start.Date.AddDays(-(((int)start.DayOfWeek + 6) % 7));
                k0 = Math.Max(0, (int)Math.Floor((fromWall - mondayOf).TotalDays / (7.0 * interval)) - 1);
                if (k0 > 0)
                {
                    var days = r.ByDay.Distinct().Select(x => ((int)x + 6) % 7).ToList();
                    emitted = days.Count(d => mondayOf.AddDays(d) + start.TimeOfDay >= start) + (long)(k0 - 1) * days.Count;
                }
            }
            else
            {
                double step = (r.Freq == "WEEKLY" ? 7.0 : 1.0) * interval;
                k0 = Math.Max(0, (int)Math.Floor((fromWall - start).TotalDays / step) - 1);
                emitted = k0;
            }
        }
        for (int k = k0; k - k0 < MaxWindowIterations && k < k0 + MaxOccurrences; k++)
        {
            if (r.Freq == "WEEKLY" && r.ByDay.Count > 0)
            {
                var weekStart = start.Date.AddDays(-(((int)start.DayOfWeek + 6) % 7)).AddDays(7.0 * k * r.Interval);
                if (weekStart > stopWall)
                    yield break;
                foreach (var dow in r.ByDay.Distinct().OrderBy(x => ((int)x + 6) % 7))
                {
                    var d = weekStart.AddDays(((int)dow + 6) % 7) + start.TimeOfDay;
                    if (d < start)
                        continue;
                    if (d > stopWall || ++emitted > r.Count)
                        yield break;
                    yield return d;
                }
                continue;
            }
            DateTime next;
            if (r.Freq == "MONTHLY")
            {
                next = start.AddMonths(k * r.Interval);
                if (next > stopWall)
                    yield break;
                if (next.Day != start.Day)
                    continue;
            }
            else
            {
                next = start.AddDays((r.Freq == "WEEKLY" ? 7.0 : 1.0) * k * r.Interval);
                if (next > stopWall)
                    yield break;
            }
            if (++emitted > r.Count)
                yield break;
            yield return next;
        }
    }

    // Événements de la fenêtre [aujourd'hui, aujourd'hui + 7 jours[ (heure locale `tz`), triés, 12 au plus.
    // Les événements horodatés déjà terminés (avant `nowLocal`) sont écartés.
    internal static List<CalendarEvent> Parse(string ics, DateTime nowLocal, TimeZoneInfo tz)
    {
        var winStart = nowLocal.Date;
        var winEnd = winStart.AddDays(WindowDays);
        var events = new List<List<Prop>>();
        List<Prop>? cur = null;
        foreach (string line in Unfold(ics))
        {
            if (line.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
                cur = new List<Prop>();
            else if (line.StartsWith("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (cur != null && events.Count < MaxVevents)
                    events.Add(cur);
                cur = null;
            }
            else if (cur != null && ParseLine(line) is { } p)
                cur.Add(p);
        }
        string Get(List<Prop> props, string name) => props.FirstOrDefault(x => x.Name == name)?.Value ?? "";
        // RECURRENCE-ID : l'occurrence d'origine est remplacée par l'événement modifié.
        var replaced = new HashSet<(string uid, DateTime at)>();
        foreach (var props in events)
            if (props.FirstOrDefault(x => x.Name == "RECURRENCE-ID") is { } rid && ParseDate(rid, tz) is { } rv)
                replaced.Add((Get(props, "UID"), ToLocal(rv.wall, rv.zone, tz)));
        var result = new List<CalendarEvent>();
        foreach (var props in events)
        {
            if (Get(props, "STATUS").Trim().Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))
                continue;
            if (props.FirstOrDefault(x => x.Name == "DTSTART") is not { } dtstart || ParseDate(dtstart, tz) is not { } s)
                continue;
            var span = s.allDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
            if (props.FirstOrDefault(x => x.Name == "DTEND") is { } dtend && ParseDate(dtend, tz) is { } e && e.allDay == s.allDay)
            {
                var diff = e.zone == s.zone ? e.wall - s.wall : ToLocal(e.wall, e.zone, tz) - ToLocal(s.wall, s.zone, tz);
                if (diff >= TimeSpan.Zero)
                    span = diff;
            }
            string title = Clean(Get(props, "SUMMARY"), 120), location = Clean(Get(props, "LOCATION"), 120), uid = Get(props, "UID");
            bool isOverride = props.Any(x => x.Name == "RECURRENCE-ID");

            var exdates = new HashSet<DateTime>();
            foreach (var ex in props.Where(x => x.Name == "EXDATE"))
                foreach (string one in ex.Value.Split(','))
                    if (ParseDate(new Prop("EXDATE", ex.Params, one), tz) is { } xv)
                        exdates.Add(ToLocal(xv.wall, xv.zone, tz));

            var rule = isOverride || props.FirstOrDefault(x => x.Name == "RRULE") is not { } rrule ? null : ParseRule(rrule.Value, tz);
            IEnumerable<DateTime> walls = new[] { s.wall };
            if (rule != null)
            {
                // Borne haute en heure murale d'origine (marge de deux jours pour les écarts de fuseau).
                var stop = (s.zone == tz ? winEnd : ToLocal(winEnd, tz, s.zone)).AddDays(2);
                var from = (s.zone == tz ? winStart : ToLocal(winStart, tz, s.zone)).AddDays(-2) - span;
                walls = Occurrences(s.wall, rule, stop, from);
            }
            foreach (var wall in walls)
            {
                var start = ToLocal(wall, s.zone, tz);
                if (rule?.UntilLocal is { } until && start >= until)
                    break;
                if (start >= winEnd)
                    break;
                var end = start + span;
                if (exdates.Contains(start) || (!isOverride && replaced.Contains((uid, start))))
                    continue;
                if (end <= winStart || (!s.allDay && end <= nowLocal))
                    continue;
                result.Add(new CalendarEvent(title, location, start, end, s.allDay));
            }
        }
        return result.OrderBy(x => x.Start).ThenBy(x => x.AllDay ? 0 : 1).ThenBy(x => x.Title, StringComparer.Ordinal).Take(MaxEvents)
            .ToList();
    }
}
