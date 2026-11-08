// C# 12 / winlestia.exe / Client en ligne de commande : envoie une commande autorisée au shell en cours via un tube nommé réservé à
// l'utilisateur.
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using PanneauxGlass;

namespace WinlestiaCli;

// Métadonnées générées par source : le JSON par réflexion n'est plus disponible une fois l'exe élagué.
[JsonSerializable(typeof(string[]))]
internal partial class CliJson : JsonSerializerContext
{
}

static class Program
{
    static int Main(string[] args)
    {
        if (!CliCommands.TryParse(args, out var command, out var error))
        {
            Console.Error.WriteLine("winlestia: " + error);
            return 2;
        }
        if (command!.Kind == CliKind.Help)
        {
            Console.WriteLine(CliCommands.HelpText);
            return 0;
        }
        if (command.Kind == CliKind.Wallpaper)
        {   // `wallpaper list` is WallpaperList and never reaches this path resolution
            string full;
            try
            {
                full = Path.GetFullPath(command.Arg);
            }
            catch (Exception) { Console.Error.WriteLine("winlestia: invalid wallpaper path."); return 2; }
            if (!CliCommands.ValidWallpaper(full, File.Exists, out error))
            {
                Console.Error.WriteLine("winlestia: " + error);
                return 2;
            }
            args = command.Number > 0 ? new[] { "wallpaper", full, "--display", command.Number.ToString() } : new[] { "wallpaper", full };
        }
        try
        {
            // Asynchronous: otherwise the 8 s read limit cannot interrupt the read
            using var client = new NamedPipeClientStream(".", "WinlestiaCli", PipeDirection.InOut,
                PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            client.Connect(1500);
            var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(args, CliJson.Default.StringArray));
            var reader = new StreamReader(client, new UTF8Encoding(false), false, 4096, true);
            string? line;
            using (var limit = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                try
                {
                    line = reader.ReadLineAsync(limit.Token).AsTask().GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) { Console.Error.WriteLine("winlestia: the shell did not answer within 8 seconds.");
                    return 3; }
            }
            if (line == null)
            {
                Console.Error.WriteLine("winlestia: no answer from the shell.");
                return 3;
            }
            using var reply = JsonDocument.Parse(line);
            int code = reply.RootElement.GetProperty("code").GetInt32();
            string text = reply.RootElement.GetProperty("text").GetString() ?? "";
            if (code == 0)
            {
                Console.WriteLine(text);
                return 0;
            }
            Console.Error.WriteLine("winlestia: " + text);
            return code;
        }
        catch (TimeoutException) { Console.Error.WriteLine("winlestia: Winlestia is not running."); return 3; }
        catch (IOException ex) { Console.Error.WriteLine("winlestia: could not talk to Winlestia: " + ex.Message); return 3; }
        catch (UnauthorizedAccessException) { Console.Error.WriteLine("winlestia: access to the shell was refused."); return 3; }
        catch (JsonException) { Console.Error.WriteLine("winlestia: unreadable answer from the shell."); return 3; }
    }
}
