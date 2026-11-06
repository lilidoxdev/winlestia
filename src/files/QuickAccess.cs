// Quick access list model: the pinned paths (folders and files) with an optional label, in the user's order, persisted in settings.json.
// Every operation edits the LIST only, never the target on disk. Each one saves and raises Changed (the sidebar rebuilds its section).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WinlestiaFiles;

internal enum TargetKind { Unknown, Folder, File, Missing }

internal static class QuickAccess {
    internal static event Action? Changed;

    internal static List<QuickEntry> Entries => FilesSettings.Data.QuickAccess ??= new List<QuickEntry>();
    internal static bool Initialized => FilesSettings.Data.QuickAccess != null;

    static string Key(string path) => path.TrimEnd('\\', '/');
    internal static int IndexOf(string path) {
        var list = FilesSettings.Data.QuickAccess; if (list == null) return -1;
        string key = Key(path);
        return list.FindIndex(q => string.Equals(Key(q.Path), key, StringComparison.OrdinalIgnoreCase));
    }
    internal static bool IsPinned(string path) => IndexOf(path) >= 0;

    static void Commit() { FilesSettings.Save(); Changed?.Invoke(); }

    // First launch (no "quickAccess" key): the folders the sidebar showed so far, same order.
    internal static void InitIfNew(IEnumerable<string> paths) {
        if (Initialized) return;
        FilesSettings.Data.QuickAccess = paths.Select(p => new QuickEntry { Path = p }).ToList();
        FilesSettings.Save();
    }

    internal static bool Pin(string path) {
        if (string.IsNullOrWhiteSpace(path) || IsPinned(path)) return false;
        Entries.Add(new QuickEntry { Path = path }); Commit(); return true;
    }
    internal static bool Unpin(string path) { int i = IndexOf(path); return i >= 0 && RemoveAt(i); }
    internal static bool RemoveAt(int i) { if (i < 0 || i >= Entries.Count) return false; Entries.RemoveAt(i); Commit(); return true; }

    internal static bool MoveTo(int from, int to) {
        var list = Entries;
        if (from < 0 || from >= list.Count || to < 0 || to >= list.Count || from == to) return false;
        var q = list[from]; list.RemoveAt(from); list.Insert(to, q); Commit(); return true;
    }
    internal static bool Move(int i, int delta) => MoveTo(i, i + delta);

    // Empty / blank = back to the default name.
    internal static void SetLabel(int i, string? label) {
        if (i < 0 || i >= Entries.Count) return;
        label = label?.Trim();
        Entries[i].Label = string.IsNullOrEmpty(label) ? null : label; Commit();
    }

    // "Retrouver la cible": the path changes, label and position stay.
    internal static void Replace(int i, string newPath) {
        if (i < 0 || i >= Entries.Count || string.IsNullOrWhiteSpace(newPath)) return;
        Entries[i].Path = newPath; Commit();
    }

    // Existence check for the UI: runs on the pool and gives up after the timeout (Directory.Exists can block for a long time on a disconnected network drive); a timeout counts as missing.
    internal static async Task<TargetKind> Probe(string path, int timeoutMs = 2500) {
        var work = Task.Run(() => {
            try { if (Directory.Exists(path)) return TargetKind.Folder; if (File.Exists(path)) return TargetKind.File; } catch { }
            return TargetKind.Missing;
        });
        var first = await Task.WhenAny(work, Task.Delay(timeoutMs));
        return first == work ? work.Result : TargetKind.Missing;
    }
}
