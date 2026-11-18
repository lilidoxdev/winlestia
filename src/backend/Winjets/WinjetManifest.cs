// language: C# 12, file: WinjetManifest.cs, target: Windows 11 / .NET 8, manifest.json d'un paquet Winjet (format winjet/1)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
namespace PanneauxGlass.Winjets;

internal sealed class WinjetManifest
{
    internal const string Format = "winjet/1";

    internal string Id { get; private init; } = "";
    internal string Name { get; private init; } = "";
    internal string Version { get; private init; } = "";
    internal string Author { get; private init; } = "";
    internal string Description { get; private init; } = "";
    internal string License { get; private init; } = "";
    internal string Homepage { get; private init; } = "";
    internal string CompatibleWith { get; private init; } = "";
    internal IReadOnlyList<string> Network { get; private init; } = Array.Empty<string>();
    // JSON d'origine, renvoyé tel quel au frontend.
    internal JsonElement Raw
    {
        get; private init;
    }

    internal bool AllowsHost(string host)
        => Network.Contains(host, StringComparer.OrdinalIgnoreCase)
           || (Homepage.Length > 0 && Uri.TryCreate(Homepage, UriKind.Absolute, out var h)
               && string.Equals(h.Host, host, StringComparison.OrdinalIgnoreCase));

    // Valide et construit ; null si au moins une erreur a été signalée.
    internal static WinjetManifest? Parse(JsonElement root, Version current, WinjetIssues issues)
    {
        int before = issues.Count;
        issues.Scope = "manifest.json";
        if (root.ValueKind != JsonValueKind.Object)
        {
            issues.Add("$", "doit être un objet JSON.");
            return null;
        }
        issues.Only(root, "$", "format", "id", "name", "version", "author", "description", "license", "homepage", "compatibility",
            "permissions");
        string? format = issues.Str(root, "format", "$", true);
        if (format != null && format != Format)
            issues.Add("$.format", "valeur « " + format + " » non prise en charge (attendu « " + Format + " »).");

        string? id = issues.Str(root, "id", "$", true, 64);
        if (id != null && (id.Length < 3 || !WinjetLimits.IdPattern.IsMatch(id)))
            issues.Add("$.id", "identifiant invalide (attendu : lettres minuscules, chiffres, points et tirets, ex. com.exemple.horloge).");
        if (id != null && WinjetLimits.ReservedNames.Contains(id.Split('.')[0]))
            issues.Add("$.id", "le premier segment « " + id.Split('.')[0] + " » est un nom réservé de Windows.");
        string? name = issues.Str(root, "name", "$", true, 60);
        string? version = issues.Str(root, "version", "$", true, 20);
        if (version != null && !WinjetLimits.SemVerPattern.IsMatch(version))
            issues.Add("$.version", "version invalide « " + version + " » (attendu x.y.z, ex. 1.0.0).");
        string? author = issues.Str(root, "author", "$", true, 80);
        string? description = issues.Str(root, "description", "$", false, 300);
        string? license = issues.Str(root, "license", "$", false, 60);
        string? homepage = issues.Str(root, "homepage", "$", false, 200);
        if (homepage != null && !(Uri.TryCreate(homepage, UriKind.Absolute, out var home) && home.Scheme == Uri.UriSchemeHttps
            && home.UserInfo.Length == 0))
            issues.Add("$.homepage", "doit être une adresse https://.");

        string compat = "";
        var compatibility = issues.Obj(root, "compatibility", "$", false);
        if (compatibility is { } cp)
        {
            issues.Only(cp, "$.compatibility", "winlestia");
            compat = issues.Str(cp, "winlestia", "$.compatibility", false, 60) ?? "";
            if (compat.Length > 0)
            {
                if (!WinjetSemVer.TryMatches(compat, current, out bool ok))
                    issues.Add("$.compatibility.winlestia", "plage illisible « " + compat + " » (ex. >=0.9.2 <2.0.0).");
                else if (!ok)
                    issues.Add("$.compatibility.winlestia", "ce Winjet demande Winlestia « " + compat + " » ; la version installée est "
                        + current + ".");
            }
        }

        var hosts = new List<string>();
        var permissions = issues.Obj(root, "permissions", "$", false);
        if (permissions is { } pm0)
            issues.Only(pm0, "$.permissions", "network");
        if (permissions is { } pm && issues.Arr(pm, "network", "$.permissions", false, 8) is { } net)
        {
            int index = 0;
            foreach (var item in net.EnumerateArray())
            {
                string at = "$.permissions.network[" + index++ + "]";
                string? host = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
                if (host == null)
                    issues.Add(at, "doit être un nom d'hôte (texte).");
                else if (!WinjetLimits.HostPattern.IsMatch(host))
                    issues.Add(at, "nom d'hôte invalide « " + host + " » (nom exact en minuscules, sans schéma, port, chemin ni joker).");
                else if (!hosts.Contains(host))
                    hosts.Add(host);
            }
        }

        if (issues.Count > before)
            return null;
        return new WinjetManifest
        {
            Id = id!,
            Name = name!,
            Version = version!,
            Author = author!,
            Description = description ?? "",
            License = license ?? "",
            Homepage = homepage ?? "",
            CompatibleWith = compat,
            Network = hosts,
            Raw = root.Clone()
        };
    }
}
