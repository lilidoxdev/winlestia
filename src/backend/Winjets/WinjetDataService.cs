// language: C# 12, file: WinjetDataService.cs, target: Windows 11 / .NET 8, seul accès réseau des Winjets (HTTPS, hôtes déclarés, cache
// disque)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Winlestia.Shared;
namespace PanneauxGlass.Winjets;

// Dernière valeur connue d'une source. `Value` est null tant que rien n'a pu être lu ; `Stale` = servi depuis le cache après échec.
internal sealed record WinjetDataValue(JsonElement? Value, string Updated, string? Error, bool Stale);

// Une instance visible dont les sources doivent être tenues à jour.
internal sealed record WinjetPollTarget(string InstanceId, WinjetPackage Package, IReadOnlyDictionary<string, object?> Settings,
    IReadOnlyDictionary<string, object?> State);

internal sealed class WinjetDataService : IDisposable
{
    readonly object gate = new();
    readonly string cacheDir;
    readonly HttpClient http;
    readonly WinjetMarketFeed market;
    readonly Func<DateTimeOffset> clock;
    readonly Dictionary<(string, string), WinjetDataValue> values = new();
    readonly Dictionary<(string, string), DateTimeOffset> lastAttempt = new();
    readonly Dictionary<(string, string), Task<WinjetDataValue?>> inFlight = new();
    // Dernière adresse ICS résolue par (instance, source) : un changement de réglage force une relecture immédiate.
    readonly Dictionary<(string, string), string> calendarUrls = new();
    // Une source d'annulation par instance : Forget l'annule, les requêtes en vol n'écrivent alors plus rien.
    readonly Dictionary<string, CancellationTokenSource> cancels = new();
    List<WinjetPollTarget> targets = new();
    Timer? timer;
    bool disposed;

    // Délai maximal d'une requête ; réglable pour les tests seulement.
    internal TimeSpan Timeout { get; set; } = WinjetLimits.HttpTimeout;

    // Fuseau local pour la source calendar ; réglable pour les tests seulement.
    internal TimeZoneInfo LocalZone { get; set; } = TimeZoneInfo.Local;

    // `handler` et `clock` sont injectables (tests). Sans handler : pas de redirection (elle contournerait la liste d'hôtes),
    // pas de cookies.
    internal WinjetDataService(string cacheDir, HttpMessageHandler? handler = null, Func<DateTimeOffset>? clock = null)
    {
        this.cacheDir = cacheDir;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = true,
            ConnectCallback = ConnectPublicAsync,
            ConnectTimeout = WinjetLimits.HttpTimeout,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        })
        { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Winlestia-Winjets/1");
        market = new WinjetMarketFeed((url, ct) => GetJsonAsync(url, ct), PublishMarket, this.clock);
    }

    internal event Action? Changed;

    // Adresse joignable depuis un widget : ni boucle locale, ni lien local, ni réseau privé (RFC 1918, ULA, CGNAT), ni non spécifiée.
    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None))
            return false;
        var b = address.GetAddressBytes();
        if (b.Length == 4)
            return !(b[0] == 0 || b[0] == 10 || b[0] >= 224 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127));
        return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC);
    }

    // Faux quand la requête passe par un proxy (non contourné) : l'adresse vue par la connexion est alors celle du proxy.
    internal static bool ShouldFilterAddresses(Uri target, IWebProxy? proxy)
    {
        if (proxy == null || proxy.IsBypassed(target))
            return true;
        var via = proxy.GetProxy(target);
        return via == null || (via.Host == target.Host && via.Port == target.Port);
    }

    // Résout le nom puis refuse toute adresse non publique : la liste d'hôtes ne suffit pas contre un DNS qui pointe vers le réseau local.
    static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        // Via un proxy système, le point de connexion est le proxy (configuration de l'utilisateur) : pas de filtre d'adresse,
        // la liste d'hôtes et HTTPS de bout en bout restent appliqués.
        var target = context.InitialRequestMessage?.RequestUri;
        bool filter = target == null || ShouldFilterAddresses(target, HttpClient.DefaultProxy);
        var all = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        if (!filter)
        {
            var any = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await any.ConnectAsync(all, context.DnsEndPoint.Port, ct);
                return new NetworkStream(any, true);
            }
            catch
            {
                any.Dispose();
                throw;
            }
        }
        var allowed = all.Where(IsPublicAddress).ToArray();
        if (allowed.Length == 0)
            throw new HttpRequestException("Adresse non publique refusée.");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    // Vrai si une minuterie de scrutation est armée (jamais sans source http active).
    internal bool HasTimer
    {
        get
        {
            lock (gate)
                return timer != null;
        }
    }

    internal WinjetDataValue? Get(string instanceId, string sourceId)
    {
        lock (gate)
            return values.TryGetValue((instanceId, sourceId), out var v) ? v : null;
    }

    // { instanceId -> { sourceId -> valeur } } pour l'état publié.
    internal Dictionary<string, Dictionary<string, WinjetDataValue>> Snapshot()
    {
        lock (gate)
            return values.GroupBy(p => p.Key.Item1).ToDictionary(g => g.Key, g => g.ToDictionary(p => p.Key.Item2, p => p.Value));
    }

    internal void Forget(string instanceId)
    {
        market.Forget(instanceId);
        lock (gate)
        {
            foreach (var k in calendarUrls.Keys.Where(k => k.Item1 == instanceId).ToList())
                calendarUrls.Remove(k);
            if (cancels.Remove(instanceId, out var cts))
                cts.Cancel();
            foreach (var k in inFlight.Keys.Where(k => k.Item1 == instanceId).ToList())
                inFlight.Remove(k);
            foreach (var key in values.Keys.Where(k => k.Item1 == instanceId).ToList())
            {
                values.Remove(key);
                lastAttempt.Remove(key);
            }
        }
    }

    // Instances actives et visibles. Les sources « file » sont lues tout de suite ; les sources « http » sont lues au prochain
    // passage de la minuterie (échéance immédiate si jamais lues). Sans source http active, aucune minuterie.
    internal void SetTargets(IReadOnlyList<WinjetPollTarget> visible)
    {
        lock (gate)
            targets = visible.ToList();
        foreach (var t in visible)
            foreach (var s in t.Package.Definition.Sources.Where(s => s.IsFile))
                ReadFile(t, s);
        // Une seule connexion en direct pour l'union des tickers des sources market actives ; aucune sans source market.
        market.Update(visible.SelectMany(t => t.Package.Definition.Sources.Where(s => s.IsMarket && IsEnabled(s, t))
            .Select(s => new MarketSub(t.InstanceId, s.Id, s.Symbols ?? Array.Empty<string>()))).ToList());
        Reschedule();
    }

    // Condition de la source (réglages/état de l'instance) ; seuls « settings.* » et « state.* » sont résolus.
    internal static bool IsEnabled(WinjetSource source, WinjetPollTarget t)
        => source.EnabledWhen.Length == 0 || WinjetCondition.Evaluate(source.EnabledWhen, path =>
        {
            int dot = path.IndexOf('.');
            if (dot < 0)
                return null;
            var scope = path[..dot] switch { "settings" => t.Settings, "state" => t.State, _ => null };
            return scope != null && scope.TryGetValue(path[(dot + 1)..], out var v) ? v : null;
        });

    // Rafraîchit toutes les sources actives de l'instance (action utilisateur : intervalle minimal 60 s).
    internal async Task RefreshAsync(WinjetPollTarget t, bool force)
    {
        foreach (var s in t.Package.Definition.Sources)
        {
            if (!IsEnabled(s, t))
                continue;
            if (s.IsPolled)
                await FetchAsync(t, s, force);
            else if (s.IsMarket)
                market.RefreshNow();
            else
                ReadFile(t, s);
        }
    }

    // Publication d'une charge « market » : même chemin que les autres sources (valeur + Changed -> état publié au frontend).
    void PublishMarket(string instanceId, string sourceId, JsonElement payload)
    {
        lock (gate)
            if (disposed || !targets.Any(t => t.InstanceId == instanceId))
                return;
        Set((instanceId, sourceId), new WinjetDataValue(payload, Now(), null, false));
    }

    // GET JSON borné (délai et taille comme les sources http) ; null au moindre échec. Hôtes fixés par le code appelant.
    async Task<JsonElement?> GetJsonAsync(string url, CancellationToken ct, string? userAgent = null)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");
            if (userAgent != null)
            {
                request.Headers.UserAgent.Clear();
                request.Headers.UserAgent.ParseAdd(userAgent);
            }
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > WinjetLimits.MaxResponseBytes)
                return null;
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cts.Token)) > 0)
            {
                if (buffer.Length + read > WinjetLimits.MaxResponseBytes)
                    return null;
                buffer.Write(chunk, 0, read);
            }
            using var doc = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = WinjetLimits.MaxJsonDepth });
            return doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or JsonException or IOException
            or InvalidOperationException)
        {
            if (ct.IsCancellationRequested)
                throw;
            return null;
        }
    }

    // Échéance de scrutation : on interroge toutes les sources http échues, puis on réarme.
    internal async Task PollDueAsync()
    {
        List<WinjetPollTarget> snapshot;
        lock (gate)
            snapshot = targets.ToList();
        try
        {
            var now = clock();
            foreach (var t in snapshot)
                foreach (var s in t.Package.Definition.Sources.Where(s => s.IsPolled && IsEnabled(s, t)))
                    if (DueAt(t, s) <= now)
                        await FetchAsync(t, s, false);
        }
        finally
        {
            Reschedule();
        }
    }

    DateTimeOffset DueAt(WinjetPollTarget t, WinjetSource s)
    {
        lock (gate)
            return lastAttempt.TryGetValue((t.InstanceId, s.Id), out var last) ? last.AddSeconds(s.Refresh) : DateTimeOffset.MinValue;
    }

    void Reschedule()
    {
        lock (gate)
        {
            if (disposed)
                return;
            DateTimeOffset? next = null;
            foreach (var t in targets)
                foreach (var s in t.Package.Definition.Sources.Where(s => s.IsPolled && IsEnabled(s, t)))
                {
                    var due = lastAttempt.TryGetValue((t.InstanceId, s.Id), out var last) ? last.AddSeconds(s.Refresh)
                        : DateTimeOffset.MinValue;
                    if (next == null || due < next)
                        next = due;
                }
            if (next == null)
            {
                timer?.Dispose();
                timer = null;
                return;
            }
            var wait = next.Value - clock();
            if (wait < TimeSpan.FromMilliseconds(250))
                wait = TimeSpan.FromMilliseconds(250);
            if (wait > TimeSpan.FromHours(25))
                wait = TimeSpan.FromHours(25);
            // Une seule échéance à la fois (one-shot) : réarmée après chaque passage.
            if (timer == null)
                timer = new Timer(_ => _ = SafePollAsync(), null, wait, System.Threading.Timeout.InfiniteTimeSpan);
            else
                timer.Change(wait, System.Threading.Timeout.InfiniteTimeSpan);
        }
    }

    async Task SafePollAsync()
    {
        try
        {
            await PollDueAsync();
        }
        catch (Exception ex)
        {
            Store.Log("Winjets : scrutation : " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    void Set((string, string) key, WinjetDataValue value)
    {
        lock (gate)
            values[key] = value;
        Changed?.Invoke();
    }

    string Now() => clock().UtcDateTime.ToString("o");

    void ReadFile(WinjetPollTarget t, WinjetSource s)
    {
        var key = (t.InstanceId, s.Id);
        if (!IsEnabled(s, t))
            return;
        lock (gate)
            if (values.ContainsKey(key))
                return;
        try
        {
            string root = Path.GetFullPath(t.Package.Dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(t.Package.Dir, s.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || new FileInfo(full).Length > WinjetLimits.MaxJsonBytes)
                throw new InvalidDataException("fichier hors du paquet ou trop gros");
            using var doc = JsonDocument.Parse(File.ReadAllBytes(full), new JsonDocumentOptions { MaxDepth = WinjetLimits.MaxJsonDepth });
            Set(key, new WinjetDataValue(doc.RootElement.Clone(), Now(), null, false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            Set(key, new WinjetDataValue(null, Now(), "Lecture de " + s.Path + " impossible.", false));
        }
    }

    // Une seule requête en vol par (instance, source) : un second appel reçoit la même tâche.
    internal Task<WinjetDataValue?> FetchAsync(WinjetPollTarget t, WinjetSource s, bool force)
    {
        var key = (t.InstanceId, s.Id);
        lock (gate)
        {
            if (disposed)
                return Task.FromResult<WinjetDataValue?>(null);
            if (inFlight.TryGetValue(key, out var running))
                return running;
            int floor = s.IsCalendar ? WinjetLimits.MinCalendarRefreshSeconds : WinjetLimits.MinRefreshSeconds;
            int interval = force ? floor : Math.Max(s.Refresh, floor);
            bool urlChanged = s.IsCalendar && (!calendarUrls.TryGetValue(key, out var seen) || seen != ResolveCalendarUrl(t, s));
            if (!urlChanged && lastAttempt.TryGetValue(key, out var last) && (clock() - last).TotalSeconds < interval)
                return Task.FromResult(values.TryGetValue(key, out var cur) ? cur : null);
            lastAttempt[key] = clock();
            if (!cancels.TryGetValue(t.InstanceId, out var cts))
                cancels[t.InstanceId] = cts = new CancellationTokenSource();
            var task = Run(t, s, key, cts);
            inFlight[key] = task;
            return task;
        }
    }

    async Task<WinjetDataValue?> Run(WinjetPollTarget t, WinjetSource s, (string, string) key, CancellationTokenSource owner)
    {
        await Task.Yield();
        WinjetDataValue result;
        try
        {
            if (s.IsYahoo)
                result = await FetchYahoo(s, owner.Token);
            else if (s.IsCalendar)
                result = await FetchCalendar(t, s, key, owner.Token);
            else
            {
                string? bad = WinjetDefinition.HttpsProblem(s.Url, t.Package.Manifest);
                result = bad != null ? Failure(s, "Adresse refusée : " + bad) : await Download(s, owner.Token);
            }
        }
        catch (Exception ex)
        {
            Store.Log("Winjets : " + s.Id + " : " + ex.GetType().Name + ": " + ex.Message);
            result = Failure(s, "Source indisponible.");
        }
        lock (gate)
        {
            // Instance oubliée pendant la requête : on ne réécrit rien.
            if (owner.IsCancellationRequested)
                return null;
            inFlight.Remove(key);
        }
        Set(key, result);
        return result;
    }

    // Cours Yahoo Finance : une requête par symbole, un titre en échec devient un élément status « error » (jamais d'exception).
    async Task<WinjetDataValue> FetchYahoo(WinjetSource s, CancellationToken forget)
    {
        var quotes = new List<WinjetMarket.YahooQuote>();
        foreach (string sym in s.Symbols ?? Array.Empty<string>())
        {
            JsonElement? json = null;
            try
            {
                json = await GetJsonAsync(WinjetMarket.YahooUrl(sym), forget, "Mozilla/5.0 (compatible; Winlestia-Winjets/1)");
            }
            catch (OperationCanceledException) when (!forget.IsCancellationRequested) { }
            quotes.Add(json is { } j ? WinjetMarket.ParseYahoo(sym, j) : WinjetMarket.YahooError(sym));
        }
        var payload = WinjetMarket.BuildYahooPayload(clock().ToUnixTimeMilliseconds(), quotes);
        return new WinjetDataValue(payload, Now(), quotes.Count > 0 && quotes.All(q => q.Status != "ok") ? "Source indisponible."
            : null, false);
    }

    // Adresse ICS résolue : réglage {{settings.x}} de l'instance ou adresse littérale. Vide si absente.
    static string ResolveCalendarUrl(WinjetPollTarget t, WinjetSource s)
    {
        string? key = WinjetCalendar.SettingKey(s.Url);
        object? raw = key == null ? s.Url : t.Settings.TryGetValue(key, out var v) ? v : null;
        return (raw as string ?? "").Trim();
    }

    // Agenda ICS : https seulement (webcal:// accepté, réécrit en https://), 2 Mo au plus, délai borné. L'adresse d'un réglage n'est
    // pas limitée à permissions.network ; les adresses non publiques restent refusées à la connexion.
    async Task<WinjetDataValue> FetchCalendar(WinjetPollTarget t, WinjetSource s, (string, string) key, CancellationToken forget)
    {
        var tz = LocalZone;
        var nowLocal = TimeZoneInfo.ConvertTime(clock(), tz).DateTime;
        string raw = ResolveCalendarUrl(t, s);
        lock (gate)
            calendarUrls[key] = raw;
        JsonElement Payload(string status, IReadOnlyList<CalendarEvent> ev) => WinjetCalendar.BuildPayload(status, ev, nowLocal.Date, tz);
        WinjetDataValue Fail(string why) => new(Payload("error", Array.Empty<CalendarEvent>()), Now(), why, false);
        if (raw.Length == 0)
            return new WinjetDataValue(Payload("unconfigured", Array.Empty<CalendarEvent>()), Now(), null, false);
        string? url = WinjetCalendar.NormalizeUrl(raw);
        if (url == null)
            return Fail("Adresse refusée : seul https:// (ou webcal://) est autorisé.");
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(forget);
            cts.CancelAfter(Timeout);
            // Au plus 3 redirections ; chaque cible est revalidée (https, port 443, sans identifiants) et l'IP publique est
            // contrôlée par le filtre de connexion (ConnectPublicAsync) à chaque saut.
            HttpResponseMessage response;
            for (int hop = 0; ; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.ParseAdd("text/calendar, text/plain");
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                int code = (int)response.StatusCode;
                if (code is not (301 or 302 or 303 or 307 or 308))
                    break;
                var location = response.Headers.Location;
                response.Dispose();
                if (location == null || hop >= 3)
                    return Fail("Trop de redirections.");
                string? next = WinjetCalendar.NormalizeUrl((location.IsAbsoluteUri ? location : new Uri(new Uri(url), location))
                    .AbsoluteUri);
                if (next == null)
                    return Fail("Redirection refusée : seul https:// (port 443) est autorisé.");
                url = next;
            }
            using var _ = response;
            if (!response.IsSuccessStatusCode)
                return Fail("Le serveur a répondu " + (int)response.StatusCode + ".");
            if (response.Content.Headers.ContentLength > WinjetLimits.MaxIcsBytes)
                return Fail("Réponse trop volumineuse.");
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cts.Token)) > 0)
            {
                if (buffer.Length + read > WinjetLimits.MaxIcsBytes)
                    return Fail("Réponse trop volumineuse.");
                buffer.Write(chunk, 0, read);
            }
            var events = WinjetCalendar.Parse(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), nowLocal, tz);
            return new WinjetDataValue(Payload("ok", events), Now(), null, false);
        }
        catch (OperationCanceledException) when (!forget.IsCancellationRequested)
        {
            return Fail("Délai dépassé.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            return Fail("Réseau indisponible.");
        }
    }

    async Task<WinjetDataValue> Download(WinjetSource s, CancellationToken forget)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(forget);
            cts.CancelAfter(Timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, s.Url);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode)
                return Failure(s, "Le serveur a répondu " + (int)response.StatusCode + ".");
            if (response.Content.Headers.ContentLength > WinjetLimits.MaxResponseBytes)
                return Failure(s, "Réponse trop volumineuse.");
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cts.Token)) > 0)
            {
                if (buffer.Length + read > WinjetLimits.MaxResponseBytes)
                    return Failure(s, "Réponse trop volumineuse.");
                buffer.Write(chunk, 0, read);
            }
            using var doc = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = WinjetLimits.MaxJsonDepth });
            var value = doc.RootElement.Clone();
            string now = Now();
            WriteCache(s.Url, value, now);
            return new WinjetDataValue(value, now, null, false);
        }
        catch (OperationCanceledException)
        {
            return Failure(s, "Délai dépassé.");
        }
        catch (HttpRequestException)
        {
            return Failure(s, "Réseau indisponible.");
        }
        catch (JsonException)
        {
            return Failure(s, "Réponse illisible (JSON attendu).");
        }
    }

    // Échec : on sert le cache s'il existe (périmé), sinon une valeur vide avec l'erreur.
    WinjetDataValue Failure(WinjetSource s, string error)
    {
        var cached = ReadCache(s.Url);
        return cached != null ? new WinjetDataValue(cached.Value.value, cached.Value.fetched, error, true)
            : new WinjetDataValue(null, Now(), error, false);
    }

    string CachePath(string url) => Path.Combine(cacheDir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))
        .ToLowerInvariant() + ".json");

    void WriteCache(string url, JsonElement value, string fetched)
    {
        try
        {
            Directory.CreateDirectory(cacheDir);
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("url", url);
                w.WriteString("fetched", fetched);
                w.WritePropertyName("body");
                value.WriteTo(w);
                w.WriteEndObject();
            }
            AtomicFile.WriteAllBytes(CachePath(url), ms.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Store.Log("Winjets : cache : " + ex.Message); }
    }

    (JsonElement value, string fetched)? ReadCache(string url)
    {
        try
        {
            string file = CachePath(url);
            if (!File.Exists(file) || new FileInfo(file).Length > WinjetLimits.MaxResponseBytes + 4096)
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions {
                MaxDepth = WinjetLimits.MaxJsonDepth + 2 });
            var root = doc.RootElement;
            if (root.GetProperty("url").GetString() != url)
                return null;
            return (root.GetProperty("body").Clone(), root.GetProperty("fetched").GetString() ?? "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException
            or InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            foreach (var c in cancels.Values)
                c.Cancel();
            cancels.Clear();
            timer?.Dispose();
            timer = null;
        }
        market.Dispose();
        http.Dispose();
    }
}
