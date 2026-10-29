// One-time removal of the "Winlestia Fichiers" dock pin that FilesDefaultHandler.Migrate used to add (the app now has its own themed button in the dock's system zone).
// Pure on purpose (lists in, ids out) so a test can run it on an in-memory Preferences. Idempotent through Preferences.FilesPinRemoved; other pins are never touched.
using System;
using System.Collections.Generic;
using System.Linq;

namespace PanneauxGlass;

internal static class FilesPinMigration {
    internal const string EntryName = "Winlestia Fichiers";

    // done = Preferences.FilesPinRemoved. Returns true when the state changed (caller saves). removedEntryIds = Custom entries dropped (caller also drops them from Store.Entries).
    internal static bool Apply(ref bool done, List<string> pins, List<Entry> custom, string exe, List<string> removedEntryIds) {
        if (done) return false;
        done = true;
        string id = "dock:" + exe;
        int removed = pins.RemoveAll(p => string.Equals(p, exe, StringComparison.OrdinalIgnoreCase) || string.Equals(p, id, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) {
            // Only the entry the old migration created (same id, name and target), and only while no remaining pin refers to it.
            foreach (var e in custom.Where(e => e.Id == id && e.Name == EntryName && string.Equals(e.Target, exe, StringComparison.OrdinalIgnoreCase)).ToList()) {
                if (pins.Contains(e.Id)) continue;
                custom.Remove(e); removedEntryIds.Add(e.Id);
            }
        }
        return true;
    }
}
