// C# 12 / CliServer.cs / Local named pipe for the `winlestia` command line. Async wait for a connection only: no timer, no polling.
using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PanneauxGlass;

internal sealed class CliServer : IDisposable
{
    internal const string PipeName = "WinlestiaCli";
    const int MaxRequestChars = 4096;
    readonly CancellationTokenSource cancellation = new();
    readonly Func<CliCommand, Task<(int code, string text)>> run;

    internal CliServer(Func<CliCommand, Task<(int code, string text)>> run)
    {
        this.run = run;
        _ = Task.Run(Serve);
    }

    // Only the current user: the DACL holds a single entry for the user's SID. (CurrentUserOnly cannot be combined with an explicit PipeSecurity; the client uses it to check the server's owner.)
    static NamedPipeServerStream Create(bool first)
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None), 0, 0, security);
    }

    // Reads one line of at most MaxRequestChars characters, in bounded blocks. Returns null when the line is missing or too long.
    static async Task<string?> ReadBounded(StreamReader reader, CancellationToken token)
    {
        var sb = new StringBuilder();
        var buffer = new char[256];
        while (true)
        {
            int n = await reader.ReadAsync(buffer.AsMemory(), token);
            if (n == 0)
                return null;
            for (int i = 0; i < n; i++)
            {
                if (buffer[i] == '\n')
                    return sb.ToString().TrimEnd('\r');
                sb.Append(buffer[i]);
                if (sb.Length > MaxRequestChars)
                    return null;
            }
        }
    }

    async Task Serve()
    {
        bool first = true;
        int failures = 0;
        string lastMessage = "";
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var server = Create(first);
                first = false;
                failures = 0;
                lastMessage = "";
                await server.WaitForConnectionAsync(cancellation.Token);
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                limit.CancelAfter(TimeSpan.FromSeconds(5));   // a stalled client must not hold the single instance
                using var reader = new StreamReader(server, new UTF8Encoding(false), false, 256, true);
                string? line = await ReadBounded(reader, limit.Token);
                string reply;
                if (line == null)
                    reply = Reply(2, "Invalid request.");
                else
                    reply = await Handle(line);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                await writer.WriteLineAsync(reply.AsMemory(), limit.Token);   // no WaitForPipeDrain: a client that never reads must not block the loop
            }
            catch (OperationCanceledException) { if (cancellation.IsCancellationRequested) break; }
            catch (Exception ex)
            {
                // Progressive back-off up to 30 s (e.g. pipe name already taken); logged only when the message changes.
                failures++;
                if (ex.Message != lastMessage)
                {
                    lastMessage = ex.Message;
                    Store.Log("CLI: " + ex.Message);
                }
                var wait = TimeSpan.FromMilliseconds(Math.Min(30000, 500.0 * Math.Pow(2, Math.Min(failures - 1, 10))));
                try
                {
                    await Task.Delay(wait, cancellation.Token);
                }
                catch { break; }
            }
        }
    }

    async Task<string> Handle(string line)
    {
        string[]? args;
        try
        {
            args = JsonSerializer.Deserialize<string[]>(line);
        }
        catch (JsonException) { return Reply(2, "Invalid request."); }
        if (!CliCommands.TryParse(args, out var command, out var error))
            return Reply(2, error);
        try
        {
            var (code, text) = await run(command!).WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token);
            return Reply(code, text);
        }
        catch (TimeoutException) { return Reply(1, "The shell did not answer in time."); }
        catch (OperationCanceledException) { return Reply(1, "The shell is stopping."); }
        catch (Exception ex) { Store.Log("CLI: " + ex); return Reply(1, "Command failed: " + ex.Message); }
    }

    static string Reply(int code, string text) => JsonSerializer.Serialize(new { code, text });

    public void Dispose()
    {
        cancellation.Cancel();
        cancellation.Dispose();
    }
}
