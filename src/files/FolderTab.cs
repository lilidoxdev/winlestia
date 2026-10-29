// One tab: its own history, items and cancellation. Enumeration runs on a pool thread, first 256 items shown at once, then timed appends,
// marshalled with Dispatcher.InvokeAsync at Background priority; every posted action re-checks the token.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace WinlestiaFiles;

internal enum LoadState { Loading, Ready, Empty, Denied, NotFound, Failed }
// What the window must carry across a rewrite of the visible rows: selection + scroll anchor (rows added/removed), selection + offset (re-sort), selection only (filter).
internal enum Keep { Anchor, Offset, Selection }

internal sealed class FolderTab {
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
    internal LoadState State { get; private set; }
    internal string Error { get; private set; } = "";
    internal SortSpec Sort { get; private set; } = new(SortColumn.Name, true);
    internal event Action<FolderTab>? Changed;
    // Raised around every in-place rewrite of the rows (begin = the kind, end = null) so the window can restore selection and scroll by path.
    internal event Action<FolderTab, Keep?>? Mutating;
    // User-visible notice (folder removed: moved to a parent).
    internal event Action<FolderTab, string>? Notice;
    // Inactive tabs are not watched: they are marked stale and re-synced (off thread, incremental) when shown again.
    internal bool Stale;
    internal int Total => all.Count;
    internal void SyncIfStale() { if (!Stale) return; Stale = false; Sync(Array.Empty<string>(), true); }
    string filter = "";
    internal string Filter => filter;
    internal bool FilterEmpty => filter.Length > 0 && items.Count == 0 && State is LoadState.Ready or LoadState.Empty;
    // Set once the first rows (or the final result, or a failure) have been queued to the UI: lets the window apply them
    // before its very first layout pass instead of building the list empty and laying it out a second time.
    internal readonly ManualResetEventSlim FirstPosted = new(false);

    internal bool CanBack => index > 0;
    internal bool CanForward => index < history.Count - 1;
    internal string? Parent => Location.Length == 0 ? null : Path.GetDirectoryName(Location);
    internal string Title => Path.GetFileName(Location) is { Length: > 0 } leaf ? leaf : Location;

    internal void Go(string path) {
        if (disposed) return;
        if (index >= 0 && string.Equals(history[index], path, StringComparison.OrdinalIgnoreCase)) { Reload(); return; }
        if (index < history.Count - 1) history.RemoveRange(index + 1, history.Count - index - 1);
        history.Add(path); index = history.Count - 1;
        Load(path);
    }
    internal void Back() { if (CanBack && !disposed) { index--; Load(history[index]); } }
    internal void Forward() { if (CanForward && !disposed) { index++; Load(history[index]); } }
    internal void Up() { if (Parent is { } parent) Go(parent); }
    internal void Reload() { if (!disposed && Location.Length > 0) Load(Location); }

    internal void SetSort(SortColumn column) {
        Sort = new SortSpec(column, Sort.Column == column ? !Sort.Ascending : true);
        Changed?.Invoke(this);
        if (State == LoadState.Loading) return;               // Complete() applies the final sort once
        StartSort();
    }

    bool sorting;
    void StartSort() {
        var source = all; var spec = Sort; int mine = ++sortGeneration; sorting = true;
        Task.Run(() => {
            var sorted = FileSort.Sorted(source, spec);
            ui.InvokeAsync(() => { if (mine != sortGeneration || !ReferenceEquals(source, all) || disposed) return; sorting = false; all = sorted; Mutating?.Invoke(this, Keep.Offset); try { items.Reset(Visible()); } finally { Mutating?.Invoke(this, null); } Changed?.Invoke(this); }, DispatcherPriority.Background);
        });
    }

    // In-place update after a file operation (no enumeration): drops the removed paths, inserts the added items at their sorted position.
    // Few changes are single Remove/Insert notifications (selection of the other rows untouched); a big batch is one linear merge + one Reset.
    internal void ApplyChanges(ICollection<string> removed, List<FileItem> added) {
        if (disposed || State is LoadState.Loading or LoadState.Denied or LoadState.NotFound or LoadState.Failed) return;
        const int Bulk = 20;
        bool touched = false, reset = false, mirror = filter.Length == 0;
        Mutating?.Invoke(this, Keep.Anchor);
        try {
        if (removed.Count > 0) {
            if (removed.Count > Bulk) {
                var drop = new HashSet<string>(removed, StringComparer.OrdinalIgnoreCase);
                int before = all.Count;
                var kept = all.FindAll(i => !drop.Contains(i.FullPath));
                if (kept.Count != before) { all = kept; touched = true; reset = true; }
            } else {
                foreach (var path in removed) {
                    int at = all.FindIndex(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
                    if (at < 0) continue;
                    all.RemoveAt(at); if (mirror && !reset) items.RemoveAt(at); touched = true;
                }
            }
        }
        if (added.Count > 0) {
            var cmp = FileSort.Comparer(Sort);
            if (added.Count > Bulk) {
                var have = new HashSet<string>(all.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);
                var fresh = added.Where(i => have.Add(i.FullPath)).ToList();
                fresh.Sort(cmp);
                if (fresh.Count > 0) {
                    var merged = new List<FileItem>(all.Count + fresh.Count);
                    int a = 0, b = 0;
                    while (a < all.Count || b < fresh.Count)
                        merged.Add(b >= fresh.Count || (a < all.Count && cmp(all[a], fresh[b]) <= 0) ? all[a++] : fresh[b++]);
                    all = merged; touched = true; reset = true;
                }
            } else {
                foreach (var item in added) {
                    if (all.Exists(i => string.Equals(i.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase))) continue;
                    int lo = 0, hi = all.Count;
                    while (lo < hi) { int mid = (lo + hi) >> 1; if (cmp(all[mid], item) < 0) lo = mid + 1; else hi = mid; }
                    all.Insert(lo, item); if (mirror && !reset) items.Insert(lo, item); touched = true;
                }
            }
        }
        if (!touched) return;
        if (reset || !mirror) items.Reset(Visible());          // bulk change or filtered view: one rewrite of the shown rows
        } finally { Mutating?.Invoke(this, null); }
        if (!touched) return;
        State = all.Count == 0 ? LoadState.Empty : LoadState.Ready;
        if (sorting) StartSort();                              // an in-flight sort worked on a stale snapshot
        Changed?.Invoke(this);
    }

    // ---- filter (in memory, over the already loaded items; the FileItem objects are never rebuilt) -------------------

    static readonly System.Globalization.CompareInfo Compare = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;
    const System.Globalization.CompareOptions Loose = System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace | System.Globalization.CompareOptions.IgnoreWidth;

    List<FileItem> Visible() => filter.Length == 0 ? all : all.FindAll(i => Compare.IndexOf(i.Name, filter, Loose) >= 0);

    internal void SetFilter(string text) {
        text = text.Trim();
        if (disposed || text == filter || State is LoadState.Loading or LoadState.Denied or LoadState.NotFound or LoadState.Failed) return;
        filter = text;
        Mutating?.Invoke(this, Keep.Selection);
        try { items.Reset(Visible()); } finally { Mutating?.Invoke(this, null); }
        Changed?.Invoke(this);
    }

    // ---- auto refresh (folder change notifications) -----------------------------------------------------------------

    bool syncRunning, syncFull, dirty;
    readonly HashSet<string> syncPaths = new(StringComparer.OrdinalIgnoreCase);

    // Changed paths of the shown folder (full = rescan the folder and diff). UI thread. Never touches the disk here: stat/enumeration run on a pool thread,
    // the result is applied in place through ApplyChanges (which is also what the file operations use, so both sources converge on the same rows).
    internal void Sync(ICollection<string> paths, bool full) {
        if (disposed) return;
        if (State == LoadState.Loading) { dirty = true; return; }       // applied by one rescan right after the load
        if (State is LoadState.Denied or LoadState.NotFound or LoadState.Failed && !full) return;
        foreach (var p in paths) syncPaths.Add(p);
        syncFull |= full;
        if (syncRunning) return;
        syncRunning = true;
        var batch = syncPaths.ToList(); bool whole = syncFull || batch.Count > 500;
        syncPaths.Clear(); syncFull = false;
        string loc = Location; bool hidden = ShowHidden;
        var known = whole ? all.ToArray() : Array.Empty<FileItem>();
        var visibleCheck = whole ? null : batch.Select(p => Find(p)).ToArray();
        Task.Run(() => {
            List<string>? gone = null; List<FileItem>? added = null; string? parent = null; bool lost = false;
            try {
                lost = !Directory.Exists(loc);
                if (lost) { parent = Path.GetDirectoryName(loc); while (parent != null && !Directory.Exists(parent)) parent = Path.GetDirectoryName(parent); }
                else {
                    var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    gone = new List<string>(); added = new List<FileItem>();
                    if (whole) {
                        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, ReturnSpecialDirectories = false, AttributesToSkip = hidden ? 0 : FileAttributes.Hidden | FileAttributes.System };
                        var fresh = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
                        foreach (var e in new DirectoryInfo(loc).EnumerateFileSystemInfos("*", options)) { var f = FileItem.From(e, types); fresh[f.FullPath] = f; }
                        var old = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
                        foreach (var k in known) old[k.FullPath] = k;
                        foreach (var k in known) if (!fresh.ContainsKey(k.FullPath)) gone.Add(k.FullPath);
                        foreach (var f in fresh.Values) if (!old.TryGetValue(f.FullPath, out var k) || Differs(k, f)) { if (k != null) gone.Add(f.FullPath); added.Add(f); }
                    } else {
                        for (int i = 0; i < batch.Count; i++) {
                            var now = FileItem.Probe(batch[i], types);
                            if (now != null && !hidden && (File.GetAttributes(batch[i]) & (FileAttributes.Hidden | FileAttributes.System)) != 0) now = null;
                            var was = visibleCheck![i];
                            if (now == null) { if (was != null) gone.Add(batch[i]); }
                            else if (was == null) added.Add(now);
                            else if (Differs(was, now)) { gone.Add(batch[i]); added.Add(now); }
                        }
                    }
                }
            } catch { gone = null; added = null; lost = false; }
            ui.InvokeAsync(() => {
                syncRunning = false;
                if (disposed || !string.Equals(loc, Location, StringComparison.OrdinalIgnoreCase)) return;
                if (lost) {
                    if (parent != null) { Notice?.Invoke(this, "Dossier supprimé ou renommé : retour au dossier parent"); Go(parent); }
                    else Fail(new DirectoryNotFoundException());
                    return;
                }
                if (State == LoadState.Loading) dirty = true;
                else if (gone != null && added != null && (gone.Count > 0 || added.Count > 0)) ApplyChanges(gone, added);
                if (syncPaths.Count > 0 || syncFull) Sync(Array.Empty<string>(), false);
            }, DispatcherPriority.Background);
        });
    }

    static bool Differs(FileItem a, FileItem b) => a.Size != b.Size || a.Modified != b.Modified || a.IsDir != b.IsDir;

    internal FileItem? Find(string path) => all.Find(i => string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
    internal void RaiseCut(HashSet<string> before, HashSet<string> after) { foreach (var item in all) if (before.Contains(item.FullPath) || after.Contains(item.FullPath)) item.RaiseCut(); }

    void Load(string path) {
        cts?.Cancel(); cts?.Dispose();
        var mine = cts = new CancellationTokenSource();
        Location = path; State = LoadState.Loading; Error = ""; filter = ""; dirty = false; syncPaths.Clear(); syncFull = false; Stale = false; firstLogged = false; sortGeneration++;
        all = new List<FileItem>();
        if (items.Count > 0) items.Reset(null);
        Perf.Stall?.Reset();
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        bool hidden = ShowHidden;
        var token = mine.Token;
        Changed?.Invoke(this);
        Perf.Mark("enumeration start");
        FirstPosted.Reset();
        Task.Run(() => Run(token, path, hidden, start));
    }

    void Post(CancellationToken token, Action action) =>
        ui.InvokeAsync(() => { if (!token.IsCancellationRequested) action(); }, DispatcherPriority.Background);

    void Run(CancellationToken token, string path, bool hidden, long start) {
        try {
            var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, ReturnSpecialDirectories = false,
                AttributesToSkip = hidden ? 0 : FileAttributes.Hidden | FileAttributes.System };
            var everything = new List<FileItem>();
            var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // The UI sees the first rows as early as possible (first full batch, or after FirstFlushMs on a slow disk), then at most one
            // append per FlushMs while enumeration is still running. Everything else waits for the single sorted Reset in Complete():
            // 10k items cost 2 collection notifications instead of ~40, and the sort never runs on the UI thread.
            int posted = 0; long lastFlush = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options)) {
                if (token.IsCancellationRequested) return;
                everything.Add(FileItem.From(entry, types));
                int pending = everything.Count - posted;
                if (posted == 0 ? pending >= Batch || Perf.Ms(lastFlush) >= FirstFlushMs : Perf.Ms(lastFlush) >= FlushMs) {
                    var slice = everything.GetRange(posted, pending);
                    if (posted == 0) slice = FileSort.Sorted(slice, Sort);
                    posted = everything.Count; lastFlush = System.Diagnostics.Stopwatch.GetTimestamp();
                    Post(token, () => AddBatch(slice, start));
                    FirstPosted.Set();
                }
            }
            // IgnoreInaccessible also swallows a denied root: re-read once with default options to tell empty from denied.
            if (everything.Count == 0) { using var probe = new DirectoryInfo(path).EnumerateFileSystemInfos().GetEnumerator(); probe.MoveNext(); }
            if (Perf.Enabled) Perf.Log($"nav {path}: enumeration complete {Perf.Ms(start):F1} ms ({everything.Count} items)");
            var sorted = FileSort.Sorted(everything, Sort);
            if (token.IsCancellationRequested) return;
            Post(token, () => Complete(sorted, start));
            FirstPosted.Set();
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            Post(token, () => Fail(ex));
            FirstPosted.Set();
        }
    }

    void AddBatch(List<FileItem> batch, long start) {
        items.AddRange(batch);
        LogFirstRender(start);
        Changed?.Invoke(this);
    }

    void Complete(List<FileItem> sorted, long start) {
        all = sorted;
        items.Reset(sorted);
        State = sorted.Count == 0 ? LoadState.Empty : LoadState.Ready;
        if (dirty) { dirty = false; Sync(Array.Empty<string>(), true); }   // something changed while this load was running
        LogFirstRender(start);
        Changed?.Invoke(this);
        if (Perf.Enabled) {
            string where = Location; int count = sorted.Count;
            AfterRender(() => Perf.Log($"nav {where}: complete rendered {Perf.Ms(start):F1} ms ({count} items), {Perf.SinceProcess():F0} ms since process start, max UI stall {Perf.Stall?.Max ?? 0:F1} ms"));
        }
    }

    void Fail(Exception ex) {
        items.Reset(null); all = new List<FileItem>();
        State = ex is UnauthorizedAccessException ? LoadState.Denied : ex is DirectoryNotFoundException or FileNotFoundException ? LoadState.NotFound : LoadState.Failed;
        Error = ex.Message;
        Changed?.Invoke(this);
    }

    // Loaded priority runs after the Render-priority layout/render pass that the batch just invalidated.
    void LogFirstRender(long start) {
        if (!Perf.Enabled || firstLogged || items.Count == 0) return;
        firstLogged = true;
        string where = Location; int count = items.Count;
        AfterRender(() => Perf.Log($"nav {where}: first rows rendered {Perf.Ms(start):F1} ms ({count} items in first update), {Perf.SinceProcess():F0} ms since process start"));
    }

    // Measurement only: rows applied before the window's first frame count as rendered at ContentRendered, later ones after the next render pass.
    void AfterRender(Action log) {
        if (Perf.FirstFrameDone) ui.InvokeAsync(log, DispatcherPriority.Loaded); else Perf.BeforeFirstFrame += log;
    }

    internal void Dispose() {
        disposed = true;
        cts?.Cancel(); cts?.Dispose(); cts = null;
        items.Reset(null); all = new List<FileItem>(); Changed = null;
    }
}
