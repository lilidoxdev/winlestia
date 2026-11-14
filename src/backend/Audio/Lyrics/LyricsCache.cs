// C# 12, fichier LyricsCache.cs, cible Windows 11 / .NET 8, cache disque des paroles : un petit JSON par morceau
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PanneauxGlass;

internal sealed class LyricsCache
{
    internal static readonly TimeSpan NegativeLife = TimeSpan.FromDays(7);
    internal const int MaxEntries = 500;

    readonly string directory;
    readonly Func<DateTime> now;
    readonly int maxEntries;

    internal LyricsCache(string directory, Func<DateTime>? now = null, int maxEntries = MaxEntries)
    {
        this.directory = directory;
        this.now = now ?? (() => DateTime.UtcNow);
        this.maxEntries = maxEntries;
    }

    string PathFor(string key) => Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24] + ".json");

    // Résultat mémorisé, ou null (absent, illisible, ou négatif périmé après 7 jours).
    internal LyricsResult? Get(string key)
    {
        try
        {
            string path = PathFor(key);
            if (!File.Exists(path))
                return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var root = doc.RootElement;
            string state = root.GetProperty("state").GetString() ?? "";
            if (state is not ("synced" or "plain" or "instrumental" or "none"))
                return null;
            var stored = DateTime.FromBinary(root.GetProperty("at").GetInt64());
            if (state == "none" && now() - stored > NegativeLife)
            {
                File.Delete(path);
                return null;
            }
            var lines = new List<LyricLine>();
            if (root.TryGetProperty("lines", out var array) && array.ValueKind == JsonValueKind.Array)
                foreach (var item in array.EnumerateArray())
                    lines.Add(new LyricLine(item.GetProperty("t").GetDouble(), item.GetProperty("x").GetString() ?? ""));
            string plain = root.TryGetProperty("plain", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
            return new LyricsResult(state, lines, plain);
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException
                                       or UnauthorizedAccessException or FormatException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    internal void Put(string key, LyricsResult result)
    {
        if (result.State is not ("synced" or "plain" or "instrumental" or "none"))
            return;
        try
        {
            Directory.CreateDirectory(directory);
            string json = JsonSerializer.Serialize(new
            {
                state = result.State,
                at = now().ToBinary(),
                lines = result.Lines.Select(l => new { t = l.Time, x = l.Text }).ToArray(),
                plain = result.Plain
            });
            string path = PathFor(key), temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, path, true);
            Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cache au mieux : un disque en lecture seule ne doit pas empêcher d'afficher les paroles.
        }
    }

    // Plafonne le dossier : les entrées les plus anciennes partent d'abord.
    void Trim()
    {
        var files = new DirectoryInfo(directory).GetFiles("*.json");
        if (files.Length <= maxEntries)
            return;
        foreach (var old in files.OrderBy(f => f.LastWriteTimeUtc).Take(files.Length - maxEntries))
        {
            try
            {
                old.Delete();
            }
            catch (IOException)
            {
                // Entrée utilisée ailleurs : elle partira au prochain passage.
            }
        }
    }
}
