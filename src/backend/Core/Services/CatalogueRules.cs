// language: C# 12, file: CatalogueRules.cs, target: Windows 11 / .NET 8, décisions pures du catalogue d'applications
using System;
using System.Collections.Generic;
using System.IO;

namespace PanneauxGlass;

// Isolées du service pour être testables sans Store, sans disque réel ni dispatcher.
internal static class CatalogueRules
{
    // Seules les cibles absolues avec extension sont jugées : un URI, un "shell:" ou un chemin relatif ne prouve jamais l'absence.
    internal static bool TargetGone(string target)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(target)
                || target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                || target.Contains("://")
                || target.StartsWith(@"\\", StringComparison.Ordinal)
                || !Path.IsPathRooted(target)
                || Path.GetExtension(target).Length == 0)
                return false;
            return !File.Exists(target) && !Directory.Exists(target);
        }
        catch { return false; }
    }
    // Le doublon se juge aussi parmi les entrées retenues dans le même lot : la surveillance peut voir deux fois le même raccourci.
    internal static List<Entry> NewEntries(IEnumerable<Entry> existing, IEnumerable<Entry> found)
    {
        var known = new List<Entry>(existing);
        var added = new List<Entry>();
        foreach (var entry in found)
        {
            if (known.Exists(x => x.Id == entry.Id || (x.Section == entry.Section && x.Name.Equals(entry.Name,
                StringComparison.OrdinalIgnoreCase))))
                continue;
            known.Add(entry);
            added.Add(entry);
        }
        return added;
    }
    // Un dossier supprimé emporte ses descendants ; le séparateur final évite de confondre "C:\A" et "C:\AB".
    internal static bool IsUnderDeleted(string target, IEnumerable<string> deleted)
    {
        foreach (var d in deleted)
            if (target.Equals(d, StringComparison.OrdinalIgnoreCase)
                || target.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
    // -1 = lecture impossible ou première mesure : aucune conclusion tant qu'il n'y a pas deux mesures valides.
    internal static bool PackagesChanged(int previous, int current) => previous >= 0 && current >= 0 && current != previous;
    internal static bool NeedsInventory(bool packagesChanged, bool missingChanged, int missingCount)
        => packagesChanged || (missingChanged && missingCount > 0);
}
