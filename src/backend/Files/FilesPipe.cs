// Côté client du canal à instance unique de Winlestia Fichiers, partagé (lié) par le backend et Winlestia.Files.
// Aucun type WPF ni System.Text.Json ici : le code s'exécute dans Program.Main avant que quoi que ce soit de lourd soit chargé.
// Protocole : le serveur écrit son PID (4 octets) à la connexion ; le client répond par une ligne JSON
// {"args":[..],"cwd":"..","cmd":".."}
// ; le serveur acquitte par '1'.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Winlestia.Shared;

internal static class FilesPipe
{
    // Par utilisateur : FNV-1a du nom d'utilisateur en minuscules (le canal lui-même est CurrentUserOnly).
    // WINLESTIA_FILES_PIPE : surcharge réservée aux tests pour qu'une instance de test ne parle jamais à l'instance résidente de
    // l'utilisateur (ni ne la remplace).
    internal static string Name
    {
        get
        {
            if (Environment.GetEnvironmentVariable("WINLESTIA_FILES_PIPE") is { Length: > 0 } over)
                return "Winlestia.Files.test." + over;
            uint h = 2166136261;
            foreach (char c in Environment.UserName.ToLowerInvariant())
            {
                h ^= c;
                h *= 16777619;
            }
            return "Winlestia.Files." + h.ToString("x8");
        }
    }

    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool WaitNamedPipe(string name,
        int timeoutMs);

    // File.Exists sur un chemin de canal s'y connecterait (et consommerait une instance du serveur) ; WaitNamedPipe se contente de sonder.
    // ERROR_FILE_NOT_FOUND = pas de serveur.
    static bool PipeExists(string name) => WaitNamedPipe(@"\\.\pipe\" + name, 1) || Marshal.GetLastWin32Error() != 2;

    internal static string Build(string[] args, string cwd, string? cmd)
    {
        var sb = new StringBuilder("{\"args\":[");
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0)
                sb.Append(',');
            Str(sb, args[i]);
        }
        sb.Append("],\"cwd\":");
        Str(sb, cwd);
        if (cmd != null)
        {
            sb.Append(",\"cmd\":");
            Str(sb, cmd);
        }
        return sb.Append("}\n").ToString();
    }

    static void Str(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            if (c == '"' || c == '\\')
                sb.Append('\\').Append(c);
            else if (c < 0x20)
                sb.Append("\\u").Append(((int)c).ToString("x4"));
            else
                sb.Append(c);
        }
        sb.Append('"');
    }

    // Vrai seulement quand un serveur vivant a acquitté le message. Ne bloque jamais plus d'environ timeoutMs.
    internal static bool TrySend(string json, int timeoutMs = 150)
    {
        try
        {
            string name = Name;
            if (!PipeExists(name))
                return false;     // aucun serveur : Connect tournerait sinon en boucle pendant tout le délai
            long start = Stopwatch.GetTimestamp();
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            client.Connect(timeoutMs);
            int left = timeoutMs - (int)((Stopwatch.GetTimestamp() - start) * 1000 / Stopwatch.Frequency);
            using var cts = new CancellationTokenSource(Math.Max(left, 20));
            var pid = new byte[4];
            if (!ReadAll(client, pid, cts.Token))
                return false;
            AllowSetForegroundWindow(BitConverter.ToInt32(pid, 0));
            client.WriteAsync(Encoding.UTF8.GetBytes(json), cts.Token).AsTask().GetAwaiter().GetResult();
            var ack = new byte[1];
            return ReadAll(client, ack, cts.Token) && ack[0] == (byte)'1';
        }
        catch { return false; }
    }

    static bool ReadAll(Stream s, byte[] buffer, CancellationToken token)
    {
        int n = 0;
        while (n < buffer.Length)
        {
            int r = s.ReadAsync(buffer.AsMemory(n), token).AsTask().GetAwaiter().GetResult();
            if (r == 0)
                return false;
            n += r;
        }
        return true;
    }
}
