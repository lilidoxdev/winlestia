// language: C# 12, file: WinjetDefinition.cs, target: Windows 11 / .NET 8, widget.json : tailles, réglages, état, sources de données
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
namespace PanneauxGlass.Winjets;

internal sealed record WinjetSize(int W, int H);

// Réglage déclaré. `Default` est un bool, un double ou un string selon `Type` (toggle, number, text/choice).
internal sealed record WinjetSetting(string Id, string Type, string Label, object Default, IReadOnlyList<string> Options, double Min,
    double Max, int MaxLength);

// Source de données : « file » (JSON local sous assets/), « http » (HTTPS, hôte déclaré) ou « market » (cours crypto en direct).
internal sealed record WinjetSource(string Id, string Type, string Path, string Url, int Refresh, string EnabledWhen,
    IReadOnlyList<string>? Symbols = null)
{
    internal bool IsHttp => Type == "http";
    internal bool IsMarket => Type == "market";
    internal bool IsFile => Type == "file";
}

internal sealed class WinjetDefinition
{
    internal WinjetSize Default { get; private init; } = new(352, 168);
    internal WinjetSize Min { get; private init; } = new(WinjetLimits.MinSide, WinjetLimits.MinSide);
    internal WinjetSize Max { get; private init; } = new(WinjetLimits.MaxWidth, WinjetLimits.MaxHeight);
    internal IReadOnlyList<WinjetSetting> Settings { get; private init; } = Array.Empty<WinjetSetting>();
    // Clés d'état déclarées et leur valeur initiale (bool, double ou string).
    internal IReadOnlyDictionary<string, object> State { get; private init; } = new Dictionary<string, object>();
    internal IReadOnlyList<WinjetSource> Sources { get; private init; } = Array.Empty<WinjetSource>();
    // JSON d'origine, renvoyé tel quel au frontend (le renderer fait foi sur la mise en page).
    internal JsonElement Raw { get; private init; }

    // `dir` null = on ne vérifie pas l'existence des fichiers (aucun dossier de paquet disponible).
    internal static WinjetDefinition? Parse(JsonElement root, WinjetManifest manifest, string? dir, WinjetIssues issues)
    {
        int before = issues.Count;
        issues.Scope = "widget.json";
        if (root.ValueKind != JsonValueKind.Object)
        {
            issues.Add("$", "doit être un objet JSON.");
            return null;
        }
        issues.Only(root, "$", "size", "settings", "state", "data", "layouts");
        var size = ParseSize(root, issues);
        var settings = ParseSettings(root, issues);
        var state = ParseState(root, issues);
        var sources = ParseSources(root, manifest, dir, issues);
        WinjetLayoutValidator.Validate(root, manifest, dir, state.Keys.ToHashSet(StringComparer.Ordinal), issues);
        if (issues.Count > before)
            return null;
        return new WinjetDefinition
        {
            Default = size.def,
            Min = size.min,
            Max = size.max,
            Settings = settings,
            State = state,
            Sources = sources,
            Raw = root.Clone()
        };
    }

    static (WinjetSize def, WinjetSize min, WinjetSize max) ParseSize(JsonElement root, WinjetIssues issues)
    {
        WinjetSize def = new(352, 168), min = new(WinjetLimits.MinSide, WinjetLimits.MinSide),
            max = new(WinjetLimits.MaxWidth, WinjetLimits.MaxHeight);
        var size = issues.Obj(root, "size", "$", true);
        if (size is not { } s)
            return (def, min, max);
        issues.Only(s, "$.size", "default", "min", "max");
        var d = Pair(s, "default", true, issues);
        var lo = Pair(s, "min", false, issues);
        var hi = Pair(s, "max", false, issues);
        // Règles du moteur : plancher 120×120, plafond 1600×1000 ; les valeurs hors bornes sont ramenées dans les bornes.
        if (lo != null)
            min = new WinjetSize(Math.Max(lo.W, WinjetLimits.MinSide), Math.Max(lo.H, WinjetLimits.MinSide));
        if (hi != null)
            max = new WinjetSize(Math.Min(hi.W, WinjetLimits.MaxWidth), Math.Min(hi.H, WinjetLimits.MaxHeight));
        if (min.W > max.W || min.H > max.H)
            issues.Add("$.size", "« min » dépasse « max ».");
        else if (d != null)
            def = new WinjetSize(Math.Clamp(d.W, min.W, max.W), Math.Clamp(d.H, min.H, max.H));
        return (def, min, max);
    }

    static WinjetSize? Pair(JsonElement size, string name, bool required, WinjetIssues issues)
    {
        string path = "$.size." + name;
        if (!size.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            if (required)
                issues.Add(path, "champ obligatoire.");
            return null;
        }
        if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 2 || v.EnumerateArray().Any(e
            => e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out int n) || n <= 0 || n > 20000))
        {
            issues.Add(path, "doit être [largeur, hauteur] en entiers positifs.");
            return null;
        }
        return new WinjetSize(v[0].GetInt32(), v[1].GetInt32());
    }

    static IReadOnlyList<WinjetSetting> ParseSettings(JsonElement root, WinjetIssues issues)
    {
        var result = new List<WinjetSetting>();
        if (issues.Arr(root, "settings", "$", false, 24) is not { } arr)
            return result;
        int index = 0;
        foreach (var item in arr.EnumerateArray())
        {
            string at = "$.settings[" + index++ + "]";
            if (item.ValueKind != JsonValueKind.Object)
            {
                issues.Add(at, "doit être un objet.");
                continue;
            }
            string? id = issues.Str(item, "id", at, true, 32);
            if (id != null && !WinjetLimits.SettingIdPattern.IsMatch(id))
                issues.Add(at + ".id", "identifiant invalide « " + id + " » (lettre puis lettres, chiffres ou _).");
            else if (id != null && result.Any(r => r.Id == id))
                issues.Add(at + ".id", "identifiant en double « " + id + " ».");
            string label = issues.Str(item, "label", at, true, 80) ?? "";
            string? type = issues.Str(item, "type", at, true, 20);
            if (id == null || type == null)
                continue;
            var setting = ParseOneSetting(item, at, id, type, label, issues);
            if (setting != null)
                result.Add(setting);
        }
        return result;
    }

    static WinjetSetting? ParseOneSetting(JsonElement item, string at, string id, string type, string label, WinjetIssues issues)
    {
        item.TryGetProperty("default", out var def);
        string[] allowed = type switch
        {
            "text" => new[] { "id", "type", "label", "default", "maxLength" },
            "choice" => new[] { "id", "type", "label", "default", "options" },
            "number" => new[] { "id", "type", "label", "default", "min", "max" },
            _ => new[] { "id", "type", "label", "default" }
        };
        foreach (var p in item.EnumerateObject())
            if (!allowed.Contains(p.Name))
                issues.Add(at + "." + p.Name, "propriété non permise pour un réglage de type « " + type + " ».");
        switch (type)
        {
            case "toggle":
                if (def.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    issues.Add(at + ".default", "doit être vrai ou faux.");
                    return null;
                }
                return new WinjetSetting(id, type, label, def.GetBoolean(), Array.Empty<string>(), 0, 0, 0);
            case "text":
                {
                    int maxLength = (int)Math.Clamp(issues.Num(item, "maxLength", at, false) ?? 100, 1, 200);
                    if (def.ValueKind != JsonValueKind.String || (def.GetString() ?? "").Length > maxLength)
                    {
                        issues.Add(at + ".default", "doit être un texte d'au plus " + maxLength + " caractères.");
                        return null;
                    }
                    return new WinjetSetting(id, type, label, def.GetString()!, Array.Empty<string>(), 0, 0, maxLength);
                }
            case "number":
                {
                    double min = issues.Num(item, "min", at, false) ?? double.MinValue / 4,
                        max = issues.Num(item, "max", at, false) ?? double.MaxValue / 4;
                    if (min > max)
                    {
                        issues.Add(at, "« min » dépasse « max ».");
                        return null;
                    }
                    if (def.ValueKind != JsonValueKind.Number || !def.TryGetDouble(out double n) || n < min || n > max)
                    {
                        issues.Add(at + ".default", "doit être un nombre compris entre min et max.");
                        return null;
                    }
                    return new WinjetSetting(id, type, label, n, Array.Empty<string>(), min, max, 0);
                }
            case "choice":
                {
                    var values = new List<string>();
                    if (issues.Arr(item, "options", at, true, 32) is { } options)
                    {
                        int k = 0;
                        foreach (var o in options.EnumerateArray())
                        {
                            string oat = at + ".options[" + k++ + "]";
                            if (o.ValueKind != JsonValueKind.Object)
                            {
                                issues.Add(oat, "doit être { value, label }.");
                                continue;
                            }
                            string? value = issues.Str(o, "value", oat, true, 60);
                            issues.Str(o, "label", oat, true, 80);
                            issues.Only(o, oat, "value", "label");
                            if (value != null && !values.Contains(value))
                                values.Add(value);
                        }
                    }
                    if (values.Count == 0)
                    {
                        issues.Add(at + ".options", "au moins une option est nécessaire.");
                        return null;
                    }
                    if (def.ValueKind != JsonValueKind.String || !values.Contains(def.GetString() ?? ""))
                    {
                        issues.Add(at + ".default", "doit être l'une des valeurs des options.");
                        return null;
                    }
                    return new WinjetSetting(id, type, label, def.GetString()!, values, 0, 0, 0);
                }
            default:
                issues.Add(at + ".type", "type « " + type + " » inconnu (toggle, text, choice ou number).");
                return null;
        }
    }

    static IReadOnlyDictionary<string, object> ParseState(JsonElement root, WinjetIssues issues)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        if (issues.Obj(root, "state", "$", false) is not { } state)
            return result;
        foreach (var p in state.EnumerateObject())
        {
            string at = "$.state." + p.Name;
            if (!WinjetLimits.SettingIdPattern.IsMatch(p.Name))
                issues.Add(at, "clé d'état invalide.");
            else if (result.Count >= 24)
                issues.Add("$.state", "trop de clés d'état (maximum 24).");
            else if (p.Value.ValueKind == JsonValueKind.String && p.Value.GetString()!.Length <= 100)
                result[p.Name] = p.Value.GetString()!;
            else if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out double d) && double.IsFinite(d))
                result[p.Name] = d;
            else if (p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                result[p.Name] = p.Value.GetBoolean();
            else
                issues.Add(at, "la valeur initiale doit être un texte (100 caractères au plus), un nombre ou vrai/faux.");
        }
        return result;
    }

    static IReadOnlyList<WinjetSource> ParseSources(JsonElement root, WinjetManifest manifest, string? dir, WinjetIssues issues)
    {
        var result = new List<WinjetSource>();
        if (issues.Obj(root, "data", "$", false) is not { } data)
            return result;
        foreach (var p in data.EnumerateObject())
        {
            string at = "$.data." + p.Name;
            if (!WinjetLimits.SettingIdPattern.IsMatch(p.Name))
            {
                issues.Add(at, "nom de source invalide (lettre puis lettres, chiffres ou _).");
                continue;
            }
            if (result.Count >= 8)
            {
                issues.Add("$.data", "trop de sources (maximum 8).");
                break;
            }
            if (p.Value.ValueKind != JsonValueKind.Object)
            {
                issues.Add(at, "doit être un objet.");
                continue;
            }
            string? type = issues.Str(p.Value, "type", at, true, 10);
            issues.Only(p.Value, at, type == "file" ? new[] { "type", "path" } : type == "market" ? new[] { "type", "symbols", "enabledWhen" }
                : new[] { "type", "url", "refresh", "enabledWhen" });
            string? when = issues.Str(p.Value, "enabledWhen", at, false, 400);
            if (when != null && !WinjetCondition.TryParse(when, out _, out string why))
                issues.Add(at + ".enabledWhen", "expression invalide (" + why + ").");
            if (type == "file")
            {
                string? path = issues.Str(p.Value, "path", at, true, 200);
                if (path != null && CheckAsset(path, dir, at + ".path", issues, ".json"))
                    result.Add(new WinjetSource(p.Name, type, path, "", 0, when ?? ""));
            }
            else if (type == "http")
            {
                string? url = issues.Str(p.Value, "url", at, true, 500);
                double refresh = issues.Num(p.Value, "refresh", at, false) ?? 300;
                // Intervalle minimal imposé : jamais moins d'une minute, jamais plus d'un jour.
                int clamped = (int)Math.Clamp(refresh, WinjetLimits.MinRefreshSeconds, 86400);
                if (url != null)
                {
                    string? bad = HttpsProblem(url, manifest);
                    if (bad != null)
                        issues.Add(at + ".url", bad);
                    else
                        result.Add(new WinjetSource(p.Name, type, "", url, clamped, when ?? ""));
                }
            }
            else if (type == "market")
            {
                var symbols = ParseMarketSymbols(p.Value, at, manifest, issues);
                if (symbols != null)
                    result.Add(new WinjetSource(p.Name, type, "", "", 0, when ?? "", symbols));
            }
            else if (type != null)
                issues.Add(at + ".type", "type de source « " + type + " » inconnu (file, http ou market).");
        }
        return result;
    }

    // « symbols » d'une source market (1 à 4 tickers) et hôtes Binance déclarés dans permissions.network ; null si invalide.
    static IReadOnlyList<string>? ParseMarketSymbols(JsonElement source, string at, WinjetManifest manifest, WinjetIssues issues)
    {
        int before = issues.Count;
        foreach (string host in WinjetMarket.Hosts)
            if (!manifest.Network.Contains(host, StringComparer.OrdinalIgnoreCase))
                issues.Add(at + ".type", "l'hôte « " + host + " » n'est pas déclaré dans permissions.network du manifeste.");
        var list = new List<string>();
        if (issues.Arr(source, "symbols", at, true, WinjetMarket.MaxSymbols) is { } arr)
        {
            int i = 0;
            foreach (var item in arr.EnumerateArray())
            {
                string sat = at + ".symbols[" + i++ + "]";
                string? sym = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (sym == null || !WinjetMarket.IsValidSymbol(sym))
                    issues.Add(sat, "doit être un ticker en majuscules (2 à 10 lettres ou chiffres, ex. BTC).");
                else if (list.Contains(sym))
                    issues.Add(sat, "ticker en double « " + sym + " ».");
                else
                    list.Add(sym);
            }
            if (arr.GetArrayLength() == 0)
                issues.Add(at + ".symbols", "au moins un ticker est nécessaire.");
        }
        return issues.Count > before ? null : list;
    }

    // Problème d'une URL réseau (null = acceptable) : HTTPS, sans identifiants, hôte déclaré dans permissions.network.
    internal static string? HttpsProblem(string url, WinjetManifest manifest)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "adresse illisible.";
        if (uri.Scheme != Uri.UriSchemeHttps)
            return "seul https:// est autorisé.";
        if (uri.Port != 443)
            return "seul le port 443 est autorisé.";
        if (uri.UserInfo.Length > 0)
            return "les identifiants dans l'adresse sont interdits.";
        if (!manifest.Network.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase))
            return "l'hôte « " + uri.IdnHost + " » n'est pas déclaré dans permissions.network du manifeste.";
        return null;
    }

    // Chemin de ressource sous assets/ : relatif, sans « .. », extension attendue, fichier présent si le dossier est connu.
    internal static bool CheckAsset(string path, string? dir, string at, WinjetIssues issues, params string[] extensions)
    {
        if (!path.StartsWith("assets/", StringComparison.Ordinal) || path.Contains("..") || path.Contains('\\') || path.Contains(':')
            || path.EndsWith('/'))
        {
            issues.Add(at, "le chemin doit être relatif et situé sous assets/ (ex. assets/image.png).");
            return false;
        }
        string ext = System.IO.Path.GetExtension(path);
        if (!(extensions.Length == 0 ? WinjetLimits.ImageExtensions.Contains(ext) : extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)))
        {
            issues.Add(at, "extension « " + ext + " » non acceptée ici.");
            return false;
        }
        if (dir != null && !File.Exists(System.IO.Path.Combine(dir, path.Replace('/', System.IO.Path.DirectorySeparatorChar))))
        {
            issues.Add(at, "fichier introuvable dans le paquet : " + path + ".");
            return false;
        }
        return true;
    }
}
