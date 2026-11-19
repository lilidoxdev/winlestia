// C# 12, fichier PackagedApps.cs, cible Windows 11 / .NET 8, applis MSIX/Store : chemin versionné -> AUMID stable
// Le dossier d'un paquet (\WindowsApps\Claude_4.29.0.0_x64__pzs8sxrjxfjjc\) change à chaque mise à jour : une épingle qui le
// mémorise casse. On épingle donc shell:AppsFolder\<AUMID>, comme le catalogue (entrées « uwp:<AUMID> »).
// Pur volontairement (listes en entrée) pour être testable sans disque ni registre.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PanneauxGlass;

internal static class PackagedApps
{
    internal const string ShellPrefix = @"shell:AppsFolder\";
    static readonly Regex WindowsApps =
        new(@"^[A-Za-z]:\\(?:(?:[^\\]+\\)?Program Files[^\\]*\\)?WindowsApps\\(?<name>[^_\\]+)_(?<ver>[^_\\]+)_(?<arch>[^_\\]*)_"
        + @"(?<res>[^_\\]*)_(?<pub>[^_\\]+)(?:\\(?<rest>.*))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool TryParseWindowsAppsPath(string? path, out string packageName, out string publisherId, out string relativeExe)
    {
        packageName = publisherId = relativeExe = "";
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var m = WindowsApps.Match(path.Replace('/', '\\'));
        if (!m.Success)
            return false;
        packageName = m.Groups["name"].Value;
        publisherId = m.Groups["pub"].Value;
        relativeExe = m.Groups["rest"].Value;
        return true;
    }

    internal static string PackageFamilyName(string packageName, string publisherId) => packageName + "_" + publisherId;

    internal static string? ResolveAumid(string path, IEnumerable<Entry> catalogue) => ResolveAumid(path, catalogue, ManifestAumid);

    internal static string? ResolveAumid(string path, IEnumerable<Entry> catalogue, Func<string, string, string?>? manifestFallback)
    {
        if (!TryParseWindowsAppsPath(path, out string name, out string publisher, out string exe))
            return null;
        string prefix = "uwp:" + PackageFamilyName(name, publisher) + "!";
        var candidates = catalogue.Where(e => (e.Id ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 1)
            return candidates[0].Id.Substring(4);
        string family = PackageFamilyName(name, publisher);
        string? fromManifest = FromManifest(family, exe, manifestFallback);
        if (candidates.Count == 0)
            return fromManifest;
        // Plusieurs applis dans le paquet : l'IconPath d'un uwp: est un logo PNG, seul le manifeste départage les exécutables.
        var match = fromManifest == null ? null : candidates.FirstOrDefault(e => e.Id.Substring(4)
            .Equals(fromManifest, StringComparison.OrdinalIgnoreCase));
        return (match ?? candidates[0]).Id.Substring(4);
    }

    // Familles dont le manifeste est introuvable (appli désinstallée) : une seule tentative par processus.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> failedFamilies = new(StringComparer.OrdinalIgnoreCase);
    static string? FromManifest(string family, string exe, Func<string, string, string?>? manifestFallback)
    {
        if (manifestFallback == null || failedFamilies.ContainsKey(family))
            return null;
        try
        {
            string? aumid = manifestFallback(family, exe);
            if (aumid == null)
                failedFamilies[family] = true;
            return aumid;
        }
        catch (Exception)
        {
            failedFamilies[family] = true;
            return null;
        }
    }

    // Repli : le paquet installé (nouvelle version) porte le même nom de famille ; son manifeste donne l'Application Id.
    static string? ManifestAumid(string family, string relativeExe)
    {
        var manager = new Windows.Management.Deployment.PackageManager();
        foreach (var package in manager.FindPackagesForUser("", family))
        {
            string manifest = Path.Combine(package.InstalledLocation.Path, "AppxManifest.xml");
            var applications = XDocument.Load(manifest).Descendants().Where(x => x.Name.LocalName == "Application").ToList();
            var app = applications.FirstOrDefault(a => string.Equals(((string?)a.Attribute("Executable") ?? "").Replace('/', '\\'),
                relativeExe, StringComparison.OrdinalIgnoreCase)) ?? (applications.Count == 1 ? applications[0] : null);
            string? id = (string?)app?.Attribute("Id");
            if (!string.IsNullOrEmpty(id))
                return package.Id.FamilyName + "!" + id;
        }
        return null;
    }

    // Entrée « dock: » canonique d'une appli empaquetée (chemin WindowsApps ou clé shell:AppsFolder\ d'une fenêtre), sinon null.
    internal static Entry? Canonical(Entry entry, IEnumerable<Entry> catalogue, Func<string, string, string?>? manifestFallback = null)
    {
        string? aumid;
        string entryTarget = entry.Target ?? "";
        if (entryTarget.StartsWith(ShellPrefix, StringComparison.OrdinalIgnoreCase))
            aumid = entryTarget.Substring(ShellPrefix.Length);
        else
            aumid = ResolveAumid(entryTarget, catalogue, manifestFallback ?? ManifestAumid);
        if (string.IsNullOrEmpty(aumid))
            return null;
        string target = ShellPrefix + aumid;
        var cat = catalogue.FirstOrDefault(e => (e.Id ?? "").Equals("uwp:" + aumid, StringComparison.OrdinalIgnoreCase));
        string entryIcon = entry.IconPath ?? "";
        string icon = !string.IsNullOrEmpty(cat?.IconPath) ? cat!.IconPath
            : entryIcon.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || WindowsAppsGone(entryIcon) ? "" : entryIcon;
        return new Entry
        {
            Id = "dock:" + target,
            Name = entry.Name,
            Target = target,
            IconPath = icon,
            Section = entry.Section,
            Category = entry.Category
        };
    }

    static bool WindowsAppsGone(string path) => TryParseWindowsAppsPath(path, out _, out _, out _) && !File.Exists(path);

    // « uwp:<AUMID> » et « dock:shell:AppsFolder\<AUMID> » désignent la même appli.
    static string PinKey(string pin) => pin.StartsWith("uwp:", StringComparison.OrdinalIgnoreCase) ? pin.Substring(4)
        : pin.StartsWith("dock:" + ShellPrefix, StringComparison.OrdinalIgnoreCase) ? pin.Substring(5 + ShellPrefix.Length) : pin;

    // Réécrit les épingles « dock: » (et chemins bruts) qui visent un dossier WindowsApps versionné. Renvoie true si quelque chose a
    // changé ; removedEntryIds = entrées Custom fusionnées dans un doublon (l'appelant les retire aussi du catalogue en mémoire).
    internal static bool MigratePins(List<string> pins, List<Entry> custom, List<string> favorites, IReadOnlyCollection<Entry> catalogue,
        List<string> removedEntryIds, Func<string, string, string?>? manifestFallback = null, Preferences? prefs = null)
    {
        var memo = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? Resolve(string path)
        {
            if (!memo.TryGetValue(path, out var found))
                memo[path] = found = ResolveAumid(path, catalogue, manifestFallback);
            return found;
        }
        void Move(Dictionary<string, string>? d, string from, string to, bool survivorWins)
        {
            if (d == null || !d.TryGetValue(from, out var v))
                return;
            d.Remove(from);
            if (!survivorWins || !d.ContainsKey(to))
                d[to] = v;
        }
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (var e in custom.ToList())
        {
            string entryId = e.Id ?? "";
            if (!entryId.StartsWith("dock:", StringComparison.OrdinalIgnoreCase) || !TryParseWindowsAppsPath(e.Target, out _, out _, out _))
                continue;
            string? resolved = Resolve(e.Target);
            var canon = resolved == null ? null : Canonical(new Entry
            {
                Id = entryId,
                Name = e.Name,
                Target = ShellPrefix + resolved,
                IconPath = e.IconPath,
                Section = e.Section,
                Category = e.Category
            }, catalogue, manifestFallback);
            if (canon == null)
                continue;
            map[entryId] = canon.Id;
            var twin = custom.FirstOrDefault(x => !ReferenceEquals(x, e)
                && (x.Id ?? "").Equals(canon.Id, StringComparison.OrdinalIgnoreCase));
            if (twin != null)
            {
                custom.Remove(e);
                removedEntryIds.Add(entryId);
                if (prefs != null)
                {
                    Move(prefs.Names, entryId, twin.Id, true);
                    Move(prefs.Categories, entryId, twin.Id, true);
                    Move(prefs.Icons, entryId, twin.Id, true);
                }
            }
            else
            {
                string oldId = entryId;
                bool customIcon = prefs?.Icons?.ContainsKey(oldId) == true;
                e.Id = canon.Id;
                e.Target = canon.Target;
                if (!customIcon && canon.IconPath.Length > 0)
                    e.IconPath = canon.IconPath;
                if (prefs != null)
                {
                    Move(prefs.Names, oldId, e.Id, false);
                    Move(prefs.Categories, oldId, e.Id, false);
                    Move(prefs.Icons, oldId, e.Id, false);
                }
            }
            changed = true;
        }
        string Rewrite(string value)
        {
            if (map.TryGetValue(value, out var mapped))
                return mapped;
            if (TryParseWindowsAppsPath(value, out _, out _, out _) && Resolve(value) is { } aumid)
                return "uwp:" + aumid;
            return value;
        }
        var result = new List<string>(pins.Count);
        var rewrittenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pin in pins)
        {
            string next = Rewrite(pin);
            string key = PinKey(next);
            if (next != pin)
            {
                changed = true;
                if (rewrittenKeys.Contains(key) || result.Any(r => PinKey(r).Equals(key, StringComparison.OrdinalIgnoreCase)))
                    continue;
                rewrittenKeys.Add(key);
            }
            else if (rewrittenKeys.Contains(key))
            {
                changed = true;
                continue;
            }
            result.Add(next);
        }
        if (!result.SequenceEqual(pins))
        {
            pins.Clear();
            pins.AddRange(result);
        }
        for (int i = 0; i < favorites.Count; i++)
            if (map.TryGetValue(favorites[i], out var f) && f != favorites[i])
            {
                favorites[i] = f;
                changed = true;
            }
        return changed;
    }
}
