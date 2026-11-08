// C# 12 / WallpaperLogic.cs / Utilitaires purs du sélecteur de fond d'écran : noms de processus des fonds animés et libellés d'écran.
// Aucune E/S, aucune API Windows.
using System;
using System.Collections.Generic;
using System.Linq;

namespace PanneauxGlass;

internal static class WallpaperLogic
{
    // Nom d'exécutable (minuscules, avec .exe) -> nom du produit. Noms de processus uniquement ; aucune fenêtre ni contenu n'est lu.
    // Ajouter une ligne pour couvrir un autre produit.
    internal static readonly (string exe, string label)[] AnimatedHosts = {
        ("desktophut studio.exe", "DesktopHut"),
        ("wallpaper32.exe", "Wallpaper Engine"), ("wallpaper64.exe", "Wallpaper Engine"),
        ("lively.exe", "Lively Wallpaper"), ("livelywpf.exe", "Lively Wallpaper"),
        ("wallpaper.service.exe", "Wallpaper X"), ("wallpaper x launcher.exe", "Wallpaper X"),
    };

    // Les noms de processus peuvent venir avec ou sans ".exe" et dans n'importe quelle casse (Process.ProcessName n'a pas d'extension).
    internal static string[] Animated(IEnumerable<string?> processNames)
    {
        var seen = new HashSet<string>(processNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => Normalize(n!)),
            StringComparer.Ordinal);
        return AnimatedHosts.Where(h => seen.Contains(h.exe)).Select(h => h.label).Distinct().ToArray();
    }

    static string Normalize(string name)
    {
        name = name.Trim().ToLowerInvariant();
        return name.EndsWith(".exe", StringComparison.Ordinal) ? name : name + ".exe";
    }

    // Libellé neutre pour la CLI ; le sélecteur construit son propre libellé traduit à partir des mêmes champs.
    internal static string DisplayName(int id, int width, int height, bool primary) => "Display " + id + " (" + width + "x" + height + ")"
        + (primary ? " primary" : "");
}
