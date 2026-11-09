// Un onglet : son propre historique, ses éléments et son annulation. L'énumération tourne sur un thread du pool, les 256 premiers
// éléments s'affichent d'un coup, puis des ajouts temporisés,
// marshalés par Dispatcher.InvokeAsync en priorité Background ; chaque action postée revérifie le jeton.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace WinlestiaFiles;

internal enum LoadState
{
    Loading, Ready, Empty, Denied, NotFound, Failed
}
// Ce que la fenêtre doit conserver lors d'une réécriture des lignes visibles : sélection + ancre de défilement (lignes
// ajoutées/retirées), sélection + décalage (retri), sélection seule (filtre).
internal enum Keep
{
    Anchor, Offset, Selection
}

internal sealed partial class FolderTab
{
    internal static bool ShowHidden;
    const int Batch = 256, FirstFlushMs = 80, FlushMs = 150;
    readonly Dispatcher ui = Application.Current.Dispatcher;
    readonly List<string> history = new();
    int index = -1, sortGeneration;
    List<FileItem> all = new();
    CancellationTokenSource? cts;
    bool disposed, firstLogged;
    readonly RangeObservableCollection<FileItem> items = new();

    internal RangeObservableCollection<FileItem> Items => items;
    internal string Location { get; private set; } = "";
    internal LoadState State
    {
        get; private set;
    }
    internal string Error { get; private set; } = "";
    internal SortSpec Sort { get; private set; } = FilesSettings.InitialSort();
    internal event Action<FolderTab>? Changed;
    // Levé autour de chaque réécriture sur place des lignes (début = le type, fin = null) pour que la fenêtre restaure sélection et
    // défilement par chemin.
    internal event Action<FolderTab, Keep?>? Mutating;
    // Avis visible par l'utilisateur (dossier supprimé : remonté au parent).
    internal event Action<FolderTab, string>? Notice;
    // Les onglets inactifs ne sont pas surveillés : ils sont marqués périmés et resynchronisés (hors thread, en incrémental) quand
    // on les réaffiche.
    internal bool Stale;
    internal int Total => all.Count;
    internal void SyncIfStale()
    {
        if (!Stale)
            return;
        Stale = false;
        Sync(Array.Empty<string>(), true);
    }
    string filter = "";
    internal string Filter => filter;
    internal bool FilterEmpty => filter.Length > 0 && items.Count == 0 && !Searching && State is LoadState.Ready or LoadState.Empty;
    // Posé dès que les premières lignes (ou le résultat final, ou un échec) ont été mises en file vers l'UI : permet à la fenêtre
    // de les appliquer avant sa toute première passe de mise en page, au lieu de construire la liste vide puis de la mettre en page une
    // seconde fois.
    internal readonly ManualResetEventSlim FirstPosted = new(false);

    internal bool CanBack => index > 0;
    internal bool CanForward => index < history.Count - 1;
    internal string? Parent => Location.Length == 0 || ThisPc.Is(Location) ? null : Path.GetDirectoryName(Location) ?? ThisPc.Path;
    internal string Title => ThisPc.Is(Location) ? ThisPc.Label : Path.GetFileName(Location) is { Length: > 0 } leaf ? leaf : Location;

    internal void Go(string path)
    {
        if (disposed)
            return;
        if (index >= 0 && string.Equals(history[index], path, StringComparison.OrdinalIgnoreCase))
        {
            Reload();
            return;
        }
        if (index < history.Count - 1)
            history.RemoveRange(index + 1, history.Count - index - 1);
        history.Add(path);
        index = history.Count - 1;
        Load(path);
    }
    internal void Back()
    {
        if (CanBack && !disposed)
        {
            index--;
            Load(history[index]);
        }
    }
    internal void Forward()
    {
        if (CanForward && !disposed)
        {
            index++;
            Load(history[index]);
        }
    }
    internal void Up()
    {
        if (Parent is { } parent)
            Go(parent);
    }
    internal void Reload()
    {
        if (!disposed && Location.Length > 0)
            Load(Location);
    }

    internal void SetSort(SortColumn column) => SetSort(Sort with
    {
        Column = column,
        Ascending = Sort.Column == column ? !Sort.Ascending
        : true
    });
    internal void SetSort(SortSpec spec)
    {
        if (Sort == spec)
            return;
        Sort = spec;
        Changed?.Invoke(this);
        if (State == LoadState.Loading)
            return;               // Complete() applies the final sort once
        StartSort();
    }

    bool sorting;
    void StartSort()
    {
        var source = all;
        var spec = Sort;
        int mine = ++sortGeneration;
        sorting = true;
        Task.Run(() =>
        {
            var sorted = FileSort.Sorted(source, spec);
            ui.InvokeAsync(() =>
            {
                if (mine != sortGeneration || !ReferenceEquals(source, all) || disposed)
                    return;
                sorting = false;
                all = sorted;
                if (deepMode)
                    deep = FileSort.Sorted(deep, Sort);
                Mutating?.Invoke(this, Keep.Offset);
                try
                {
                    items.Reset(Visible());
                }
                finally { Mutating?.Invoke(this, null); }
                Changed?.Invoke(this);
            },
                DispatcherPriority.Background);
        });
    }

    // Mise à jour sur place après une opération de fichier (sans énumération) : retire les chemins supprimés, insère les éléments
    // ajoutés à leur position triée.
    // Peu de changements = notifications Remove/Insert unitaires (la sélection des autres lignes reste intacte) ; un gros lot = une
    // fusion linéaire + un Reset.
    internal void ApplyChanges(ICollection<string> removed, List<FileItem> added)
    {
        if (disposed || State is LoadState.Loading or LoadState.Denied or LoadState.NotFound or LoadState.Failed)
            return;
        const int Bulk = 20;
        bool touched = false, reset = false, mirror = filter.Length == 0;
        Mutating?.Invoke(this, Keep.Anchor);
        try
        {
            if (deepMode && removed.Count > 0)
            {
                var drop = new HashSet<string>(removed, StringComparer.OrdinalIgnoreCase);
                if (deep.RemoveAll(i => drop.Contains(i.FullPath)) > 0)
                {
                    touched = true;
                    reset = true;
                }
            }
            if (removed.Count > 0)
            {
                if (removed.Count > Bulk)
                {
                    var drop = new HashSet<string>(removed, StringComparer.OrdinalIgnoreCase);
                    int before = all.Count;
                    var kept = all.FindAll(i => !drop.Contains(i.FullPath));
                    if (kept.Count != before)
                    {
                        all = kept;
                        touched = true;
                        reset = true;
                    }
                }
                else
                {
                    foreach (var path in removed)
                    {
                        int at = all.FindIndex(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
                        if (at < 0)
                            continue;
                        all.RemoveAt(at);
                        if (mirror && !reset)
                            items.RemoveAt(at);
                        touched = true;
                    }
                }
            }
            if (added.Count > 0)
            {
                var cmp = FileSort.Comparer(Sort);
                if (added.Count > Bulk)
                {
                    var have = new HashSet<string>(all.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);
                    var fresh = added.Where(i => have.Add(i.FullPath)).ToList();
                    fresh.Sort(cmp);
                    if (fresh.Count > 0)
                    {
                        var merged = new List<FileItem>(all.Count + fresh.Count);
                        int a = 0, b = 0;
                        while (a < all.Count || b < fresh.Count)
                            merged.Add(b >= fresh.Count || (a < all.Count && cmp(all[a], fresh[b]) <= 0) ? all[a++] : fresh[b++]);
                        all = merged;
                        touched = true;
                        reset = true;
                    }
                }
                else
                {
                    foreach (var item in added)
                    {
                        if (all.Exists(i => string.Equals(i.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        int lo = 0, hi = all.Count;
                        while (lo < hi)
                        {
                            int mid = (lo + hi) >> 1;
                            if (cmp(all[mid], item) < 0)
                                lo = mid + 1;
                            else
                                hi = mid;
                        }
                        all.Insert(lo, item);
                        if (mirror && !reset)
                            items.Insert(lo, item);
                        touched = true;
                    }
                }
            }
            if (!touched)
                return;
            if (reset || !mirror)
                items.Reset(Visible());          // changement en masse ou vue filtrée : une seule réécriture des lignes affichées
        }
        finally { Mutating?.Invoke(this, null); }
        if (!touched)
            return;
        State = all.Count == 0 ? LoadState.Empty : LoadState.Ready;
        if (sorting)
            StartSort();                              // un tri en cours travaillait sur un instantané périmé
        Changed?.Invoke(this);
    }

    internal FileItem? Find(string path) => all.Find(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
    internal void RaiseCut(HashSet<string> before, HashSet<string> after)
    {
        foreach (var item in all)
            if (before.Contains(item.FullPath) || after.Contains(item.FullPath))
                item.RaiseCut();
    }

    void Load(string path)
    {
        StopDeep();
        deepMode = false;
        deep = new List<FileItem>();
        cts?.Cancel();
        cts?.Dispose();
        var mine = cts = new CancellationTokenSource();
        Location = path;
        State = LoadState.Loading;
        Error = "";
        filter = "";
        dirty = false;
        syncPaths.Clear();
        syncFull = false;
        Stale = false;
        firstLogged = false;
        sortGeneration++;
        all = new List<FileItem>();
        if (items.Count > 0)
            items.Reset(null);
        Perf.Stall?.Reset();
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        bool hidden = ShowHidden;
        var token = mine.Token;
        Changed?.Invoke(this);
        Perf.Mark("enumeration start");
        FirstPosted.Reset();
        if (ThisPc.Is(path))
        {
            State = LoadState.Ready;
            FirstPosted.Set();
            Changed?.Invoke(this);
            return;
        }   // page virtuelle : les lecteurs sont listés par la fenêtre
        Task.Run(() => Run(token, path, hidden, start));
    }

    void Post(CancellationToken token, Action action) =>
        ui.InvokeAsync(() => { if (!token.IsCancellationRequested) action(); }, DispatcherPriority.Background);

    void Run(CancellationToken token, string path, bool hidden, long start)
    {
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = hidden ? 0 : FileAttributes.Hidden | FileAttributes.System
            };
            var everything = new List<FileItem>();
            var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // L'UI voit les premières lignes le plus tôt possible (premier lot complet, ou après FirstFlushMs sur un disque lent), puis
            // au plus un ajout par FlushMs tant que l'énumération continue. Tout le reste attend l'unique Reset trié de Complete() :
            // 10k éléments coûtent 2 notifications de collection au lieu de ~40, et le tri ne tourne jamais sur le thread UI.
            int posted = 0;
            long lastFlush = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
            {
                if (token.IsCancellationRequested)
                    return;
                everything.Add(FileItem.From(entry, types));
                int pending = everything.Count - posted;
                if (posted == 0 ? pending >= Batch || Perf.Ms(lastFlush) >= FirstFlushMs : Perf.Ms(lastFlush) >= FlushMs)
                {
                    var slice = everything.GetRange(posted, pending);
                    if (posted == 0)
                        slice = FileSort.Sorted(slice, Sort);
                    posted = everything.Count;
                    lastFlush = System.Diagnostics.Stopwatch.GetTimestamp();
                    Post(token, () => AddBatch(slice, start));
                    FirstPosted.Set();
                }
            }
            // IgnoreInaccessible avale aussi une racine refusée : on relit une fois avec les options par défaut pour distinguer vide et
            // refusé.
            if (everything.Count == 0)
            {
                using var probe = new DirectoryInfo(path).EnumerateFileSystemInfos().GetEnumerator();
                probe.MoveNext();
            }
            if (Perf.Enabled)
                Perf.Log($"nav {path}: enumeration complete {Perf.Ms(start):F1} ms ({everything.Count} items)");
            var sorted = FileSort.Sorted(everything, Sort);
            if (token.IsCancellationRequested)
                return;
            Post(token, () => Complete(sorted, start));
            FirstPosted.Set();
        }
        catch (OperationCanceledException)
        {
            // Énumération annulée par la navigation : résultat partiel jeté.
        }
        catch (Exception ex)
        {
            Post(token, () => Fail(ex));
            FirstPosted.Set();
        }
    }

    void AddBatch(List<FileItem> batch, long start)
    {
        items.AddRange(batch);
        LogFirstRender(start);
        Changed?.Invoke(this);
    }

    void Complete(List<FileItem> sorted, long start)
    {
        all = sorted;
        items.Reset(sorted);
        State = sorted.Count == 0 ? LoadState.Empty : LoadState.Ready;
        if (dirty)
        {
            dirty = false;
            Sync(Array.Empty<string>(), true);
        }   // quelque chose a changé pendant ce chargement
        LogFirstRender(start);
        Changed?.Invoke(this);
        if (Perf.Enabled)
        {
            string where = Location;
            int count = sorted.Count;
            AfterRender(()
                => Perf.Log($"nav {where}: complete rendered {Perf.Ms(start):F1} ms ({count} items), {Perf.SinceProcess():F0} ms since " +
                $"process start, max UI stall {Perf.Stall?.Max ?? 0:F1} ms"));
        }
    }

    void Fail(Exception ex)
    {
        items.Reset(null);
        all = new List<FileItem>();
        State = ex is UnauthorizedAccessException ? LoadState.Denied : ex is DirectoryNotFoundException or FileNotFoundException
            ? LoadState.NotFound : LoadState.Failed;
        Error = ex.Message;
        Changed?.Invoke(this);
    }

    // La priorité Loaded passe après la passe de mise en page/rendu en priorité Render que le lot vient d'invalider.
    void LogFirstRender(long start)
    {
        if (!Perf.Enabled || firstLogged || items.Count == 0)
            return;
        firstLogged = true;
        string where = Location;
        int count = items.Count;
        AfterRender(()
            => Perf.Log($"nav {where}: first rows rendered {Perf.Ms(start):F1} ms ({count} items in first update), " +
            $"{Perf.SinceProcess():F0} ms since process start"));
    }

    // Mesure uniquement : les lignes appliquées avant la première image de la fenêtre comptent comme rendues à ContentRendered, les
    // suivantes après la passe de rendu suivante.
    void AfterRender(Action log)
    {
        if (Perf.FirstFrameDone)
            ui.InvokeAsync(log, DispatcherPriority.Loaded);
        else
            Perf.BeforeFirstFrame += log;
    }

    internal void Dispose()
    {
        disposed = true;
        StopDeep();
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
        items.Reset(null);
        all = new List<FileItem>();
        Changed = null;
    }
}
