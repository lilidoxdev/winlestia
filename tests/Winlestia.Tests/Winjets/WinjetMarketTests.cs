// C#12 / Tests de la source « market » (bougies, symboles, charge utile), de la validation associée et des nouveaux blocs/actions.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using PanneauxGlass;
using PanneauxGlass.Winjets;
using Xunit;
namespace Winlestia.Tests.Winjets;

public class WinjetMarketTests
{
    static string Run(Pkg p)
    {
        using var t = new TempDir();
        string path = p.WriteFolder(Directory.CreateDirectory(t.Combine("d")).FullName);
        using var v = WinjetValidator.Validate(path, Pkg.Current, t.Combine("tmp"));
        return v.Ok ? "" : string.Join("\n", v.Errors);
    }

    static Pkg WithMarket(JsonNode? symbols, bool hosts = true, bool replaceBlocks = false)
    {
        var p = Pkg.Default();
        if (hosts)
            p.Edit("manifest.json", o => o["permissions"]!["network"] = new JsonArray("api.binance.com", "stream.binance.com"));
        return p.Edit("widget.json", o =>
        {
            ((JsonObject)o["data"]!)["market"] = new JsonObject { ["type"] = "market", ["symbols"] = symbols };
            ((JsonObject)o["data"]!).Remove("live");
        });
    }

    static Pkg WithBlock(JsonObject block)
        => Pkg.Default().Edit("widget.json", o => o["layouts"]![0]!["root"]!["children"]!.AsArray().Add(block));

    // ---- symboles ----

    [Theory]
    [InlineData("BTC", true)]
    [InlineData("1INCH", true)]
    [InlineData("AB", true)]
    [InlineData("ABCDEFGHIJ", true)]
    [InlineData("A", false)]
    [InlineData("ABCDEFGHIJK", false)]
    [InlineData("btc", false)]
    [InlineData("BTC-USD", false)]
    [InlineData("BTC\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SymbolValidation(string? s, bool ok) => Assert.Equal(ok, WinjetMarket.IsValidSymbol(s));

    // ---- logique de bougies ----

    [Fact]
    public void RollUpdatesLastCandle()
    {
        var c = new List<double[]> { new double[] { 60_000, 10, 12, 9, 11 } };
        Assert.True(WinjetMarket.Roll(c, 60, 60_000, 60_000 + 5_000, 11, 15, 8, 14));
        Assert.Equal(new double[] { 60_000, 10, 15, 8, 14 }, c[0]);
        Assert.False(WinjetMarket.Roll(c, 60, 60_000, 60_000, 11, 15, 8, 14));
        Assert.Single(c);
    }

    [Fact]
    public void RollOpensNewCandleAtBoundaryAndTrims()
    {
        var c = new List<double[]> { new double[] { 0, 1, 2, 1, 2 }, new double[] { 60_000, 2, 3, 2, 3 } };
        Assert.True(WinjetMarket.Roll(c, 2, 60_000, 120_000, 3, 4, 3, 4));
        Assert.Equal(2, c.Count);
        Assert.Equal(new double[] { 60_000, 2, 3, 2, 3 }, c[0]);
        Assert.Equal(new double[] { 120_000, 3, 4, 3, 4 }, c[1]);
    }

    [Fact]
    public void RollAlignsOnLargerBuckets()
    {
        long fifteen = 15 * 60_000L;
        var c = new List<double[]> { new double[] { fifteen, 5, 6, 4, 5 } };
        // une minute plus tard : même bougie de 15 min
        Assert.True(WinjetMarket.Roll(c, 96, fifteen, fifteen + 60_000, 5, 7, 5, 7));
        Assert.Single(c);
        Assert.Equal(7, c[0][2]);
        // franchissement de la frontière : nouvelle bougie alignée
        Assert.True(WinjetMarket.Roll(c, 96, fifteen, 2 * fifteen + 60_000, 7, 8, 7, 8));
        Assert.Equal(2 * fifteen, c[1][0]);
    }

    [Fact]
    public void RollIgnoresOldAndHandlesEmpty()
    {
        var c = new List<double[]>();
        Assert.True(WinjetMarket.Roll(c, 60, 60_000, 120_000, 1, 2, 1, 2));
        Assert.False(WinjetMarket.Roll(c, 60, 60_000, 60_000, 9, 9, 9, 9));
        Assert.Single(c);
    }

    [Fact]
    public void KlinesAndMessageParsing()
    {
        using var doc = JsonDocument.Parse("[[1000,\"1.5\",\"2\",\"1\",\"1.8\",\"9\"],[\"bad\"],[2000,\"x\",\"2\",\"1\",\"1\"]]");
        var list = WinjetMarket.ParseKlines(doc.RootElement)!;
        Assert.Single(list);
        Assert.Equal(new double[] { 1000, 1.5, 2, 1, 1.8 }, list[0]);
        Assert.True(WinjetMarket.ParseKlineMessage(
            "{\"stream\":\"btcusdt@kline_1m\",\"data\":{\"e\":\"kline\",\"s\":\"BTCUSDT\",\"k\":{\"t\":120000,\"o\":\"1\",\"h\":\"3\",\"l\":\"0.5\",\"c\":\"2\"}}}",
            out string sym, out long t, out double o, out double h, out double l, out double c));
        Assert.Equal(("BTC", 120000L, 1.0, 3.0, 0.5, 2.0), (sym, t, o, h, l, c));
        Assert.False(WinjetMarket.ParseKlineMessage("{\"data\":{}}", out _, out _, out _, out _, out _, out _));
        Assert.False(WinjetMarket.ParseKlineMessage("not json", out _, out _, out _, out _, out _, out _));
    }

    [Fact]
    public void ChangeAndUrls()
    {
        Assert.Equal(10, WinjetMarket.Change24h(new List<double[]> { new double[] { 0, 100, 0, 0, 0 } }, 110));
        Assert.Equal(0, WinjetMarket.Change24h(new List<double[]>(), 110));
        Assert.Equal("wss://stream.binance.com:9443/stream?streams=btcusdt@kline_1m/ethusdt@kline_1m",
            WinjetMarket.StreamUrl(new[] { "BTC", "ETH" }));
        Assert.Equal("https://api.binance.com/api/v3/klines?symbol=BTCUSDT&interval=2h&limit=84",
            WinjetMarket.KlinesUrl("BTC", WinjetMarket.Timeframes[2]));
    }

    [Fact]
    public void PayloadShape()
    {
        var candles = new Dictionary<string, List<double[]>> { ["1h"] = new() { new double[] { 1, 2, 3, 1, 2 } } };
        var el = WinjetMarket.BuildPayload(true, 1234, new[] { new WinjetMarket.SymbolView("BTC", 94812.5, 2.41, candles) });
        Assert.True(el.GetProperty("live").GetBoolean());
        Assert.Equal(1234, el.GetProperty("updated").GetInt64());
        var btc = el.GetProperty("symbols").GetProperty("BTC");
        Assert.Equal(94812.5, btc.GetProperty("price").GetDouble());
        Assert.Equal(2.41, btc.GetProperty("change24h").GetDouble());
        Assert.Equal(5, btc.GetProperty("candles").GetProperty("1h")[0].GetArrayLength());
        Assert.Equal(0, btc.GetProperty("candles").GetProperty("1j").GetArrayLength());
    }

    // ---- flux partagé (sans réseau) ----

    [Fact]
    public void FeedPublishesLiveMessagesThrottledAndStopsWhenUnused()
    {
        var published = new List<(string inst, string src, JsonElement v)>();
        using var feed = new WinjetMarketFeed((_, _) => System.Threading.Tasks.Task.FromResult<JsonElement?>(null),
            (i, s, v) => { lock (published) published.Add((i, s, v)); }, null, false);
        Assert.False(feed.Running);
        feed.Update(new[] { new MarketSub("i1", "market", new[] { "BTC" }), new MarketSub("i2", "market", new[] { "BTC", "ETH" }) });
        Assert.True(feed.Running);
        for (int k = 0; k < 20; k++)
            feed.OnMessage("{\"data\":{\"s\":\"BTCUSDT\",\"k\":{\"t\":60000,\"o\":\"1\",\"h\":\"" + (2 + k) + "\",\"l\":\"1\",\"c\":\"" + (2 + k) + "\"}}}");
        System.Threading.Thread.Sleep(1800);
        lock (published)
        {
            Assert.InRange(published.Count(p => p.inst == "i1"), 1, 3);
            var last = published.Last(p => p.inst == "i1").v;
            Assert.Equal(21, last.GetProperty("symbols").GetProperty("BTC").GetProperty("price").GetDouble());
        }
        feed.Update(Array.Empty<MarketSub>());
        Assert.False(feed.Running);
    }

    [Fact]
    public void TickerChangeRefreshesNewRunWhileOldRequestInFlight()
    {
        var urls = new List<string>();
        var gate = new System.Threading.Tasks.TaskCompletionSource<JsonElement?>();
        using var feed = new WinjetMarketFeed((u, _) => { lock (urls) urls.Add(u); return gate.Task; },
            (_, _, _) => { }, null, false);
        feed.Update(new[] { new MarketSub("i1", "market", new[] { "BTC" }) });
        feed.RefreshNow();
        feed.Update(new[] { new MarketSub("i1", "market", new[] { "ETH" }) });
        feed.RefreshNow();
        System.Threading.SpinWait.SpinUntil(() => { lock (urls) return urls.Any(u => u.Contains("ETH")); }, 3000);
        lock (urls)
            Assert.Contains(urls, u => u.Contains("ETH"));
        gate.TrySetResult(null);
    }

    // ---- validateur : source market ----

    [Fact]
    public void MarketSourceAccepted() => Assert.Equal("", Run(WithMarket(new JsonArray("BTC", "ETH", "SOL"))));

    [Fact]
    public void MarketSourceRejectsMissingHosts()
    {
        string errors = Run(WithMarket(new JsonArray("BTC"), false));
        Assert.Contains("api.binance.com", errors);
        Assert.Contains("stream.binance.com", errors);
    }

    [Fact]
    public void MarketSourceRejectsBadSymbols()
    {
        Assert.NotEqual("", Run(WithMarket(new JsonArray("btc"))));
        Assert.NotEqual("", Run(WithMarket(new JsonArray("BTC", "ETH", "SOL", "ADA", "XRP"))));
        Assert.NotEqual("", Run(WithMarket(new JsonArray())));
        Assert.NotEqual("", Run(WithMarket(new JsonArray("BTC", "BTC"))));
        Assert.NotEqual("", Run(WithMarket(null)));
        Assert.NotEqual("", Run(WithMarket(new JsonArray("BTC")).Edit("widget.json", o => o["data"]!["market"]!["url"] = "https://x.example.com")));
    }

    // ---- validateur : nouveaux blocs, actions, propriétés ----

    [Fact]
    public void NewBlocksAndPropsAccepted()
    {
        Assert.Equal("", Run(WithBlock(new JsonObject { ["type"] = "visualizer", ["bars"] = 32, ["source"] = "spectrum" })));
        Assert.Equal("", Run(WithBlock(new JsonObject { ["type"] = "visualizer" })));
        Assert.Equal("", Run(WithBlock(new JsonObject { ["type"] = "media", ["showArt"] = true, ["showProgress"] = "{{ settings.showSeconds }}", ["showControls"] = false })));
        Assert.Equal("", Run(WithBlock(new JsonObject { ["type"] = "chart", ["series"] = "data.live.prices", ["variant"] = "candles", ["hover"] = true, ["valueFormat"] = "currency" })));
        Assert.Equal("", Run(WithBlock(new JsonObject { ["type"] = "clock", ["variant"] = "digital", ["seconds"] = true })));
    }

    [Fact]
    public void NewBlocksRejectBadValues()
    {
        Assert.NotEqual("", Run(WithBlock(new JsonObject { ["type"] = "visualizer", ["bars"] = 7 })));
        Assert.NotEqual("", Run(WithBlock(new JsonObject { ["type"] = "visualizer", ["bars"] = 65 })));
        Assert.NotEqual("", Run(WithBlock(new JsonObject { ["type"] = "visualizer", ["source"] = "mic" })));
        Assert.NotEqual("", Run(WithBlock(new JsonObject { ["type"] = "media", ["showArt"] = 3 })));
        Assert.NotEqual("", Run(WithBlock(new JsonObject { ["type"] = "media", ["bogus"] = true })));
        Assert.NotEqual("", Run(WithBlock(new JsonObject { ["type"] = "chart", ["series"] = "a.b", ["variant"] = "pie" })));
        Assert.NotEqual("", Run(WithBlock(new JsonObject { ["type"] = "chart", ["series"] = "a.b", ["valueFormat"] = 1 })));
    }

    [Theory]
    [InlineData("media.playPause", true)]
    [InlineData("media.next", true)]
    [InlineData("media.prev", true)]
    [InlineData("openSettings", true)]
    [InlineData("media.stop", false)]
    public void MediaActions(string type, bool ok)
    {
        string errors = Run(WithBlock(new JsonObject { ["type"] = "stat", ["value"] = "1", ["action"] = new JsonObject { ["type"] = type } }));
        Assert.Equal(ok, errors == "");
    }

    [Fact]
    public void MediaActionRejectsExtraProps()
        => Assert.NotEqual("", Run(WithBlock(new JsonObject { ["type"] = "stat", ["value"] = "1",
            ["action"] = new JsonObject { ["type"] = "media.next", ["x"] = 1 } })));

    [Fact]
    public void ContextPathsAreNotRootRestricted()
        => Assert.Equal("", Run(WithBlock(new JsonObject { ["type"] = "text", ["text"] = "{{ media.title }} {{ system.cpu }} {{ time.hours }}" })));

    [Fact]
    public void MultiSymbolChoiceListWorks()
        => Assert.Equal("", Run(Pkg.Default().Edit("widget.json", o => ((JsonArray)o["settings"]!).Add(new JsonObject
        {
            ["id"] = "pairs",
            ["type"] = "choice",
            ["label"] = "Paires",
            ["default"] = "BTC,ETH,SOL",
            ["options"] = new JsonArray(
                new JsonObject { ["value"] = "BTC,ETH,SOL", ["label"] = "Top 3" },
                new JsonObject { ["value"] = "BTC,ETH", ["label"] = "BTC et ETH" })
        }))));

    // ---- batterie ----

    [Theory]
    [InlineData(1, 80, 1, 80, false)]
    [InlineData(8, 40, 1, 40, true)]
    [InlineData(128, 255, 1, -1, false)]
    [InlineData(255, 255, 255, -1, false)]
    [InlineData(1, 100, 1, 100, false)]
    [InlineData(1, 255, 0, -1, false)]
    public void BatteryInterpretation(int flag, int percent, int ac, int expectedPercent, bool expectedCharging)
        => Assert.Equal((expectedPercent, expectedCharging), BatteryStatus.Interpret((byte)flag, (byte)percent, (byte)ac));
}
