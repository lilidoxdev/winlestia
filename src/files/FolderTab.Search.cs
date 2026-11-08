// Onglet : filtre en mémoire et recherche dans les sous-dossiers
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
    // ---- filtre (en mémoire, sur les éléments déjà chargés ; les objets FileItem ne sont jamais reconstruits) -------------------

    static readonly System.Globalization.CompareInfo Compare = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
    const System.Globalization.CompareOptions Loose = System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions
        .IgnoreNonSpace | System.Globalization.CompareOptions.IgnoreWidth;

    List<FileItem> Visible() => deepMode ? deep : filter.Length == 0 ? all : all.FindAll(i => Compare.IndexOf(i.Name, filter, Loose) >= 0);

    internal void SetFilter(string text)
    {
        text = text.Trim();
        if (disposed || text == filter || State is LoadState.Loading or LoadState.Denied or LoadState.NotFound or LoadState.Failed)
            return;
        filter = text;
        RestartDeep();
        Mutating?.Invoke(this, Keep.Selection);
        try
        {
            items.Reset(Visible());
        }
        finally { Mutating?.Invoke(this, null); }
        Changed?.Invoke(this);
    }

    // ---- recherche dans les sous-dossiers (hors thread UI, annulable, résultats postés par lots) ---------------------------

    // La liste superficielle (`all`) n'est jamais touchée par une recherche profonde : effacer le filtre ou annuler la remet comme avant.
    List<FileItem> deep = new();
    bool deepOption, deepMode;
    CancellationTokenSource? deepCts;
    internal bool DeepOption => deepOption;
    internal bool DeepActive => deepMode;
    internal bool Searching
    {
        get; private set;
    }
    internal int DeepCount => deep.Count;

    internal void SetDeepOption(bool on)
    {
        if (deepOption == on || disposed)
            return;
        deepOption = on;
        if (filter.Length == 0)
            return;
        RestartDeep();
        Mutating?.Invoke(this, Keep.Selection);
        try
        {
            items.Reset(Visible());
        }
        finally { Mutating?.Invoke(this, null); }
        Changed?.Invoke(this);
    }

    // Échap : arrête le parcours, garde ce qui a été trouvé jusque-là.
    internal bool CancelDeep()
    {
        if (!Searching)
            return false;
        deepCts?.Cancel();
        Searching = false;
        Changed?.Invoke(this);
        return true;
    }

    void StopDeep()
    {
        deepCts?.Cancel();
        deepCts?.Dispose();
        deepCts = null;
        Searching = false;
    }

    void RestartDeep()
    {
        StopDeep();
        deep = new List<FileItem>();
        deepMode = deepOption && filter.Length > 0 && State is LoadState.Ready or LoadState.Empty && !ThisPc.Is(Location);
        if (!deepMode)
            return;
        var mine = deepCts = new CancellationTokenSource();
        var token = mine.Token;
        string root = Location, text = filter;
        bool hidden = ShowHidden;
        Searching = true;
        Task.Run(() => DeepRun(token, root, text, hidden));
    }

    void DeepRun(CancellationToken token, string root, string text, bool hidden)
    {
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true,
                ReturnSpecialDirectories = false, AttributesToSkip = hidden ? 0 : FileAttributes.Hidden | FileAttributes.System };
            var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var walk = new System.IO.Enumeration.FileSystemEnumerable<FileItem>(root, (ref System.IO.Enumeration.FileSystemEntry e) =>
            {
                string dir = e.Directory.ToString();
                string rel = Path.GetRelativePath(root, dir);
                if (rel == ".")
                    rel = "";
                var item = FileItem.From(ref e, types);
                item.Where = rel;
                return item;
            }, options)
            {
                ShouldIncludePredicate = (ref System.IO.Enumeration.FileSystemEntry e) => Compare.IndexOf(e.FileName.ToString(), text,
                    Loose) >= 0,
                ShouldRecursePredicate = (ref System.IO.Enumeration.FileSystemEntry e) => e.IsDirectory
                    && (e.Attributes & FileAttributes.ReparsePoint) == 0,
            };
            var pending = new List<FileItem>();
            long last = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (var item in walk)
            {
                if (token.IsCancellationRequested)
                    return;
                pending.Add(item);
                if (pending.Count >= 200 || Perf.Ms(last) >= 120)
                {
                    var slice = pending;
                    pending = new List<FileItem>();
                    last = System.Diagnostics.Stopwatch.GetTimestamp();
                    Post(token, () => DeepBatch(slice));
                }
            }
            var rest = pending;
            Post(token, () => DeepDone(rest));
        }
        catch (OperationCanceledException)
        {
            // Énumération annulée par la navigation : résultat partiel jeté.
        }
        catch (Exception ex) { Perf.Log("deep search: " + ex.Message); Post(token, () => DeepDone(new List<FileItem>())); }
    }

    void DeepBatch(List<FileItem> slice)
    {
        if (!deepMode)
            return;
        deep.AddRange(slice);
        items.AddRange(slice);
        Changed?.Invoke(this);
    }

    void DeepDone(List<FileItem> rest)
    {
        if (!deepMode)
            return;
        deep.AddRange(rest);
        Searching = false;
        deep = FileSort.Sorted(deep, Sort);
        Mutating?.Invoke(this, Keep.Selection);
        try
        {
            items.Reset(deep);
        }
        finally { Mutating?.Invoke(this, null); }
        Changed?.Invoke(this);
    }
}
