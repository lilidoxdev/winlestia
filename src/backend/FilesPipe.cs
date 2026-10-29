// Client side of the Winlestia Fichiers single-instance pipe, shared (linked) by the backend and Winlestia.Files.
// No WPF type and no System.Text.Json here: it runs in Program.Main before anything heavy is loaded.
// Protocol: server writes its PID (4 bytes) on connect; client replies with one JSON line {"args":[..],"cwd":"..","cmd":".."}; server acks with '1'.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Winlestia.Shared;

internal static class FilesPipe {
    // Per user: FNV-1a of the lower-cased user name (the pipe itself is CurrentUserOnly).
    // WINLESTIA_FILES_PIPE: test-only override so a test instance never talks to (or replaces) the user's resident one.
    internal static string Name {
        get { if (Environment.GetEnvironmentVariable("WINLESTIA_FILES_PIPE") is { Length: > 0 } over) return "Winlestia.Files.test." + over; uint h = 2166136261; foreach (char c in Environment.UserName.ToLowerInvariant()) { h ^= c; h *= 16777619; } return "Winlestia.Files." + h.ToString("x8"); }
    }

    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool WaitNamedPipe(string name, int timeoutMs);

    // File.Exists on a pipe path would connect to it (and burn a server instance); WaitNamedPipe only probes. ERROR_FILE_NOT_FOUND = no server.
    static bool PipeExists(string name) => WaitNamedPipe(@"\\.\pipe\" + name, 1) || Marshal.GetLastWin32Error() != 2;

    internal static string Build(string[] args, string cwd, string? cmd) {
        var sb = new StringBuilder("{\"args\":[");
        for (int i = 0; i < args.Length; i++) { if (i > 0) sb.Append(','); Str(sb, args[i]); }
        sb.Append("],\"cwd\":"); Str(sb, cwd);
        if (cmd != null) { sb.Append(",\"cmd\":"); Str(sb, cmd); }
        return sb.Append("}\n").ToString();
    }

    static void Str(StringBuilder sb, string s) {
        sb.Append('"');
        foreach (char c in s) {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        sb.Append('"');
    }

    // True only when a live server acknowledged the message. Never blocks longer than about timeoutMs.
    internal static bool TrySend(string json, int timeoutMs = 150) {
        try {
            string name = Name;
            if (!PipeExists(name)) return false;     // no server: Connect would otherwise spin for the whole timeout
            long start = Stopwatch.GetTimestamp();
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            client.Connect(timeoutMs);
            int left = timeoutMs - (int)((Stopwatch.GetTimestamp() - start) * 1000 / Stopwatch.Frequency);
            using var cts = new CancellationTokenSource(Math.Max(left, 20));
            var pid = new byte[4];
            if (!ReadAll(client, pid, cts.Token)) return false;
            AllowSetForegroundWindow(BitConverter.ToInt32(pid, 0));
            client.WriteAsync(Encoding.UTF8.GetBytes(json), cts.Token).AsTask().GetAwaiter().GetResult();
            var ack = new byte[1];
            return ReadAll(client, ack, cts.Token) && ack[0] == (byte)'1';
        } catch { return false; }
    }

    static bool ReadAll(Stream s, byte[] buffer, CancellationToken token) {
        int n = 0;
        while (n < buffer.Length) {
            int r = s.ReadAsync(buffer.AsMemory(n), token).AsTask().GetAwaiter().GetResult();
            if (r == 0) return false;
            n += r;
        }
        return true;
    }
}
