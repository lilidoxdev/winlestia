// C# 12, fichier LyricsService.cs, cible Windows 11 / .NET 8, paroles : fournisseur LRCLIB et suivi des recherches en cours
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PanneauxGlass;

// Fournisseur LRCLIB. Jamais d'exception vers l'appelant : null = échec transitoire (réseau, délai), à ne pas mémoriser ;
// LyricsResult.None = LRCLIB n'a rien.
internal static class LrclibProvider
{
    // Un seul client : l'adresse du dépôt identifie l'application auprès du service.
    static readonly HttpClient client = Create();

    // Relu juste avant chaque appel réseau : une recherche en cours quand le réglage passe à faux n'envoie plus rien.
    internal static Func<bool> Enabled { get; set; } = () => Store.State.Lyrics;

    static HttpClient Create()
    {
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(8),
            MaxResponseContentBufferSize = 2 * 1024 * 1024
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Winlestia (https://github.com/lilidoxdev/winlestia)");
        return http;
    }

    internal static async Task<LyricsResult?> Fetch(string title, string artist, string album, double duration, CancellationToken cancel)
    {
        try
        {
            bool known = duration > 0 && double.IsFinite(duration) && duration < 3600 * 4;
            string get = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(title) + "&artist_name=" + Uri.EscapeDataString(artist)
                         + (string.IsNullOrWhiteSpace(album) ? "" : "&album_name=" + Uri.EscapeDataString(album))
                         + (known ? "&duration=" + Math.Round(duration).ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
            var (status, body) = await Read(get, cancel);
            if (status == 200 && body != null)
            {
                var direct = Candidate(body);
                if (direct != null && (!known || direct.Duration <= 0 || Math.Abs(direct.Duration - duration) <= 6))
                {
                    var result = LyricsText.FromCandidate(direct);
                    if (result.State != "none")
                        return result;
                }
            }
            else if (status != 404 && status != 200)
                return null;

            string cleanTitle = LyricsText.CleanTitle(title, true), cleanArtist = LyricsText.CleanArtist(artist);
            string search = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(cleanTitle) + "&artist_name="
                            + Uri.EscapeDataString(cleanArtist);
            (status, body) = await Read(search, cancel);
            if (status != 200)
                return status == 404 ? LyricsResult.None : null;
            if (body == null)
                return null;
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return LyricsResult.None;
            var candidates = doc.RootElement.EnumerateArray().Select(e => Candidate(e)).Where(c => c != null).Select(c => c!).ToList();
            var chosen = LyricsText.Select(candidates, duration);
            return chosen == null ? LyricsResult.None : LyricsText.FromCandidate(chosen);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException
                                       or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    static async Task<(int status, string? body)> Read(string url, CancellationToken cancel)
    {
        if (!Enabled())
            throw new OperationCanceledException();
        using var response = await client.GetAsync(url, cancel);
        int status = (int)response.StatusCode;
        return (status, status == 200 ? await response.Content.ReadAsStringAsync(cancel) : null);
    }

    static LyricCandidate? Candidate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Candidate(doc.RootElement);
    }

    static LyricCandidate? Candidate(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
            return null;
        string? Text(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        double d = e.TryGetProperty("duration", out var dv) && dv.ValueKind == JsonValueKind.Number && dv.TryGetDouble(out double n) ? n : 0;
        bool instrumental = e.TryGetProperty("instrumental", out var iv) && iv.ValueKind == JsonValueKind.True;
        return new LyricCandidate(Text("syncedLyrics"), Text("plainLyrics"), instrumental, d);
    }
}

// Suivi des recherches : le pont ne fait qu'interroger (cache mémoire, cache disque) et déclencher ; le réseau tourne hors du
// canal et prévient par le callback à la fin. Dernier arrivé gagne : changer de morceau annule la recherche précédente.
internal sealed class LyricsService
{
    internal static readonly TimeSpan TransientLife = TimeSpan.FromSeconds(60);
    internal const int MemoryCap = 64;
    // Deux requêtes au plus (get puis search) : 8 s chacune via le délai du client, 16 s en tout.
    static readonly TimeSpan Budget = TimeSpan.FromSeconds(16);

    internal static LyricsService Shared { get; } = new(new LyricsCache(Path.Combine(Store.Root, "lyrics")),
                                                       LrclibProvider.Fetch);

    readonly LyricsCache cache;
    readonly Func<string, string, string, double, CancellationToken, Task<LyricsResult?>> fetch;
    readonly Func<DateTime> now;
    readonly object gate = new();
    readonly Dictionary<string, (LyricsResult Result, DateTime? Until)> memory = new();
    readonly Queue<string> order = new();
    readonly HashSet<string> running = new();
    readonly SemaphoreSlim slots = new(2);
    CancellationTokenSource? latest;

    internal LyricsService(LyricsCache cache, Func<string, string, string, double, CancellationToken, Task<LyricsResult?>> fetch,
                           Func<DateTime>? now = null)
    {
        this.cache = cache;
        this.fetch = fetch;
        this.now = now ?? (() => DateTime.UtcNow);
    }

    // Résultat connu, ou Loading (recherche lancée si besoin). `done` est appelé une fois la recherche terminée.
    internal LyricsResult Request(string title, string artist, string album, double duration, Action done)
    {
        string key = LyricsText.TrackKey(artist, title, duration);
        lock (gate)
        {
            if (memory.TryGetValue(key, out var known))
            {
                if (known.Until == null || known.Until > now())
                    return known.Result;
                memory.Remove(key);
            }
        }
        var stored = cache.Get(key);
        if (stored != null)
            return Remember(key, stored, null);
        CancellationTokenSource mine;
        lock (gate)
        {
            if (!running.Add(key))
                return LyricsResult.Loading;
            latest?.Cancel();
            mine = latest = new CancellationTokenSource();
        }
        _ = Task.Run(() => Run(key, title, artist, album, duration, mine, done));
        return LyricsResult.Loading;
    }

    async Task Run(string key, string title, string artist, string album, double duration, CancellationTokenSource mine, Action done)
    {
        bool acquired = false;
        try
        {
            await slots.WaitAsync(mine.Token);
            acquired = true;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(mine.Token);
            timeout.CancelAfter(Budget);
            var result = await fetch(title, artist, album, duration, timeout.Token);
            if (mine.IsCancellationRequested)
                return; // remplacée par un morceau plus récent : rien à mémoriser
            if (result != null)
            {
                cache.Put(key, result);
                Remember(key, result, null);
            }
            else
                Remember(key, LyricsResult.None, now() + TransientLife); // échec transitoire : ni disque, nouvel essai après 60 s
            done();
        }
        catch (OperationCanceledException)
        {
            if (!mine.IsCancellationRequested)
            {
                Remember(key, LyricsResult.None, now() + TransientLife);
                done();
            }
        }
        catch (Exception ex)
        {
            Store.Log("Paroles: " + ex.Message);
            Remember(key, LyricsResult.None, now() + TransientLife);
            done();
        }
        finally
        {
            if (acquired)
                slots.Release();
            lock (gate)
            {
                running.Remove(key);
                if (ReferenceEquals(latest, mine))
                    latest = null;
            }
            mine.Dispose();
        }
    }

    LyricsResult Remember(string key, LyricsResult result, DateTime? until)
    {
        lock (gate)
        {
            if (!memory.ContainsKey(key))
            {
                // Une seule entrée, la plus ancienne, part quand le plafond est atteint.
                while (memory.Count >= MemoryCap && order.Count > 0)
                    memory.Remove(order.Dequeue());
                order.Enqueue(key);
            }
            memory[key] = (result, until);
        }
        return result;
    }

    internal static object Payload(LyricsResult r, string key) => new
    {
        state = r.State,
        lines = r.Lines.Select(l => new { time = l.Time, text = l.Text }).ToArray(),
        plain = r.Plain,
        key
    };
}
