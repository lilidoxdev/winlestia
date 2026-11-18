// language: C# 12, file: MediaSessionMatch.cs, target: Windows 11 / .NET 8
// Correspondance pure entre les sessions audio WASAPI et la source média sélectionnée dans le tableau de bord.
// Le volume par onglet est impossible : les sessions audio Windows sont par processus (un onglet de navigateur n'est pas une session), donc
// le volume et le visualiseur agissent toujours sur toutes les sessions audio du navigateur ou de l'application sélectionné.
using System;
using System.Collections.Generic;
using System.Linq;

namespace PanneauxGlass;

/// <summary>Une session audio telle que la voit le mélangeur. ExeStem peut être vide quand le chemin de l'image est illisible.</summary>
internal sealed record AudioSessionInfo(
    int Pid,
    string ExeStem,
    IReadOnlyList<string> AncestorExeStems,
    string SessionIdentifier
);

/// <summary>La source média résolue : radical de l'exécutable (peut être vide) et AUMID brut.</summary>
internal sealed record MediaTarget(string ExeStem, string Aumid);

internal static class MediaSessionMatch
{
    const int MaxAncestorDepth = 12;

    // Les processus génériques shell/hôte parentent de nombreuses applications sans rapport : ils n'identifient jamais une source média via
    // leurs ancêtres.
    static readonly HashSet<string> GenericAncestorStems = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "cmd",
        "powershell",
        "pwsh",
        "conhost",
        "svchost",
        "services",
        "wininit",
        "winlogon",
        "sihost",
        "runtimebroker",
        "dllhost",
        "applicationframehost",
        "windowsterminal",
        "wt",
        "taskhostw",
        "userinit",
    };

    internal static bool Matches(AudioSessionInfo session, MediaTarget target)
    {
        if (target.ExeStem.Length > 0)
        {
            if (session.ExeStem.Equals(target.ExeStem, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Firefox et Chromium jouent l'audio depuis un processus enfant : on remonte la chaîne des parents.
            bool ancestorMatches =
                !GenericAncestorStems.Contains(target.ExeStem)
                && session.AncestorExeStems.Any(ancestor =>
                    ancestor.Equals(target.ExeStem, StringComparison.OrdinalIgnoreCase)
                );
            if (ancestorMatches)
            {
                return true;
            }

            if (session.SessionIdentifier.Contains(target.ExeStem + ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string family = PackageFamily(target.Aumid);
        return family.Length >= 6 && session.SessionIdentifier.Contains(family, StringComparison.OrdinalIgnoreCase);
    }

    // Les AUMID de type hash n'apparaissent jamais dans les identifiants de session : seules les familles de paquets sont utiles ici.
    static string PackageFamily(string aumid)
    {
        bool unusable =
            aumid.Length == 0
            || MediaSourceIdentity.IsHashStyle(aumid)
            || aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        return unusable ? "" : aumid.Split('!')[0];
    }

    /// <summary>Radicaux d'exécutable de la chaîne de parents d'un processus, le parent le plus proche en premier.</summary>
    internal static IReadOnlyList<string> AncestorStems(int pid, Func<int, int> parentOf, Func<int, string> stemOf)
    {
        var stems = new List<string>();
        var seen = new HashSet<int> { pid };
        int current = parentOf(pid);
        while (current > 0 && stems.Count < MaxAncestorDepth && seen.Add(current))
        {
            stems.Add(stemOf(current));
            current = parentOf(current);
        }

        return stems;
    }

    /// <summary>
    /// Racine de l'arbre de processus correspondant : on remonte tant que le parent a le même radical d'exécutable que la cible.
    /// Capturer ce PID avec INCLUDE_TARGET_PROCESS_TREE couvre tous les processus enfants du navigateur.
    /// </summary>
    internal static int RootPid(int pid, string exeStem, Func<int, int> parentOf, Func<int, string> stemOf)
    {
        if (exeStem.Length == 0)
        {
            return pid;
        }

        int root = pid;
        var seen = new HashSet<int> { pid };
        for (int depth = 0; depth < MaxAncestorDepth; depth++)
        {
            int parent = parentOf(root);
            bool sameApp = parent > 0 && stemOf(parent).Equals(exeStem, StringComparison.OrdinalIgnoreCase);
            if (!sameApp || !seen.Add(parent))
            {
                break;
            }

            root = parent;
        }

        return root;
    }
}
