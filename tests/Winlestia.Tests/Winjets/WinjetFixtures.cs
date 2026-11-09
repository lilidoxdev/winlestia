// C#12 / Outils de test Winjets : paquets minimaux fabriqués en code (dossier ou ZIP), dossier temporaire, variable d'environnement restaurée.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using PanneauxGlass.Winjets;
namespace Winlestia.Tests.Winjets;

// Dossier temporaire supprimé à la fin du test.
internal sealed class TempDir : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wj-test-" + Guid.NewGuid().ToString("N"));

    internal TempDir() => Directory.CreateDirectory(Path);

    internal string Combine(params string[] parts) => System.IO.Path.Combine(Path, System.IO.Path.Combine(parts));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

// Pose une variable d'environnement le temps du test et remet l'ancienne valeur.
internal sealed class EnvScope : IDisposable
{
    readonly string name;
    readonly string? old;

    internal EnvScope(string name, string? value)
    {
        this.name = name;
        old = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(name, old);
}

// Paquet en mémoire : nom de fichier -> octets. Les JSON sont modifiables via Edit.
internal sealed class Pkg
{
    internal static readonly Version Current = new(1, 0, 0);
    internal Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    // PNG minimal : signature + IHDR 1×1 (suffisant pour le contrôle de contenu).
    internal static byte[] Png(int w = 1, int h = 1) => new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52,
        (byte)(w >> 24), (byte)(w >> 16), (byte)(w >> 8), (byte)w, (byte)(h >> 24), (byte)(h >> 16), (byte)(h >> 8), (byte)h, 8, 6, 0, 0, 0
    };

    internal static string ManifestJson(string id = "com.test.demo", string version = "1.0.0") => $$$"""
        {"format":"winjet/1","id":"{{{id}}}","name":"Démo","version":"{{{version}}}","author":"Test","description":"Essai",
         "compatibility":{"winlestia":">=0.9.2"},"permissions":{"network":["api.example.com"]}}
        """;

    internal const string WidgetJson = """
        {
          "size": { "default": [352, 168], "min": [168, 168], "max": [720, 352] },
          "settings": [
            { "id": "showSeconds", "type": "toggle", "label": "Secondes", "default": false },
            { "id": "city", "type": "text", "label": "Ville", "default": "Paris", "maxLength": 10 },
            { "id": "range", "type": "choice", "label": "Période", "default": "7d",
              "options": [ { "value": "1d", "label": "24 h" }, { "value": "7d", "label": "7 jours" } ] },
            { "id": "decimals", "type": "number", "label": "Décimales", "default": 2, "min": 0, "max": 6 },
            { "id": "source", "type": "choice", "label": "Source", "default": "demo",
              "options": [ { "value": "demo", "label": "Démo" }, { "value": "live", "label": "En direct" } ] }
          ],
          "state": { "mode": "face", "count": 1, "flag": false },
          "data": {
            "demo": { "type": "file", "path": "assets/demo.json" },
            "live": { "type": "http", "url": "https://api.example.com/v1/price", "refresh": 120, "enabledWhen": "settings.source == 'live'" }
          },
          "layouts": [
            { "minWidth": 0, "root": { "type": "column", "gap": 8, "padding": 8, "children": [
              { "type": "text", "text": "{{ settings.city }}", "style": "title", "tone": "accent" },
              { "type": "clock", "variant": "{{settings.range}}", "showSeconds": "{{ settings.showSeconds }}" },
              { "type": "image", "src": "assets/logo.png", "fit": "cover" },
              { "type": "chart", "series": "data.live.prices", "color": "positive" },
              { "type": "buttons", "bind": "state.mode", "options": [ { "value": "face", "label": "Face" }, { "value": "pile", "label": "Pile" } ] },
              { "type": "stat", "value": "1", "action": { "type": "setState", "key": "mode", "value": "pile" } }
            ] } }
          ]
        }
        """;

    internal static Pkg Default(string id = "com.test.demo", string version = "1.0.0")
    {
        var p = new Pkg();
        p.Files["manifest.json"] = Encoding.UTF8.GetBytes(ManifestJson(id, version));
        p.Files["widget.json"] = Encoding.UTF8.GetBytes(WidgetJson);
        p.Files["assets/demo.json"] = Encoding.UTF8.GetBytes("{\"price\":1}");
        p.Files["assets/logo.png"] = Png();
        p.Files["preview.png"] = Png(400, 300);
        return p;
    }

    internal Pkg Edit(string file, Action<JsonObject> change)
    {
        var node = JsonNode.Parse(Files[file])!.AsObject();
        change(node);
        Files[file] = Encoding.UTF8.GetBytes(node.ToJsonString());
        return this;
    }

    internal Pkg Set(string file, string text)
    {
        Files[file] = Encoding.UTF8.GetBytes(text);
        return this;
    }

    internal Pkg Set(string file, byte[] bytes)
    {
        Files[file] = bytes;
        return this;
    }

    internal string WriteZip(string path)
    {
        using (var fs = File.Create(path))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            foreach (var (name, bytes) in Files)
            {
                using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                s.Write(bytes, 0, bytes.Length);
            }
        return path;
    }

    internal string WriteFolder(string dir)
    {
        foreach (var (name, bytes) in Files)
        {
            string full = Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, bytes);
        }
        return dir;
    }
}
