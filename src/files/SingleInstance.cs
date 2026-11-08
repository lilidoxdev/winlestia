// Côté serveur du tube d'instance unique (protocole dans backend/FilesPipe.cs). Une seule boucle d'acceptation en attente, sans minuteur ni
// scrutation :
// un serveur au repos est un seul ConnectNamedPipe en attente (overlapped). Un message corrompu n'atteint jamais l'interface et ne met
// jamais fin au processus.
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Winlestia.Shared;

namespace WinlestiaFiles;

internal sealed class SingleInstance : IDisposable
{
    // Reprise croissante (100 ms par échec) pour ne pas saturer le canal quand le résident est occupé.
    const int RetryStepMs = 100;

    internal sealed record Request(string[] Args, string Cwd, string? Cmd);

    readonly Action<Request> onRequest;
    readonly CancellationTokenSource cts = new();
    NamedPipeServerStream? pending;

    SingleInstance(Action<Request> onRequest)
    {
        this.onRequest = onRequest;
    }

    // Plusieurs instances permises, mais au plus MaxHandlers servies à la fois : un écouteur peut ainsi toujours être créé ("all pipe
    // instances are busy" ne peut pas se produire).
    const int MaxInstances = 32, MaxHandlers = 16;
    readonly SemaphoreSlim slots = new(MaxHandlers, MaxHandlers);

    static NamedPipeServerStream Create(bool first) =>
        new(FilesPipe.Name, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None));

    // "All pipe instances are busy" est transitoire (un client ou une instance mourante tient encore un emplacement) : courte temporisation
    // croissante, 50 -> 800 ms.
    static NamedPipeServerStream CreateRetry(CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return Create(false);
            }
            catch (IOException) when (attempt < 4 && !ct.IsCancellationRequested) { Thread.Sleep(50 << attempt); }
        }
    }

    // Null quand une autre instance possède déjà le tube (ou qu'il ne peut pas être créé) : l'appelant tourne alors simplement en autonome.
    internal static SingleInstance? TryStart(Action<Request> onRequest)
    {
        try
        {
            var server = new SingleInstance(onRequest) { pending = Create(true) };
            _ = server.Loop();
            return server;
        }
        catch { return null; }
    }

    async Task Loop()
    {
        int failures = 0;
        bool logged = false;
        while (!cts.IsCancellationRequested)
        {
            var stream = pending!;
            bool handed = false;
            try
            {
                // saturated: the listener stays up, clients just wait their own short budget
                await slots.WaitAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    await stream.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);
                    pending = CreateRetry(cts.Token);                       // ready for the next client before this one is served
                    failures = 0;
                    handed = true;
                    _ = Handle(stream);
                }
                finally { if (!handed) slots.Release(); }
            }
            catch (OperationCanceledException)
            {
                stream.Dispose();
                return;
            }
            catch (Exception ex)
            {
                stream.Dispose();
                if (!logged)
                {
                    logged = true;
                    App.CrashLog(ex);
                }           // once, never one line per retry
                if (++failures >= 8)
                    return;
                try
                {
                    await Task.Delay(RetryStepMs * failures, cts.Token).ConfigureAwait(false);
                    pending = CreateRetry(cts.Token);
                }
                catch (OperationCanceledException) { return; }
                catch { continue; }
            }
        }
    }

    async Task Handle(NamedPipeServerStream stream)
    {
        using (stream)
        {
            try
            {
                using var limit = new CancellationTokenSource(2000);
                await stream.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), limit.Token).ConfigureAwait(false);
                var buffer = new byte[65536];
                int n = 0;
                while (n < buffer.Length)
                {
                    int r = await stream.ReadAsync(buffer.AsMemory(n), limit.Token).ConfigureAwait(false);
                    if (r == 0)
                        break;
                    n += r;
                    if (Array.IndexOf(buffer, (byte)'\n', n - r, r) >= 0)
                        break;
                }
                if (n == 0)
                    return;                               // connected and left (probe): nothing to parse, nothing to log
                var request = Parse(Encoding.UTF8.GetString(buffer, 0, n));
                if (request == null)
                    return;
                onRequest(request);
                await stream.WriteAsync(new[] { (byte)'1' }, limit.Token).ConfigureAwait(false);
                stream.WaitForPipeDrain();
            }
            catch (IOException)
            {                           // client gave up (its own 150 ms budget): nothing to report
            }
            catch (OperationCanceledException)
            {
                // Client parti ou requête annulée : rien à reporter.
            }
            catch (System.Text.Json.JsonException)
            {        // garbage from a foreign client
            }
            catch (Exception ex) { App.CrashLog(ex); }
            finally { try { slots.Release(); } catch (ObjectDisposedException) { /* Sémaphore déjà libéré à l'arrêt. */ } }
        }
    }

    static Request? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            return ParseCore(text.Trim());
        }
        catch (JsonException) { return null; }
    }

    static Request? ParseCore(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;
        var args = new System.Collections.Generic.List<string>();
        if (root.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in a.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.String || args.Count >= 64)
                    return null;
                string s = e.GetString() ?? "";
                if (s.Length > 32768)
                    return null;
                args.Add(s);
            }
        }
        string cwd = root.TryGetProperty("cwd", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
        string? cmd = root.TryGetProperty("cmd", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        return new Request(args.ToArray(), cwd, cmd);
    }

    public void Dispose()
    {
        Safe.Try(() =>
        {
            cts.Cancel();
            pending?.Dispose();

        });
    }
}
