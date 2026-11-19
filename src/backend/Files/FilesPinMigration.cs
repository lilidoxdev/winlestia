// Retrait unique de l'épingle « Winlestia Fichiers » du dock que FilesDefaultHandler.Migrate ajoutait autrefois (l'application a
// maintenant son propre bouton thémé dans la zone système du dock).
// Pur volontairement (listes en entrée, identifiants en sortie) pour qu'un test puisse l'exécuter sur des Preferences en mémoire.
// Idempotent grâce à Preferences.FilesPinRemoved ; les autres épingles ne sont jamais touchées.
using System;
using System.Collections.Generic;
using System.Linq;

namespace PanneauxGlass;

internal static class FilesPinMigration
{
    internal const string EntryName = "Winlestia Fichiers";

    // done = Preferences.FilesPinRemoved. Renvoie true quand l'état a changé (l'appelant sauve). removedEntryIds = entrées Custom
    // supprimées (l'appelant les retire aussi de Store.Entries).
    internal static bool Apply(ref bool done, List<string> pins, List<Entry> custom, string exe, List<string> removedEntryIds)
    {
        if (done)
            return false;
        done = true;
        string id = "dock:" + exe;
        int removed = pins.RemoveAll(p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase) || string.Equals(p, id,
            StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            // Uniquement l'entrée créée par l'ancienne migration (même id, même nom, même cible), et seulement tant qu'aucune
            // épingle restante n'y fait référence.
            foreach (var e in custom.Where(e => e.Id == id && e.Name == EntryName && string.Equals(e.Target, exe,
                StringComparison.OrdinalIgnoreCase)).ToList())
            {
                if (pins.Contains(e.Id))
                    continue;
                custom.Remove(e);
                removedEntryIds.Add(e.Id);
            }
        }
        return true;
    }
}
