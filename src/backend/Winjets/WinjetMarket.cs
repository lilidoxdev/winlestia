// language: C# 12, file: WinjetMarket.cs, target: Windows 11 / .NET 8, source de données « market » : cours crypto Binance (historique REST + flux websocket)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
namespace PanneauxGlass.Winjets;

internal sealed record MarketTimeframe(string Key, string Interval, int Limit, long BucketMs);

// Abonnement d'une instance : source `SourceId` pour ces tickers.
internal sealed record MarketSub(string InstanceId, string SourceId, IReadOnlyList<string> Symbols);

// Fonctions pures de la source market (aucun accès réseau).
internal static class WinjetMarket
{
    internal const int MaxSymbols = 4;
    internal static readonly string[] Hosts = { "api.binance.com", "stream.binance.com" };
    static readonly Regex SymbolPattern = new(@"^[A-Z0-9]{2,10}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Clé de payload -> intervalle Binance, nombre de bougies, durée d'une bougie.
    internal static readonly MarketTimeframe[] Timeframes =
    {
        new("1h", "1m", 60, 60_000L),
        new("1j", "15m", 96, 15 * 60_000L),
        new("1s", "2h", 84, 2 * 3_600_000L),
        new("1m", "1d", 30, 86_400_000L)
    };

    // `$` accepte un « \n » final : on le refuse explicitement.
    internal static bool IsValidSymbol(string? s) => s != null && !s.Contains('\n') && SymbolPattern.IsMatch(s);

    internal static string StreamUrl(IEnumerable<string> symbols)
        => "wss://stream.binance.com:9443/stream?streams=" + string.Join("/", symbols.Select(s => s.ToLowerInvariant() + "usdt@kline_1m"));

    internal static string KlinesUrl(string symbol, MarketTimeframe tf)
        => "https://api.binance.com/api/v3/klines?symbol=" + symbol + "USDT&interval=" + tf.Interval + "&limit=" + tf.Limit;

    // Met à jour la dernière bougie ou en ouvre une (la bougie reçue commence à `t`). Faux si rien n'a changé.
    // Bougies [t, o, h, l, c], triées ; on garde au plus `limit` éléments.
    internal static bool Roll(List<double[]> candles, int limit, long bucketMs, long t, double o, double h, double l, double c)
    {
        double bucket = t - (((t % bucketMs) + bucketMs) % bucketMs);
        if (candles.Count > 0)
        {
            var last = candles[^1];
            if (bucket < last[0])
                return false;
            if (bucket == last[0])
            {
                double nh = Math.Max(last[2], h), nl = Math.Min(last[3], l);
                bool changed = nh != last[2] || nl != last[3] || c != last[4];
                last[2] = nh;
                last[3] = nl;
                last[4] = c;
                return changed;
            }
        }
        candles.Add(new[] { bucket, o, h, l, c });
        while (candles.Count > limit)
            candles.RemoveAt(0);
        return true;
    }

    // Réponse /klines : [[openTime, "o", "h", "l", "c", ...], ...]. Null si la forme n'est pas celle attendue.
    internal static List<double[]>? ParseKlines(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return null;
        var list = new List<double[]>();
        foreach (var row in root.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 5 || row[0].ValueKind != JsonValueKind.Number
                || !row[0].TryGetInt64(out long t))
                continue;
            var v = new double[5];
            v[0] = t;
            bool ok = true;
            for (int i = 1; i < 5 && ok; i++)
                ok = Num(row[i], out v[i]);
            if (ok)
                list.Add(v);
        }
        return list;
    }

    static bool Num(JsonElement e, out double d)
    {
        d = 0;
        if (e.ValueKind == JsonValueKind.Number)
            return e.TryGetDouble(out d) && double.IsFinite(d);
        return e.ValueKind == JsonValueKind.String && double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)
            && double.IsFinite(d);
    }

    // Message du flux combiné : {"stream":"btcusdt@kline_1m","data":{"s":"BTCUSDT","k":{"t":..,"o":"..","h":"..","l":"..","c":".."}}}.
    internal static bool ParseKlineMessage(string json, out string symbol, out long t, out double o, out double h, out double l, out double c)
    {
        symbol = "";
        t = 0;
        o = h = l = c = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("data", out var d) ? d : doc.RootElement;
            if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("k", out var k) || k.ValueKind != JsonValueKind.Object)
                return false;
            string pair = (k.TryGetProperty("s", out var ks) && ks.ValueKind == JsonValueKind.String ? ks.GetString()
                : data.TryGetProperty("s", out var ds) && ds.ValueKind == JsonValueKind.String ? ds.GetString() : null) ?? "";
            if (!pair.EndsWith("USDT", StringComparison.Ordinal) || pair.Length <= 4)
                return false;
            symbol = pair[..^4];
            return k.TryGetProperty("t", out var kt) && kt.TryGetInt64(out t) && k.TryGetProperty("o", out var ko) && Num(ko, out o)
                && k.TryGetProperty("h", out var kh) && Num(kh, out h) && k.TryGetProperty("l", out var kl) && Num(kl, out l)
                && k.TryGetProperty("c", out var kc) && Num(kc, out c);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Variation sur 24 h en %, depuis l'ouverture de la plus ancienne bougie « 1j » (96 × 15 min = 24 h).
    internal static double Change24h(IReadOnlyList<double[]> day, double price)
    {
        if (day.Count == 0 || day[0][1] <= 0 || price <= 0)
            return 0;
        return Math.Round((price - day[0][1]) / day[0][1] * 100, 2);
    }

    internal sealed record SymbolView(string Symbol, double Price, double Change24h, IReadOnlyDictionary<string, List<double[]>> Candles);

    internal static JsonElement BuildPayload(bool live, long updatedMs, IEnumerable<SymbolView> symbols)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteBoolean("live", live);
            w.WriteNumber("updated", updatedMs);
            w.WriteStartObject("symbols");
            foreach (var s in symbols)
            {
                w.WriteStartObject(s.Symbol);
                w.WriteNumber("price", s.Price);
                w.WriteNumber("change24h", s.Change24h);
                w.WriteStartObject("candles");
                foreach (var tf in Timeframes)
                {
                    w.WriteStartArray(tf.Key);
                    if (s.Candles.TryGetValue(tf.Key, out var list))
                        foreach (var c in list)
                        {
                            w.WriteStartArray();
                            foreach (double x in c)
                                w.WriteNumberValue(x);
                            w.WriteEndArray();
                        }
                    w.WriteEndArray();
                }
                w.WriteEndObject();
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        using var doc = JsonDocument.Parse(ms.ToArray());
        return doc.RootElement.Clone();
    }
}

// Une seule connexion websocket partagée pour l'union des tickers de toutes les instances ; fermée quand plus personne n'en veut.
// Aucune boucle active : réception en attente, reconnexion avec délai exponentiel, publication au plus une fois par seconde.
internal sealed class WinjetMarketFeed : IDisposable
{
    static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1), HistoryEvery = TimeSpan.FromMinutes(5),
        IdleLimit = TimeSpan.FromSeconds(90), MinHistoryGap = TimeSpan.FromSeconds(30);

    sealed class SymbolState
    {
        internal double Price;
        internal readonly Dictionary<string, List<double[]>> Candles = new();
    }

    readonly object gate = new();
    readonly Func<string, CancellationToken, Task<JsonElement?>> getJson;
    readonly Action<string, string, JsonElement> publish;
    readonly Func<DateTimeOffset> clock;
    readonly bool connect;
    readonly Dictionary<string, SymbolState> states = new(StringComparer.Ordinal);
    List<MarketSub> subs = new();
    string signature = "";
    IReadOnlyList<string> symbols = Array.Empty<string>();
    CancellationTokenSource? run;
    Timer? historyTimer, flushTimer;
    bool live, dirty, flushPending, disposed, refreshing;
    CancellationToken refreshToken;
    long updatedMs;
    DateTimeOffset lastFlush = DateTimeOffset.MinValue, lastHistory = DateTimeOffset.MinValue;

    internal WinjetMarketFeed(Func<string, CancellationToken, Task<JsonElement?>> getJson, Action<string, string, JsonElement> publish,
        Func<DateTimeOffset>? clock = null, bool connect = true)
    {
        this.connect = connect;
        this.getJson = getJson;
        this.publish = publish;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    // Vrai tant qu'une connexion (ou sa tentative de reconnexion) est entretenue.
    internal bool Running
    {
        get
        {
            lock (gate)
                return run != null;
        }
    }

    internal void Forget(string instanceId)
    {
        List<MarketSub> rest;
        lock (gate)
            rest = subs.Where(s => s.InstanceId != instanceId).ToList();
        Update(rest);
    }

    internal void Update(IReadOnlyList<MarketSub> wanted)
    {
        lock (gate)
        {
            if (disposed)
                return;
            string sig = string.Join("|", wanted.Select(s => s.InstanceId + "/" + s.SourceId + "/" + string.Join(",", s.Symbols)));
            if (sig == signature)
                return;
            signature = sig;
            subs = wanted.ToList();
            var union = wanted.SelectMany(s => s.Symbols).Where(WinjetMarket.IsValidSymbol).Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal).Take(16).ToList();
            if (union.Count == 0)
            {
                StopLocked();
                states.Clear();
                MarkDirtyLocked();
                return;
            }
            if (run == null || !union.SequenceEqual(symbols))
            {
                StopLocked();
                symbols = union;
                foreach (string gone in states.Keys.Where(k => !union.Contains(k)).ToList())
                    states.Remove(gone);
                foreach (string s in union)
                    StateLocked(s);
                run = new CancellationTokenSource();
                var token = run.Token;
                historyTimer = new Timer(_ => _ = SafeRefreshAsync(union, token), null, HistoryEvery, HistoryEvery);
                if (connect)
                    _ = Task.Run(() => LoopAsync(union, token));
            }
            MarkDirtyLocked();
        }
    }

    // Action utilisateur « rafraîchir » : relit l'historique (au plus une fois toutes les 30 s).
    internal void RefreshNow()
    {
        IReadOnlyList<string> syms;
        CancellationToken token;
        lock (gate)
        {
            if (run == null || disposed)
                return;
            syms = symbols;
            token = run.Token;
        }
        _ = SafeRefreshAsync(syms, token);
    }

    void StopLocked()
    {
        run?.Cancel();
        run?.Dispose();
        run = null;
        historyTimer?.Dispose();
        historyTimer = null;
        live = false;
        symbols = Array.Empty<string>();
        lastHistory = DateTimeOffset.MinValue;
    }

    async Task LoopAsync(IReadOnlyList<string> syms, CancellationToken ct)
    {
        int backoff = 2;
        while (!ct.IsCancellationRequested)
        {
            bool received = false;
            try
            {
                await SafeRefreshAsync(syms, ct);
                using var ws = new ClientWebSocket();
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                using (var connect = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connect.CancelAfter(WinjetLimits.HttpTimeout);
                    await ws.ConnectAsync(new Uri(WinjetMarket.StreamUrl(syms)), connect.Token);
                }
                SetLive(true, ct);
                var buffer = new byte[8192];
                using var message = new MemoryStream();
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    message.SetLength(0);
                    WebSocketReceiveResult r;
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    idle.CancelAfter(IdleLimit);
                    do
                    {
                        r = await ws.ReceiveAsync(buffer, idle.Token);
                        if (r.MessageType == WebSocketMessageType.Close)
                            break;
                        message.Write(buffer, 0, r.Count);
                        if (message.Length > 65536)
                            throw new InvalidDataException("message trop gros");
                    } while (!r.EndOfMessage);
                    if (r.MessageType == WebSocketMessageType.Close)
                        break;
                    if (r.MessageType == WebSocketMessageType.Text)
                    {
                        received = true;
                        OnMessage(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length), ct);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Store.Log("Winjets : market : " + ex.GetType().Name + ": " + ex.Message);
            }
            SetLive(false, ct);
            if (received)
                backoff = 2;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(backoff), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            backoff = Math.Min(backoff * 2, 60);
        }
    }

    void SetLive(bool value, CancellationToken ct)
    {
        lock (gate)
        {
            if (ct.IsCancellationRequested || live == value)
                return;
            live = value;
            MarkDirtyLocked();
        }
    }

    internal void OnMessage(string json, CancellationToken ct = default)
    {
        if (!WinjetMarket.ParseKlineMessage(json, out string sym, out long t, out double o, out double h, out double l, out double c))
            return;
        lock (gate)
        {
            if (ct.IsCancellationRequested || !states.TryGetValue(sym, out var st))
                return;
            bool changed = st.Price != c;
            st.Price = c;
            foreach (var tf in WinjetMarket.Timeframes)
            {
                if (!st.Candles.TryGetValue(tf.Key, out var list))
                    st.Candles[tf.Key] = list = new List<double[]>();
                changed |= WinjetMarket.Roll(list, tf.Limit, tf.BucketMs, t, o, h, l, c);
            }
            if (changed)
            {
                updatedMs = clock().ToUnixTimeMilliseconds();
                MarkDirtyLocked();
            }
        }
    }

    SymbolState StateLocked(string sym)
    {
        if (!states.TryGetValue(sym, out var st))
            states[sym] = st = new SymbolState();
        return st;
    }

    // Historique REST de tous les tickers (jamais deux à la fois, au plus une fois toutes les 30 s).
    async Task SafeRefreshAsync(IReadOnlyList<string> syms, CancellationToken ct)
    {
        lock (gate)
        {
            if ((refreshing && refreshToken == ct) || disposed || ct.IsCancellationRequested || (clock() - lastHistory) < MinHistoryGap)
                return;
            refreshing = true;
            refreshToken = ct;
            lastHistory = clock();
        }
        try
        {
            foreach (string sym in syms)
                foreach (var tf in WinjetMarket.Timeframes)
                {
                    List<double[]>? list = null;
                    try
                    {
                        var json = await getJson(WinjetMarket.KlinesUrl(sym, tf), ct);
                        list = json is { } j ? WinjetMarket.ParseKlines(j) : null;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Store.Log("Winjets : market : historique " + sym + " : " + ex.GetType().Name + ": " + ex.Message);
                    }
                    if (list == null)
                        continue;
                    lock (gate)
                    {
                        if (ct.IsCancellationRequested)
                            return;
                        var st = StateLocked(sym);
                        st.Candles[tf.Key] = list;
                        if (tf.Key == WinjetMarket.Timeframes[0].Key && list.Count > 0)
                            st.Price = list[^1][4];
                    }
                }
            lock (gate)
            {
                if (ct.IsCancellationRequested)
                    return;
                updatedMs = clock().ToUnixTimeMilliseconds();
                MarkDirtyLocked();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Store.Log("Winjets : market : historique : " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            lock (gate)
                if (refreshToken == ct)
                    refreshing = false;
        }
    }

    // Au plus une publication par seconde, seulement si quelque chose a changé.
    void MarkDirtyLocked()
    {
        dirty = true;
        if (flushPending || disposed)
            return;
        flushPending = true;
        var wait = FlushInterval - (clock() - lastFlush);
        if (wait < TimeSpan.FromMilliseconds(50))
            wait = TimeSpan.FromMilliseconds(50);
        if (flushTimer == null)
            flushTimer = new Timer(_ => Flush(), null, wait, Timeout.InfiniteTimeSpan);
        else
            flushTimer.Change(wait, Timeout.InfiniteTimeSpan);
    }

    void Flush()
    {
        var outgoing = new List<(MarketSub sub, JsonElement payload)>();
        lock (gate)
        {
            flushPending = false;
            if (!dirty || disposed)
                return;
            dirty = false;
            lastFlush = clock();
            foreach (var sub in subs)
            {
                var views = new List<WinjetMarket.SymbolView>();
                foreach (string sym in sub.Symbols)
                    if (states.TryGetValue(sym, out var st) && st.Price > 0)
                        views.Add(new WinjetMarket.SymbolView(sym, st.Price,
                            WinjetMarket.Change24h(st.Candles.TryGetValue("1j", out var day) ? day : new List<double[]>(), st.Price),
                            st.Candles));
                outgoing.Add((sub, WinjetMarket.BuildPayload(live, updatedMs, views)));
            }
        }
        foreach (var (sub, payload) in outgoing)
            publish(sub.InstanceId, sub.SourceId, payload);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            StopLocked();
            flushTimer?.Dispose();
            flushTimer = null;
        }
    }
}
