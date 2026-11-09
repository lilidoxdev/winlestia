// Onglet : synchronisation automatique sur notification de changement du dossier
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
namespace WinlestiaFiles;

internal sealed partial class FolderTab
{
    // ---- auto refresh (folder change notifications) -----------------------------------------------------------------

    bool syncRunning, syncFull, dirty;
    readonly HashSet<string> syncPaths = new(StringComparer.OrdinalIgnoreCase);

    // Chemins modifiés du dossier affiché (full = réanalyser le dossier et comparer). Thread UI. Ne touche jamais au disque ici :
    // lecture/énumération sur un thread du pool,
    // le résultat est appliqué sur place via ApplyChanges (que les opérations de fichiers utilisent aussi, donc les deux sources
    // convergent vers les mêmes lignes).
    internal void Sync(ICollection<string> paths, bool full)
    {
        if (disposed)
            return;
        if (State == LoadState.Loading)
        {
            dirty = true;
            return;
        }       // applied by one rescan right after the load
        if (State is LoadState.Denied or LoadState.NotFound or LoadState.Failed && !full)
            return;
        foreach (var p in paths)
            syncPaths.Add(p);
        syncFull |= full;
        if (syncRunning)
            return;
        syncRunning = true;
        var batch = syncPaths.ToList();
        bool whole = syncFull || batch.Count > 500;
        syncPaths.Clear();
        syncFull = false;
        string loc = Location;
        bool hidden = ShowHidden;
        var known = whole ? all.ToArray() : Array.Empty<FileItem>();
        var visibleCheck = whole ? null : batch.Select(p => Find(p)).ToArray();
        Task.Run(() =>
        {
            List<string>? gone = null;
            List<FileItem>? added = null;
            string? parent = null;
            bool lost = false;
            try
            {
                lost = !Directory.Exists(loc);
                if (lost)
                {
                    parent = Path.GetDirectoryName(loc);
                    while (parent != null && !Directory.Exists(parent))
                        parent = Path.GetDirectoryName(parent);
                }
                else
                {
                    var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    gone = new List<string>();
                    added = new List<FileItem>();
                    if (whole)
                    {
                        var options = new EnumerationOptions
                        {
                            IgnoreInaccessible = true,
                            RecurseSubdirectories = false,
                            ReturnSpecialDirectories = false,
                            AttributesToSkip = hidden ? 0 : FileAttributes.Hidden | FileAttributes.System
                        };
                        var fresh = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
                        foreach (var e in new DirectoryInfo(loc).EnumerateFileSystemInfos("*", options))
                        {
                            var f = FileItem.From(e, types);
                            fresh[f.FullPath] = f;
                        }
                        var old = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
                        foreach (var k in known)
                            old[k.FullPath] = k;
                        foreach (var k in known)
                            if (!fresh.ContainsKey(k.FullPath))
                                gone.Add(k.FullPath);
                        foreach (var f in fresh.Values)
                            if (!old.TryGetValue(f.FullPath, out var k) || Differs(k, f))
                            {
                                if (k != null)
                                    gone.Add(f.FullPath);
                                added.Add(f);
                            }
                    }
                    else
                    {
                        for (int i = 0; i < batch.Count; i++)
                        {
                            var now = FileItem.Probe(batch[i], types);
                            if (now != null && !hidden
                                && (File.GetAttributes(batch[i]) & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                                now = null;
                            var was = visibleCheck![i];
                            if (now == null)
                            {
                                if (was != null)
                                    gone.Add(batch[i]);
                            }
                            else if (was == null)
                                added.Add(now);
                            else if (Differs(was, now))
                            {
                                gone.Add(batch[i]);
                                added.Add(now);
                            }
                        }
                    }
                }
            }
            catch { gone = null; added = null; lost = false; }
            ui.InvokeAsync(() =>
            {
                syncRunning = false;
                if (disposed || !string.Equals(loc, Location, StringComparison.OrdinalIgnoreCase))
                    return;
                if (lost)
                {
                    if (parent != null)
                    {
                        Notice?.Invoke(this, "Dossier supprimé ou renommé : retour au dossier parent");
                        Go(parent);
                    }
                    else
                        Fail(new DirectoryNotFoundException());
                    return;
                }
                if (State == LoadState.Loading)
                    dirty = true;
                else if (gone != null && added != null && (gone.Count > 0 || added.Count > 0))
                    ApplyChanges(gone, added);
                if (syncPaths.Count > 0 || syncFull)
                    Sync(Array.Empty<string>(), false);
            }, DispatcherPriority.Background);
        });
    }

    static bool Differs(FileItem a, FileItem b) => a.Size != b.Size || a.Modified != b.Modified || a.IsDir != b.IsDir;
}
