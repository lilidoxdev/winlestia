// language: C# 12, file: LaunchRules.cs, target: Windows 11 / .NET 8, décisions pures de lancement, recherche web et classement des accès
using System;
using System.IO;

namespace PanneauxGlass;

// Extraites de Coordinator.Boards/Dialogs/Palette : aucun processus n'est démarré ici, seules les cibles sont décidées.
internal static class LaunchRules
{
    // Laisse le temps à l'espace de se replier avant l'outil de capture d'écran de Windows, sinon il capturerait le panneau.
    internal const int ScreenClipDelayMs = 120;
    // L'« Explorateur de fichiers » Windows épinglé (lnk ou explorer.exe nu) ouvre Winlestia Fichiers quand il est le gestionnaire par
    // défaut.
    internal static bool IsPlainExplorer(string? target)
    {
        var name = Path.GetFileName(target ?? "");
        return name.Equals("File Explorer.lnk", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Explorateur de fichiers.lnk", StringComparison.OrdinalIgnoreCase)
            || name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
    }
    // Les cibles shell: ne sont pas des chemins : seul explorer.exe sait les résoudre.
    internal static bool IsShellTarget(string target) => target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);
    internal static bool IsScreenClip(string target) => target.StartsWith("ms-screenclip:", StringComparison.OrdinalIgnoreCase);
    // Une saisie sans schéma est une adresse à compléter ; sinon la saisie part en recherche DuckDuckGo.
    internal static string WebTarget(string value, bool address)
    {
        if (address && !value.Contains("://"))
            value = "https://" + value;
        return address ? value : "https://duckduckgo.com/?q=" + Uri.EscapeDataString(value);
    }
    // Rubrique d'affichage d'un accès ajouté à la main selon le panneau qui le reçoit.
    internal static string SectionFor(string board)
        => board switch
        {
            "apps" => "Applications",
            "games" => "Jeux",
            "projects" => "Projets",
            _ => "Dossiers"
        };
    internal static string SelectArguments(string target) => "/select,\"" + target + "\"";
}
