// language: C# 12, file: WinjetInstances.cs, target: Windows 11 / .NET 8, instances posées, persistées dans <données>\winjets\instances.json
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Winlestia.Shared;
namespace PanneauxGlass.Winjets;

internal sealed class WinjetInstances
{
    internal const int FileVersion = 1;
    const int MaxInstances = 64;
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    readonly object gate = new();
    readonly string path;
    List<WinjetInstance> items = new();

    internal WinjetInstances(string path)
    {
        this.path = path;
        Load();
    }

    internal event Action? Changed;

    internal IReadOnlyList<WinjetInstance> All
    {
        get
        {
            lock (gate)
                return items.ToArray();
        }
    }

    internal WinjetInstance? Find(string id)
    {
        lock (gate)
            return items.FirstOrDefault(i => i.Id == id);
    }

    // Lecture avec reprise : principal -> .bak -> liste vide (le fichier illisible est gardé en .corrupt.bak), comme Store.
    void Load()
    {
        if (!File.Exists(path))
            return;
        var file = TryRead(path);
        if (file == null)
        {
            Store.Log("instances.json illisible : copie conservée (.corrupt.bak)");
            try
            {
                File.Copy(path, path + ".corrupt.bak", true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Store.Log("instances.json.corrupt.bak : " + ex.Message); }
            file = File.Exists(path + ".bak") ? TryRead(path + ".bak") : null;
            if (file != null)
            {
                Store.Log("instances.json restauré depuis .bak");
                try
                {
                    File.Copy(path + ".bak", path, true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Store.Log("instances.json : " + ex.Message); }
            }
        }
        items = Sanitize(file?.Instances ?? new List<WinjetInstance>());
    }

    static WinjetInstancesFile? TryRead(string file)
    {
        try
        {
            var data = JsonSerializer.Deserialize<WinjetInstancesFile>(File.ReadAllText(file, Encoding.UTF8), Json);
            if (data?.Instances == null)
                return null;
            if (data.Version > FileVersion)
                Store.Log("instances.json : version " + data.Version + " plus récente que " + FileVersion + ", lecture au mieux");
            return data;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    // Écarte les entrées sans identifiant valide ou en double, normalise les valeurs (JsonElement -> bool/double/string).
    static List<WinjetInstance> Sanitize(IEnumerable<WinjetInstance> raw)
    {
        var result = new List<WinjetInstance>();
        foreach (var i in raw.Where(r => r != null))
        {
            if (string.IsNullOrEmpty(i.Id) || i.Id.Length > 24 || string.IsNullOrEmpty(i.PackageId) || result.Any(r => r.Id == i.Id)
                || result.Count >= MaxInstances)
                continue;
            i.Geometry ??= new WinjetGeometry();
            i.Opacity = i.Opacity is { } o ? Math.Clamp(o, 0, 100) : null;
            i.Settings = Plain(i.Settings);
            i.State = Plain(i.State);
            result.Add(i);
        }
        return result;
    }

    static Dictionary<string, object?> Plain(Dictionary<string, object?>? d)
        => (d ?? new()).Select(p => (p.Key, Value: WinjetSettingsCoercer.Plain(p.Value))).Where(p => p.Value != null)
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string text;
            lock (gate)
                text = JsonSerializer.Serialize(new WinjetInstancesFile { Version = FileVersion, Instances = items }, Json);
            AtomicFile.WriteAllText(path, text, Encoding.UTF8, path + ".bak");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Store.Log("instances.json : enregistrement : " + ex.Message); }
        Changed?.Invoke();
    }

    static string NewId(ICollection<WinjetInstance> existing)
    {
        while (true)
        {
            string id = "w-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
            if (existing.All(i => i.Id != id))
                return id;
        }
    }

    // Nouvelle instance à la taille par défaut. Sans x/y : première place libre sur une grille de 16 px de gouttière.
    internal WinjetInstance? Add(WinjetPackage package, string screen, double? x, double? y)
    {
        WinjetInstance instance;
        lock (gate)
        {
            if (items.Count >= MaxInstances)
                return null;
            var size = package.Definition.Default;
            screen = ScreenName(screen, "");
            x = x is { } cx && double.IsFinite(cx) ? Coord(cx) : null;
            y = y is { } cy && double.IsFinite(cy) ? Coord(cy) : null;
            var (px, py) = x is { } fx && y is { } fy ? (fx, fy) : FirstFree(screen, size.W, size.H);
            instance = new WinjetInstance
            {
                Id = NewId(items),
                PackageId = package.Id,
                Geometry = new WinjetGeometry { Screen = screen, X = px, Y = py, W = size.W, H = size.H },
                Settings = WinjetSettingsCoercer.Defaults(package.Definition),
                State = package.Definition.State.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal)
            };
            items.Add(instance);
        }
        Save();
        return instance;
    }

    // Balayage ligne par ligne d'une zone supposée de 1920×1080 ; le frontend replace ensuite selon la vraie zone de travail.
    (double, double) FirstFree(string screen, double w, double h)
    {
        var onScreen = items.Where(i => i.Geometry.Screen == screen).Select(i => i.Geometry).ToList();
        for (double y = 16; y + h <= 1080; y += 24)
            for (double x = 16; x + w <= 1920; x += 24)
                if (!onScreen.Any(g => x < g.X + g.W + 16 && g.X < x + w + 16 && y < g.Y + g.H + 16 && g.Y < y + h + 16))
                    return (x, y);
        return (16 + onScreen.Count % 8 * 24, 16 + onScreen.Count % 8 * 24);
    }

    internal bool Remove(string id)
    {
        bool removed;
        lock (gate)
            removed = items.RemoveAll(i => i.Id == id) > 0;
        if (removed)
            Save();
        return removed;
    }

    internal int RemoveForPackage(string packageId)
    {
        int n;
        lock (gate)
            n = items.RemoveAll(i => i.PackageId == packageId);
        if (n > 0)
            Save();
        return n;
    }

    internal bool SetHidden(string id, bool hidden)
    {
        lock (gate)
        {
            var i = items.FirstOrDefault(x => x.Id == id);
            if (i == null)
                return false;
            i.Hidden = hidden;
        }
        Save();
        return true;
    }

    // Null JSON = retour au réglage de verre global ; un nombre fini est arrondi puis borné à 0..100 ; le reste est refusé.
    internal bool SetOpacity(string id, JsonElement value)
    {
        int? opacity;
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            opacity = null;
        else if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double d) && double.IsFinite(d))
            opacity = (int)Math.Clamp(Math.Round(d), 0, 100);
        else
            return false;
        lock (gate)
        {
            var i = items.FirstOrDefault(x => x.Id == id);
            if (i == null)
                return false;
            if (i.Opacity == opacity)
                return true;
            i.Opacity = opacity;
        }
        Save();
        return true;
    }

    // Le frontend clampe déjà ; le backend borne en plus aux tailles du paquet et rejette ce qui n'est pas fini.
    internal bool SetGeometry(string id, JsonElement geometry, WinjetDefinition def)
    {
        if (geometry.ValueKind != JsonValueKind.Object)
            return false;
        lock (gate)
        {
            var i = items.FirstOrDefault(x => x.Id == id);
            if (i == null)
                return false;
            var g = i.Geometry.Copy();
            if (geometry.TryGetProperty("screen", out var s) && s.ValueKind == JsonValueKind.String)
                g.Screen = ScreenName(s.GetString(), g.Screen);
            g.X = Num(geometry, "x", g.X, -MaxCoord, MaxCoord);
            g.Y = Num(geometry, "y", g.Y, -MaxCoord, MaxCoord);
            g.W = Math.Clamp(Num(geometry, "w", g.W, 0, 100000), def.Min.W, def.Max.W);
            g.H = Math.Clamp(Num(geometry, "h", g.H, 0, 100000), def.Min.H, def.Max.H);
            g.Nx = Num(geometry, "nx", g.Nx, -10, 10);
            g.Ny = Num(geometry, "ny", g.Ny, -10, 10);
            if (geometry.TryGetProperty("recovered", out var r) && r.ValueKind is JsonValueKind.True or JsonValueKind.False)
                g.Recovered = r.GetBoolean();
            i.Geometry = g;
        }
        Save();
        return true;
    }

    const double MaxCoord = 100000;

    // Bornes communes à Add et SetGeometry : nom d'écran de 100 caractères au plus, coordonnées dans ±100000.
    static string ScreenName(string? value, string fallback) => value != null && value.Length <= 100 ? value : fallback;

    static double Coord(double v) => Math.Clamp(v, -MaxCoord, MaxCoord);

    static double Num(JsonElement e, string name, double fallback, double min, double max)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d)
            ? Math.Clamp(d, min, max) : fallback;

    internal bool SetSettings(string id, JsonElement settings, WinjetDefinition def)
    {
        lock (gate)
        {
            var i = items.FirstOrDefault(x => x.Id == id);
            if (i == null)
                return false;
            i.Settings = WinjetSettingsCoercer.Settings(def, settings, i.Settings);
        }
        Save();
        return true;
    }

    // Clé déclarée seulement ; une valeur de mauvais type est refusée (message d'erreur renvoyé).
    internal string? SetState(string id, string key, JsonElement value, WinjetDefinition def)
    {
        if (!def.State.TryGetValue(key, out var initial))
            return "Clé d'état non déclarée : " + key;
        var ok = WinjetSettingsCoercer.StateValue(initial, value);
        if (ok == null)
            return "Valeur invalide pour la clé d'état " + key;
        lock (gate)
        {
            var i = items.FirstOrDefault(x => x.Id == id);
            if (i == null)
                return "Instance inconnue : " + id;
            i.State[key] = ok;
        }
        Save();
        return null;
    }

    // Après une mise à jour du paquet : réglages et état recalés sur la nouvelle définition (clés retirées écartées).
    internal void Reconcile(WinjetPackage package)
    {
        bool any = false;
        lock (gate)
            foreach (var i in items.Where(x => x.PackageId == package.Id))
            {
                i.Settings = WinjetSettingsCoercer.Settings(package.Definition, i.Settings);
                i.State = WinjetSettingsCoercer.State(package.Definition, i.State);
                i.Geometry.W = Math.Clamp(i.Geometry.W, package.Definition.Min.W, package.Definition.Max.W);
                i.Geometry.H = Math.Clamp(i.Geometry.H, package.Definition.Min.H, package.Definition.Max.H);
                any = true;
            }
        if (any)
            Save();
    }

    // Écran principal (screen vide) pour toutes les instances, en cascade depuis le coin supérieur gauche.
    internal void RecoverAll()
    {
        lock (gate)
        {
            int n = 0;
            foreach (var i in items)
            {
                i.Geometry.Screen = "";
                i.Geometry.X = i.Geometry.Y = 16 + n++ % 10 * 24;
                i.Geometry.Nx = i.Geometry.Ny = 0;
                i.Geometry.Recovered = false;
                i.Hidden = false;
            }
        }
        Save();
    }
}
