// Server side of the single-instance pipe (see backend/FilesPipe.cs for the protocol). One awaiting accept loop, no timer, no polling:
// an idle server is one pending overlapped ConnectNamedPipe. A broken message never reaches the UI and never ends the process.
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Winlestia.Shared;

namespace WinlestiaFiles;

internal sealed class SingleInstance : IDisposable {
    internal sealed record Request(string[] Args, string Cwd, string? Cmd);

    readonly Action<Request> onRequest;
    readonly CancellationTokenSource cts = new();
    NamedPipeServerStream? pending;

    SingleInstance(Action<Request> onRequest) { this.onRequest = onRequest; }

    // Many instances allowed, but at most MaxHandlers are served at once: a listener can then always be created ("all pipe instances are busy" cannot happen).
    const int MaxInstances = 32, MaxHandlers = 16;
    readonly SemaphoreSlim slots = new(MaxHandlers, MaxHandlers);

    static NamedPipeServerStream Create(bool first) =>
        new(FilesPipe.Name, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None));

    // Null when another instance already owns the pipe (or it cannot be created): the caller then simply runs standalone.
    internal static SingleInstance? TryStart(Action<Request> onRequest) {
        try {
            var server = new SingleInstance(onRequest) { pending = Create(true) };
            _ = server.Loop();
            return server;
        } catch { return null; }
    }

    async Task Loop() {
        int failures = 0;
        bool logged = false;
        while (!cts.IsCancellationRequested) {
            var stream = pending!;
            bool handed = false;
            try {
                await slots.WaitAsync(cts.Token).ConfigureAwait(false);   // saturated: the listener stays up, clients just wait their own short budget
                try {
                    await stream.WaitForConnectionAsync(cts.Token).ConfigureAwait(false);
                    pending = Create(false);                                // ready for the next client before this one is served
                    failures = 0; handed = true;
                    _ = Handle(stream);
                } finally { if (!handed) slots.Release(); }
            } catch (OperationCanceledException) {
                stream.Dispose(); return;
            } catch (Exception ex) {
                stream.Dispose();
                if (!logged) { logged = true; App.CrashLog(ex); }           // once, never one line per retry
                if (++failures >= 8) return;
                try { await Task.Delay(100 * failures, cts.Token).ConfigureAwait(false); pending = Create(false); }
                catch (OperationCanceledException) { return; }
                catch { continue; }
            }
        }
    }

    async Task Handle(NamedPipeServerStream stream) {
        using (stream) {
            try {
                using var limit = new CancellationTokenSource(2000);
                await stream.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), limit.Token).ConfigureAwait(false);
                var buffer = new byte[65536]; int n = 0;
                while (n < buffer.Length) {
                    int r = await stream.ReadAsync(buffer.AsMemory(n), limit.Token).ConfigureAwait(false);
                    if (r == 0) break;
                    n += r;
                    if (Array.IndexOf(buffer, (byte)'\n', n - r, r) >= 0) break;
                }
                if (n == 0) return;                               // connected and left (probe): nothing to parse, nothing to log
                var request = Parse(Encoding.UTF8.GetString(buffer, 0, n));
                if (request == null) return;
                onRequest(request);
                await stream.WriteAsync(new[] { (byte)'1' }, limit.Token).ConfigureAwait(false);
                stream.WaitForPipeDrain();
            } catch (IOException) {                           // client gave up (its own 150 ms budget): nothing to report
            } catch (OperationCanceledException) {
            } catch (System.Text.Json.JsonException) {        // garbage from a foreign client
            } catch (Exception ex) { App.CrashLog(ex); }
            finally { try { slots.Release(); } catch (ObjectDisposedException) { } }
        }
    }

    static Request? Parse(string text) {
        using var doc = JsonDocument.Parse(text.Trim());
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        var args = new System.Collections.Generic.List<string>();
        if (root.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Array) {
            foreach (var e in a.EnumerateArray()) {
                if (e.ValueKind != JsonValueKind.String || args.Count >= 64) return null;
                string s = e.GetString() ?? "";
                if (s.Length > 32768) return null;
                args.Add(s);
            }
        }
        string cwd = root.TryGetProperty("cwd", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
        string? cmd = root.TryGetProperty("cmd", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        return new Request(args.ToArray(), cwd, cmd);
    }

    public void Dispose() { try { cts.Cancel(); pending?.Dispose(); } catch { } }
}
