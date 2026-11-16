// C# 12, fichier DockItems.cs, cible Windows 11 / .NET 8, catalogue des éléments du dock et normalisation de leur disposition
using System;
using System.Collections.Generic;
using System.Linq;
namespace PanneauxGlass;

// Barre = éléments toujours visibles ; tiroir = éléments repliés ; tout ce qui est dans aucune des deux listes est masqué.
internal static class DockItems
{
    internal static readonly string[] Launchers = { "start", "recycle", "files", "search" };
    internal static readonly string[] Tools = { "autoTile", "running", "volume", "network", "settings", "power" };
    internal static readonly string[] Toggles = { "wifi", "bluetooth", "airplane", "micMute", "keepAwake" };
    internal static readonly string[] Shortcuts = { "nightLight", "focus" };
    internal static readonly string[] All = Launchers.Concat(Tools).Concat(Toggles).Concat(Shortcuts).ToArray();
    internal static readonly string[] DefaultBar = { "start", "recycle", "files", "search", "volume", "network", "settings", "power" };
    internal static readonly string[] DefaultDrawer = { "autoTile", "running", "wifi", "bluetooth", "micMute", "keepAwake" };
    internal const string Required = "settings";

    internal static (List<string> Bar, List<string> Drawer) Normalise(IEnumerable<string>? bar, IEnumerable<string>? drawer)
    {
        bar ??= DefaultBar;
        drawer ??= DefaultDrawer;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var outBar = new List<string>();
        var outDrawer = new List<string>();
        foreach (string id in bar)
            if (id != null && All.Contains(id) && seen.Add(id))
                outBar.Add(id);
        foreach (string id in drawer)
        {
            if (id == null || !All.Contains(id) || !seen.Add(id))
                continue;
            if (Launchers.Contains(id))
                outBar.Add(id);
            else
                outDrawer.Add(id);
        }
        if (!seen.Contains(Required))
            outBar.Add(Required);
        return (outBar, outDrawer);
    }

    // Quels radios rallumer en quittant le mode avion : ceux (clé = type + nom) qui étaient allumés avant et existent encore ;
    // à défaut, Wi-Fi et Bluetooth.
    internal static IReadOnlyList<T> RadiosToRestore<T>(IReadOnlyCollection<string>? remembered, IEnumerable<T> all, Func<T, string> key, Func<T, bool> isFallback)
    {
        var list = all.ToList();
        var matched = remembered != null && remembered.Count > 0 ? list.Where(r => remembered.Contains(key(r))).ToList() : new List<T>();
        return matched.Count > 0 ? matched : list.Where(isFallback).ToList();
    }
}
