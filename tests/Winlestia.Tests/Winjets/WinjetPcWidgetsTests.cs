// C#12 / Tests des ajouts « PC Widgets » : bloc note, actions open/openDrive, market Yahoo, source calendar (ICS), service de position.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PanneauxGlass;
using PanneauxGlass.Winjets;
using Xunit;
namespace Winlestia.Tests.Winjets;

public class WinjetPcWidgetsTests
{
    static string Run(Pkg p)
    {
        using var t = new TempDir();
        string path = p.WriteFolder(Directory.CreateDirectory(t.Combine("d")).FullName);
        using var v = WinjetValidator.Validate(path, Pkg.Current, t.Combine("tmp"));
        return v.Ok ? "" : string.Join("\n", v.Errors);
    }

    static Pkg WithBlock(JsonObject block, bool memoState = true, string id = "com.test.demo")
        => Pkg.Default(id).Edit("widget.json", o =>
        {
            if (memoState)
                o["state"]!["memo"] = "";
            o["layouts"]![0]!["root"]!["children"]!.AsArray().Add(block);
        });

    static Pkg WithSource(string name, JsonObject source, params string[] hosts)
        => Pkg.Default().Edit("manifest.json", o => o["permissions"]!["network"] = new JsonArray(hosts.Append("api.example.com").Distinct()
            .Select(h => (JsonNode)h).ToArray()))
            .Edit("widget.json", o => ((JsonObject)o["data"]!)[name] = source);

    // ---- validateur ----

    [Fact]
    public void NoteBlockAccepted()
        => Assert.Equal("", Run(WithBlock(new JsonObject {
            ["type"] = "note", ["key"] = "memo", ["placeholder"] = "Écrire ici", ["grow"] = 1 })));

    [Fact]
    public void NoteBlockNeedsDeclaredStateKey()
        => Assert.Contains("memo", Run(WithBlock(new JsonObject { ["type"] = "note", ["key"] = "memo" }, memoState: false)));

    [Fact]
    public void NoteBlockRejectsUnknownProperty()
        => Assert.NotEqual("", Run(WithBlock(new JsonObject { ["type"] = "note", ["key"] = "memo", ["color"] = "accent" })));

    [Fact]
    public void OpenAndOpenDriveActionsAccepted()
    {
        Assert.Equal("", Run(WithBlock(new JsonObject
        {
            ["type"] = "text", ["text"] = "x", ["action"] = new JsonObject { ["type"] = "open", ["path"] = "{{item.path}}" }
        }, true, "winlestia.demo")));
        Assert.Equal("", Run(WithBlock(new JsonObject
        {
            ["type"] = "text", ["text"] = "x", ["action"] = new JsonObject { ["type"] = "openDrive", ["drive"] = "C:" }
        })));
    }

    [Fact]
    public void OpenActionRequiresPath()
        => Assert.NotEqual("", Run(WithBlock(new JsonObject {
            ["type"] = "text", ["text"] = "x", ["action"] = new JsonObject { ["type"] = "open" } }, true, "winlestia.demo")));

    [Fact]
    public void OpenActionAndFilesFeedRefusedForNonBuiltinPackages()
    {
        var open = new JsonObject { ["type"] = "text", ["text"] = "x", ["action"] = new JsonObject {
            ["type"] = "open", ["path"] = "C:/x" } };
        Assert.Contains("intégrés", Run(WithBlock(open)));
        var files = new JsonObject { ["type"] = "text", ["text"] = "{{files.recent.0.name}}" };
        Assert.Contains("files.", Run(WithBlock(files)));
        Assert.Equal("", Run(WithBlock(new JsonObject { ["type"] = "text", ["text"] = "{{files.recent.0.name}}" }, true,
            "winlestia.demo")));
    }

    [Fact]
    public void YahooMarketAccepted()
        => Assert.Equal("", Run(WithSource("bourse", new JsonObject
        {
            ["type"] = "market", ["provider"] = "yahoo", ["symbols"] = new JsonArray("AAPL", "^GSPC", "EURUSD=X"), ["refresh"] = 120
        }, "query1.finance.yahoo.com")));

    [Fact]
    public void YahooMarketNeedsHost()
        => Assert.Contains("query1.finance.yahoo.com", Run(WithSource("bourse", new JsonObject
        {
            ["type"] = "market", ["provider"] = "yahoo", ["symbols"] = new JsonArray("AAPL")
        }, "api.example.com")));

    [Fact]
    public void YahooMarketRejectsLowercaseSymbol()
        => Assert.NotEqual("", Run(WithSource("bourse", new JsonObject
        {
            ["type"] = "market", ["provider"] = "yahoo", ["symbols"] = new JsonArray("aapl")
        }, "query1.finance.yahoo.com")));

    [Fact]
    public void UnknownMarketProviderRejected()
        => Assert.Contains("provider", Run(WithSource("bourse", new JsonObject
        {
            ["type"] = "market", ["provider"] = "other", ["symbols"] = new JsonArray("AAPL")
        }, "query1.finance.yahoo.com")));

    [Fact]
    public void CalendarFromSettingNeedsNoHost()
        => Assert.Equal("", Run(WithSource("agenda", new JsonObject
        {
            ["type"] = "calendar", ["url"] = "{{settings.city}}", ["refresh"] = 900
        }, "api.example.com")));

    [Theory]
    [InlineData("fr", "Aujourd’hui", "Demain", "Toute la journée", "vendredi")]
    [InlineData("es", "Hoy", "Mañana", "Todo el día", "viernes")]
    [InlineData("de", "Today", "Tomorrow", "All day", "Freitag")]
    public void IcsPayloadIsLocalized(string lang, string today, string tomorrow, string allDay, string friday)
    {
        var ev = WinjetCalendar.Parse(Ics, Now, TimeZoneInfo.Utc);
        var p = WinjetCalendar.BuildPayload("ok", ev, Now.Date, TimeZoneInfo.Utc, new System.Globalization.CultureInfo(lang));
        var events = p.GetProperty("events");
        Assert.Equal(today, events[0].GetProperty("dayLabel").GetString());
        Assert.Equal(allDay, events[2].GetProperty("timeLabel").GetString());
        Assert.Equal(tomorrow, events[2].GetProperty("dayLabel").GetString());
        Assert.Equal(friday, events[4].GetProperty("dayLabel").GetString());
    }

    [Theory]
    [InlineData("http://example.org/a.ics")]
    [InlineData("webcal://example.org/a.ics")]
    [InlineData("https://example.org/a.ics")]
    [InlineData("prefix {{settings.city}}")]
    [InlineData("{{state.mode}}")]
    public void CalendarLiteralUrlStillNeedsDeclaredHttpsHost(string url)
        => Assert.NotEqual("", Run(WithSource("agenda", new JsonObject { ["type"] = "calendar", ["url"] = url }, "api.example.com")));

    // ---- Yahoo ----

    static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    static string YahooBody(string meta, string closes)
        => "{\"chart\":{\"result\":[{\"meta\":" + meta + ",\"timestamp\":["
            + string.Join(",", Enumerable.Range(0, closes.Split(',').Length).Select(i => 1700000000 + i * 300))
           + "],\"indicators\":{\"quote\":[{\"close\":[" + closes + "]}]}}],\"error\":null}}";

    [Fact]
    public void YahooParsesPriceChangeNameAndHistory()
    {
        var q = WinjetMarket.ParseYahoo("AAPL", Json(YahooBody(
            "{\"regularMarketPrice\":110.0,\"chartPreviousClose\":100.0,\"shortName\":\"Apple Inc.\",\"longName\":\"Apple Inc. Long\"}",
            "100.5,null,101,null,110.0")));
        Assert.Equal("ok", q.Status);
        Assert.Equal(110.0, q.Price);
        Assert.Equal(10.0, q.Change);
        Assert.Equal("Apple Inc.", q.Name);
        Assert.Equal(new[] { 100.5, 101, 110.0 }, q.History);
        Assert.Equal(3, q.Candles.Count);
    }

    [Fact]
    public void YahooFallsBackToSymbolAndLastCloseAndCapsHistory()
    {
        string closes = string.Join(",", Enumerable.Range(1, 100).Select(i => i.ToString()));
        var q = WinjetMarket.ParseYahoo("^GSPC", Json(YahooBody("{\"previousClose\":50}", closes)));
        Assert.Equal("^GSPC", q.Name);
        Assert.Equal(100, q.Price);
        Assert.Equal(100.0, q.Change);
        Assert.Equal(60, q.History.Count);
        Assert.Equal(100, q.History[^1]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"chart\":{\"result\":null,\"error\":{\"code\":\"Not Found\"}}}")]
    [InlineData("[1]")]
    public void YahooBadShapeIsErrorItem(string body)
        => Assert.Equal("error", WinjetMarket.ParseYahoo("ZZZ", Json(body)).Status);

    [Fact]
    public void YahooPayloadShapeMatchesBinanceItems()
    {
        var q = WinjetMarket.ParseYahoo("AAPL", Json(YahooBody("{\"regularMarketPrice\":2,\"previousClose\":1}", "1,2")));
        var p = WinjetMarket.BuildYahooPayload(5, new[] { q, WinjetMarket.YahooError("BAD") });
        var item = p.GetProperty("symbols").GetProperty("AAPL");
        Assert.Equal(2, item.GetProperty("price").GetDouble());
        Assert.Equal(100, item.GetProperty("change24h").GetDouble());
        Assert.Equal(2, item.GetProperty("history").GetArrayLength());
        Assert.Equal(2, item.GetProperty("candles").GetProperty("1j").GetArrayLength());
        Assert.Equal(0, item.GetProperty("candles").GetProperty("1h").GetArrayLength());
        Assert.Equal("error", p.GetProperty("symbols").GetProperty("BAD").GetProperty("status").GetString());
        Assert.True(p.TryGetProperty("live", out _));
    }

    // ---- ICS ----

    internal const string Ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\n"
        + "BEGIN:VEVENT\r\nUID:a1\r\nDTSTART;VALUE=DATE:20260106\r\nDTEND;VALUE=DATE:20260107\r\nSUMMARY:Holiday\r\nEND:VEVENT\r\n"
        + "BEGIN:VEVENT\r\nUID:a2\r\nDTSTART;TZID=Europe/Paris:20260105T100000\r\nDTEND;TZID=Europe/Paris:20260105T110000\r\n"
        + "SUMMARY:Stand\\, up\r\n  with team\r\nLOCATION:Room 1\\;\r\n B\r\nEND:VEVENT\r\n"
        + "BEGIN:VEVENT\r\nUID:a3\r\nDTSTART:20260105T070000Z\r\nDTEND:20260105T080000Z\r\nSUMMARY:Already over\r\nEND:VEVENT\r\n"
        + "BEGIN:VEVENT\r\nUID:a4\r\nDTSTART:20251229T093000Z\r\nDTEND:20251229T100000Z\r\nRRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=20\r\n"
        + "EXDATE:20260107T093000Z\r\nSUMMARY:Weekly\r\nEND:VEVENT\r\n"
        + "BEGIN:VEVENT\r\nUID:a5\r\nDTSTART:20251206T120000Z\r\nRRULE:FREQ=MONTHLY\r\nSUMMARY:Monthly\r\nEND:VEVENT\r\n"
        + "BEGIN:VEVENT\r\nUID:a6\r\nDTSTART:20260108T120000Z\r\nSTATUS:CANCELLED\r\nSUMMARY:Cancelled\r\nEND:VEVENT\r\n"
        + "BEGIN:VEVENT\r\nUID:a7\r\nDTSTART:20260120T120000Z\r\nSUMMARY:Too far\r\nEND:VEVENT\r\n"
        + "END:VCALENDAR\r\n";

    static readonly DateTime Now = new(2026, 1, 5, 8, 0, 0);

    [Fact]
    public void IcsWindowSortingAndTimes()
    {
        var ev = WinjetCalendar.Parse(Ics, Now, TimeZoneInfo.Utc);
        Assert.Equal(new[] { "Stand, up with team", "Weekly", "Holiday", "Monthly", "Weekly" }, ev.Select(e => e.Title).ToArray());
        Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0), ev[0].Start);
        Assert.Equal(new DateTime(2026, 1, 5, 10, 0, 0), ev[0].End);
        Assert.Equal("Room 1;B", ev[0].Location);
        Assert.Equal(new DateTime(2026, 1, 5, 9, 30, 0), ev[1].Start);
        Assert.True(ev[2].AllDay);
        Assert.Equal(new DateTime(2026, 1, 6), ev[2].Start);
        Assert.Equal(new DateTime(2026, 1, 6, 12, 0, 0), ev[3].Start);
        Assert.Equal(new DateTime(2026, 1, 9, 9, 30, 0), ev[4].Start);
    }

    [Fact]
    public void IcsExdateAndCancelledAndPastAreDropped()
    {
        var titles = WinjetCalendar.Parse(Ics, Now, TimeZoneInfo.Utc).Select(e => e.Title).ToList();
        Assert.DoesNotContain("Cancelled", titles);
        Assert.DoesNotContain("Already over", titles);
        Assert.DoesNotContain("Too far", titles);
        Assert.Equal(2, titles.Count(t => t == "Weekly"));
    }

    [Fact]
    public void IcsConvertsToLocalZone()
    {
        var plus2 = TimeZoneInfo.CreateCustomTimeZone("T+2", TimeSpan.FromHours(2), "T+2", "T+2");
        var ev = WinjetCalendar.Parse(Ics, Now, plus2);
        Assert.Equal(new DateTime(2026, 1, 5, 11, 30, 0), ev.First(e => e.Title == "Weekly").Start);
        Assert.Equal(new DateTime(2026, 1, 5, 11, 0, 0), ev.First(e => e.Title.StartsWith("Stand")).Start);
    }

    [Fact]
    public void IcsCountAndUntilStopRecurrence()
    {
        string ics = "BEGIN:VEVENT\r\nDTSTART:20260105T100000Z\r\nRRULE:FREQ=DAILY;COUNT=2\r\nSUMMARY:A\r\nEND:VEVENT\r\n"
            + "BEGIN:VEVENT\r\nDTSTART:20260105T110000Z\r\nRRULE:FREQ=DAILY;INTERVAL=2;UNTIL=20260108T000000Z\r\nSUMMARY:B\r\n"
            + "END:VEVENT\r\n";
        var ev = WinjetCalendar.Parse(ics, Now, TimeZoneInfo.Utc);
        Assert.Equal(2, ev.Count(e => e.Title == "A"));
        Assert.Equal(new[] { 5, 7 }, ev.Where(e => e.Title == "B").Select(e => e.Start.Day).ToArray());
    }

    [Fact]
    public void IcsOldDailyEventStillAppearsAndCountSurvivesJump()
    {
        string start = Now.Date.AddDays(-7300).ToString("yyyyMMdd");
        string Ev(string rule) => "BEGIN:VEVENT\r\nDTSTART:" + start + "T100000Z\r\nRRULE:" + rule + "\r\nSUMMARY:Old\r\nEND:VEVENT\r\n";
        var open = WinjetCalendar.Parse(Ev("FREQ=DAILY"), Now, TimeZoneInfo.Utc);
        Assert.Equal(7, open.Count);
        // COUNT=7303 : dernière occurrence à J+2, donc J, J+1, J+2 seulement.
        var counted = WinjetCalendar.Parse(Ev("FREQ=DAILY;COUNT=7303"), Now, TimeZoneInfo.Utc);
        Assert.Equal(new[] { 5, 6, 7 }, counted.Select(e => e.Start.Day).ToArray());
        var weekly = WinjetCalendar.Parse(Ev("FREQ=WEEKLY"), Now, TimeZoneInfo.Utc);
        Assert.NotEmpty(weekly);
    }

    [Fact]
    public void IcsCapsAtTwelveEvents()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 30; i++)
            sb.Append("BEGIN:VEVENT\r\nDTSTART:20260106T").Append((i % 24).ToString("00")).Append("0000Z\r\nSUMMARY:E").Append(i)
                .Append("\r\nEND:VEVENT\r\n");
        Assert.Equal(12, WinjetCalendar.Parse(sb.ToString(), Now, TimeZoneInfo.Utc).Count);
    }

    [Fact]
    public void IcsPayloadShape()
    {
        var ev = WinjetCalendar.Parse(Ics, Now, TimeZoneInfo.Utc);
        var p = WinjetCalendar.BuildPayload("ok", ev, Now.Date, TimeZoneInfo.Utc, new System.Globalization.CultureInfo("en"));
        Assert.Equal("ok", p.GetProperty("status").GetString());
        Assert.Equal(5, p.GetProperty("count").GetInt32());
        var first = p.GetProperty("events")[0];
        Assert.Equal("09:00\u201310:00", first.GetProperty("timeLabel").GetString());
        Assert.Equal("Today", first.GetProperty("dayLabel").GetString());
        Assert.False(first.GetProperty("allDay").GetBoolean());
        Assert.StartsWith("2026-01-05T09:00:00", first.GetProperty("start").GetString());
        var holiday = p.GetProperty("events")[2];
        Assert.Equal("All day", holiday.GetProperty("timeLabel").GetString());
        Assert.Equal("Tomorrow", holiday.GetProperty("dayLabel").GetString());
        Assert.Equal("Friday", p.GetProperty("events")[4].GetProperty("dayLabel").GetString());
    }

    [Theory]
    [InlineData("webcal://example.org/a.ics", "https://example.org/a.ics")]
    [InlineData("https://example.org/a.ics", "https://example.org/a.ics")]
    [InlineData("http://example.org/a.ics", null)]
    [InlineData("ftp://example.org/a.ics", null)]
    [InlineData("file:///c:/a.ics", null)]
    [InlineData("https://user:pw@example.org/a.ics", null)]
    [InlineData("https://example.org:8443/a.ics", null)]
    [InlineData("", null)]
    public void IcsUrlNormalization(string raw, string? expected) => Assert.Equal(expected, WinjetCalendar.NormalizeUrl(raw));

    // ---- service de données ----

    sealed class Fake : HttpMessageHandler
    {
        internal int Calls;
        internal readonly List<string> Urls = new();
        internal Func<HttpRequestMessage, HttpResponseMessage> Reply = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            lock (Urls)
                Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(Reply(request));
        }
    }

    sealed class Rig : IDisposable
    {
        internal readonly TempDir Dir = new();
        internal readonly Fake Http = new();
        internal DateTimeOffset Now = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);
        internal readonly WinjetDataService Service;
        internal WinjetPollTarget Target = null!;

        internal Rig()
        {
            Service = new WinjetDataService(Dir.Combine("cache"), Http, () => Now) { LocalZone = TimeZoneInfo.Utc };
        }

        internal void Load(string name, JsonObject source, string icsUrl, params string[] hosts)
        {
            var pkg = WithSource(name, source, hosts);
            string dir = pkg.WriteFolder(Directory.CreateDirectory(Dir.Combine("pkg" + name)).FullName);
            using var v = WinjetValidator.ValidateDirectory(dir, Pkg.Current);
            Assert.True(v.Ok, string.Join("|", v.Errors));
            Target = new("w-1", v.Package!, new Dictionary<string, object?> { ["city"] = icsUrl }, new Dictionary<string, object?>());
        }

        public void Dispose()
        {
            Service.Dispose();
            Dir.Dispose();
        }
    }

    static JsonObject CalendarSource() => new() { ["type"] = "calendar", ["url"] = "{{settings.city}}", ["refresh"] = 10 };

    [Fact]
    public async Task CalendarFetchesSettingUrlAnyHostAndRewritesWebcal()
    {
        using var r = new Rig();
        r.Http.Reply = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Ics, Encoding.UTF8, "text/calendar") };
        r.Load("agenda", CalendarSource(), "webcal://calendar.example.net/me.ics", "api.example.com");
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Equal(new[] { "https://calendar.example.net/me.ics" }, r.Http.Urls);
        var v = r.Service.Get("w-1", "agenda")!;
        Assert.Equal("ok", v.Value!.Value.GetProperty("status").GetString());
        Assert.Equal(5, v.Value!.Value.GetProperty("count").GetInt32());
    }

    static HttpResponseMessage Redirect(string to)
    {
        var m = new HttpResponseMessage(HttpStatusCode.Found);
        m.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
        return m;
    }

    [Fact]
    public async Task CalendarFollowsRedirectsUpToThreeAndRevalidates()
    {
        using var r = new Rig();
        r.Http.Reply = q => q.RequestUri!.AbsolutePath switch
        {
            "/a.ics" => Redirect("https://b.example.net/b.ics"),
            "/b.ics" => Redirect("/c.ics"),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Ics, Encoding.UTF8, "text/calendar") }
        };
        r.Load("agenda", CalendarSource(), "https://a.example.net/a.ics", "api.example.com");
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Equal(new[] { "https://a.example.net/a.ics", "https://b.example.net/b.ics", "https://b.example.net/c.ics" }, r.Http.Urls);
        Assert.Equal("ok", r.Service.Get("w-1", "agenda")!.Value!.Value.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("http://evil.example.net/x.ics")]
    [InlineData("https://evil.example.net:8443/x.ics")]
    [InlineData("https://user:pw@evil.example.net/x.ics")]
    public async Task CalendarRefusesUnsafeRedirectTarget(string target)
    {
        using var r = new Rig();
        r.Http.Reply = _ => Redirect(target);
        r.Load("agenda", CalendarSource(), "https://a.example.net/a.ics", "api.example.com");
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Single(r.Http.Urls);
        Assert.Equal("error", r.Service.Get("w-1", "agenda")!.Value!.Value.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CalendarStopsAfterThreeRedirects()
    {
        using var r = new Rig();
        r.Http.Reply = _ => Redirect("https://a.example.net/loop.ics");
        r.Load("agenda", CalendarSource(), "https://a.example.net/a.ics", "api.example.com");
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Equal(4, r.Http.Urls.Count);
        Assert.Equal("error", r.Service.Get("w-1", "agenda")!.Value!.Value.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CalendarEmptyUrlIsUnconfiguredWithoutNetwork()
    {
        using var r = new Rig();
        r.Load("agenda", CalendarSource(), "", "api.example.com");
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Equal(0, r.Http.Calls);
        var p = r.Service.Get("w-1", "agenda")!.Value!.Value;
        Assert.Equal("unconfigured", p.GetProperty("status").GetString());
        Assert.Equal(0, p.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task CalendarRejectsHttpWithoutNetwork()
    {
        using var r = new Rig();
        r.Load("agenda", CalendarSource(), "http://example.org/a.ics", "api.example.com");
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Equal(0, r.Http.Calls);
        Assert.Equal("error", r.Service.Get("w-1", "agenda")!.Value!.Value.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CalendarRefreshFloorIsFiveMinutesButUrlChangeIsImmediate()
    {
        using var r = new Rig();
        r.Http.Reply = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Ics) };
        r.Load("agenda", CalendarSource(), "https://a.example.net/x.ics", "api.example.com");
        await r.Service.RefreshAsync(r.Target, false);
        r.Now += TimeSpan.FromSeconds(120);
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Equal(1, r.Http.Calls);
        r.Now += TimeSpan.FromSeconds(181);
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Equal(2, r.Http.Calls);
        var changed = new WinjetPollTarget("w-1", r.Target.Package, new Dictionary<string, object?> {
            ["city"] = "https://b.example.net/y.ics" },
            r.Target.State);
        await r.Service.RefreshAsync(changed, true);
        Assert.Equal(3, r.Http.Calls);
    }

    [Fact]
    public async Task CalendarOversizedResponseIsError()
    {
        using var r = new Rig();
        r.Http.Reply = _ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(new string('x', (int)WinjetLimits.MaxIcsBytes + 10)) };
        r.Load("agenda", CalendarSource(), "https://a.example.net/x.ics", "api.example.com");
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Equal("error", r.Service.Get("w-1", "agenda")!.Value!.Value.GetProperty("status").GetString());
    }

    [Fact]
    public async Task YahooFetchesEachSymbolAndFailureBecomesErrorItem()
    {
        using var r = new Rig();
        r.Http.Reply = req => req.RequestUri!.AbsolutePath.EndsWith("/AAPL")
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(YahooBody("{\"regularMarketPrice\":110,\"previousClose\":100,\"shortName\":\"Apple\"}",
                    "100,110"),
                    Encoding.UTF8, "application/json")
            }
            : new HttpResponseMessage(HttpStatusCode.InternalServerError);
        r.Load("bourse", new JsonObject
        {
            ["type"] = "market", ["provider"] = "yahoo", ["symbols"] = new JsonArray("AAPL", "MSFT")
        }, "", "query1.finance.yahoo.com");
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Equal(2, r.Http.Calls);
        Assert.All(r.Http.Urls, u => Assert.StartsWith("https://query1.finance.yahoo.com/v8/finance/chart/", u));
        Assert.All(r.Http.Urls, u => Assert.EndsWith("?range=1d&interval=5m", u));
        var s = r.Service.Get("w-1", "bourse")!.Value!.Value.GetProperty("symbols");
        Assert.Equal("ok", s.GetProperty("AAPL").GetProperty("status").GetString());
        Assert.Equal(10, s.GetProperty("AAPL").GetProperty("change24h").GetDouble());
        Assert.Equal("error", s.GetProperty("MSFT").GetProperty("status").GetString());
    }

    [Fact]
    public async Task YahooNetworkExceptionDoesNotCrash()
    {
        using var r = new Rig();
        r.Http.Reply = _ => throw new HttpRequestException("down");
        r.Load("bourse", new JsonObject { ["type"] = "market", ["provider"] = "yahoo", ["symbols"] = new JsonArray("AAPL") }, "",
            "query1.finance.yahoo.com");
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Equal("error", r.Service.Get("w-1", "bourse")!.Value!.Value.GetProperty("symbols").GetProperty("AAPL").GetProperty("status")
            .GetString());
    }

    // ---- position ----

    [Fact]
    public async Task LocationCachesSuccessAndRetriesFailureLater()
    {
        int calls = 0, changes = 0;
        bool ok = false;
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var svc = new LocationService(() => changes++, () =>
        {
            calls++;
            return Task.FromResult(ok ? new LocationFix(48.857, 2.352, 100, true) : new LocationFix(0, 0, 0, false));
        }, () => now);
        Assert.False(JsonSerializer.SerializeToElement(svc.Snapshot()).GetProperty("ok").GetBoolean());
        Assert.False(JsonSerializer.SerializeToElement(svc.Snapshot()).GetProperty("requested").GetBoolean());
        await svc.Request();
        Assert.True(JsonSerializer.SerializeToElement(svc.Snapshot()).GetProperty("requested").GetBoolean());
        Assert.Equal(1, calls);
        await svc.Request();
        Assert.Equal(1, calls);
        now += TimeSpan.FromSeconds(61);
        ok = true;
        await svc.Request();
        Assert.Equal(2, calls);
        var snap = JsonSerializer.SerializeToElement(svc.Snapshot());
        Assert.True(snap.GetProperty("ok").GetBoolean());
        Assert.Equal(48.857, snap.GetProperty("lat").GetDouble());
        Assert.Equal(100, snap.GetProperty("accuracyM").GetDouble());
        now += TimeSpan.FromMinutes(29);
        await svc.Request();
        Assert.Equal(2, calls);
        now += TimeSpan.FromMinutes(2);
        await svc.Request();
        Assert.Equal(3, calls);
        Assert.Equal(3, changes);
    }

    [Fact]
    public async Task LocationProviderExceptionGivesNotOk()
    {
        var svc = new LocationService(() => { }, () => throw new UnauthorizedAccessException("denied"));
        await svc.Request();
        Assert.False(JsonSerializer.SerializeToElement(svc.Snapshot()).GetProperty("ok").GetBoolean());
        Assert.True(JsonSerializer.SerializeToElement(svc.Snapshot()).GetProperty("requested").GetBoolean());
    }
}
