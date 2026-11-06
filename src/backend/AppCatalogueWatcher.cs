// language: C# 12, file: AppCatalogueWatcher.cs, target: Windows 11 / .NET 8. Start menu watch: a new shortcut reaches the catalogue without waiting for the full inventory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Threading;

namespace PanneauxGlass;
// Two recursive FileSystemWatchers (user and common Start menu). The debounce timer only exists during a burst of events.
internal sealed class AppCatalogueWatcher : IDisposable
{
    readonly Dispatcher dispatcher; readonly Func<List<Entry>, int> add; readonly Func<List<string>, int> remove; readonly Action refresh; readonly Action? tick; System.Threading.Timer? poll; readonly RefreshThrottle throttle = new();
    readonly List<FileSystemWatcher> watchers = new(); readonly object gate = new(); readonly BurstDebounce burst = new();
    readonly Dictionary<string, int> paths = new(StringComparer.OrdinalIgnoreCase);
    System.Threading.Timer? timer, refreshTimer; bool disposed, wantRefresh;
    internal AppCatalogueWatcher(Dispatcher dispatcher, Func<List<Entry>, int> add, Func<List<string>, int> remove, Action refresh, Action? tick = null)
    {
        this.dispatcher = dispatcher;
        this.add = add;
        this.remove = remove;
        this.refresh = refresh;
        this.tick = tick;
    }
    internal void Start()
    {
        // Low-frequency check (packaged apps leave no Start menu event, vanished targets leave none either): one light pass per minute, nothing between.
        if (tick != null)
            poll = new System.Threading.Timer(_ => { if (!disposed) dispatcher.BeginInvoke((Action)(() => { if (!disposed) tick(); })); }, null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60));
        foreach (var folder in new[] { Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.CommonStartMenu })
        {
            try
            {
                string root = Environment.GetFolderPath(folder);
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    Store.Log("Veille du menu Démarrer : dossier absent (" + folder + ").");
                    continue;
                }
                var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite, InternalBufferSize = 65536 };
                watcher.Created += (_, e) => Event(e.FullPath, true, false);
                watcher.Changed += (_, e) => Event(e.FullPath, true, true);
                watcher.Deleted += (_, e) => Event(e.FullPath, false, false);
                watcher.Renamed += (_, e) => { Event(e.OldFullPath, false, false); Event(e.FullPath, true, false); };
                watcher.Error += (_, e) => Failed(watcher, e.GetException());
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            catch (Exception ex) { Store.Log("Veille du menu Démarrer : " + ex.Message); }
        }
    }
    // Buffer overflow or lost folder: a full inventory covers whatever was missed, then the watcher is re-armed if it can be.
    void Failed(FileSystemWatcher watcher, Exception? error)
    {
        Store.Log("Veille du menu Démarrer : " + (error?.Message ?? "erreur"));
        lock (gate)
        {
            if (!disposed)
            {
                wantRefresh = true;
                burst.Touch(DateTime.UtcNow);
                Arm(CatalogueWatchRules.Debounce);
            }
        }
        try
        {
            watcher.EnableRaisingEvents = false;
            if (Directory.Exists(watcher.Path))
                watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { Store.Log("Veille du menu Démarrer arrêtée : " + ex.Message); }
    }
    // Only a shortcut really added or removed leads to an inventory: filtered names, folder content changes and plain file changes are ignored.
    void Event(string path, bool present, bool changed)
    {
        if (!CatalogueWatchRules.IsShortcutEvent(path))
            return;
        bool folder = Path.GetExtension(path).Length == 0, lnk = CatalogueWatchRules.IsLnk(path);
        if (folder && changed)
            return;
        if (!folder)
        {
            string name = CatalogueWatchRules.CleanName(Path.GetFileNameWithoutExtension(path));
            if (name.Length == 0 || CatalogueWatchRules.IsExcludedName(name))
                return;
            if (!lnk && changed)
                return;
        }
        if (!present)
        {
            // Removal is immediate: the entry leaves the catalogue now, the inventory that follows rewrites catalog.json.
            dispatcher.BeginInvoke((Action)(() => { if (disposed) return; if (remove(new List<string> { path }) > 0) lock (gate) { if (disposed) return; wantRefresh = true; burst.Touch(DateTime.UtcNow); Arm(CatalogueWatchRules.Debounce); } }));
            return;
        }
        lock (gate)
        {
            if (disposed)
                return;
            if (lnk)
                paths.TryAdd(path, 0);
            else
                wantRefresh = true;
            burst.Touch(DateTime.UtcNow);
            Arm(CatalogueWatchRules.Debounce);
        }
    }
    void Arm(TimeSpan delay)
    {
        if (timer == null)
            timer = new System.Threading.Timer(_ => Fire(), null, delay, System.Threading.Timeout.InfiniteTimeSpan);
        else
            timer.Change(delay, System.Threading.Timeout.InfiniteTimeSpan);
    }
    void Fire()
    {
        Dictionary<string, int> batch;
        lock (gate)
        {
            if (disposed || timer == null)
                return;
            if (burst.Remaining(DateTime.UtcNow) is TimeSpan left && left > TimeSpan.Zero)
            {
                timer.Change(left, System.Threading.Timeout.InfiniteTimeSpan);
                return;
            }
            timer.Dispose();
            timer = null;
            burst.Reset();
            batch = new(paths, StringComparer.OrdinalIgnoreCase);
            paths.Clear();
        }
        dispatcher.BeginInvoke((Action)(() => Process(batch)));
    }
    // UI thread: shortcut parsing uses the same COM object as TaskbarNative.LaunchPath, entries are added where the catalogue is read.
    void Process(Dictionary<string, int> batch)
    {
        if (disposed)
            return;
        var found = new List<Entry>();
        var waiting = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in batch)
        {
            var entry = Build(item.Key, out bool targetPending);
            if (entry != null)
                found.Add(entry);
            else if (targetPending && item.Value + 1 < CatalogueWatchRules.MaxTargetWaits)
                waiting[item.Key] = item.Value + 1;
        }
        bool added = found.Count > 0 && add(found) > 0;
        if (waiting.Count > 0)
        {
            lock (gate)
            {
                if (disposed)
                    return;
                if (added)
                    wantRefresh = true;
                foreach (var item in waiting)
                    paths[item.Key] = item.Value;
                burst.Touch(DateTime.UtcNow);
                Arm(CatalogueWatchRules.Debounce);
            }
            return;
        }
        bool need;
        lock (gate)
        {
            if (added)
                wantRefresh = true;
            need = wantRefresh;
            wantRefresh = false;
        }
        if (need)
            RequestRefresh();
    }
    // At most one inventory per minute from the watcher; the last request is replayed by a one-shot timer that exists only while it waits.
    internal void RequestRefresh()
    {
        lock (gate)
        {
            if (disposed)
                return;
            TimeSpan wait = throttle.Delay(DateTime.UtcNow);
            if (wait > TimeSpan.Zero)
            {
                if (refreshTimer == null)
                    refreshTimer = new System.Threading.Timer(_ => { lock (gate) { refreshTimer?.Dispose(); refreshTimer = null; } dispatcher.BeginInvoke((Action)RequestRefresh); }, null, wait, System.Threading.Timeout.InfiniteTimeSpan);
                return;
            }
            throttle.Ran(DateTime.UtcNow);
        }
        refresh();
    }
    // Mirrors the .lnk branch of Inventory.ps1. A shortcut may appear before its target: report it as pending.
    static Entry? Build(string file, out bool targetPending)
    {
        targetPending = false;
        try
        {
            if (!File.Exists(file))
                return null;
            string name = CatalogueWatchRules.CleanName(Path.GetFileNameWithoutExtension(file));
            if (string.IsNullOrEmpty(name) || CatalogueWatchRules.IsExcludedName(name))
                return null;
            if (!ReadShortcut(file, out string resolved, out string arguments, out string iconLocation))
                return null;
            resolved = Environment.ExpandEnvironmentVariables(resolved);
            bool local = resolved.Length > 0 && !resolved.StartsWith(@"\\", StringComparison.Ordinal);
            if (local && !resolved.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !File.Exists(resolved) && !Directory.Exists(resolved))
            {
                targetPending = true;
                return null;
            }
            string icon = CatalogueWatchRules.IconSource(iconLocation, resolved, p => p.Length > 0 && !p.StartsWith(@"\\", StringComparison.Ordinal) && (File.Exists(p) || Directory.Exists(p)));
            return new Entry { Id = CatalogueWatchRules.ShortcutId(name, resolved, arguments), Name = name, Target = file, Section = "Applications", Category = "Autres logiciels", IconPath = icon };
        }
        catch (Exception ex) { Store.Log("Veille du menu Démarrer : " + ex.Message); return null; }
    }
    static bool ReadShortcut(string file, out string target, out string arguments, out string icon)
    {
        target = arguments = icon = "";
        object? shell = null, link = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null)
                return false;
            shell = Activator.CreateInstance(type);
            if (shell == null)
                return false;
            link = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { file });
            if (link == null)
                return false;
            string Get(string property) => Convert.ToString(link.GetType().InvokeMember(property, System.Reflection.BindingFlags.GetProperty, null, link, null)) ?? "";
            target = Get("TargetPath");
            arguments = Get("Arguments");
            icon = Get("IconLocation");
            return true;
        }
        catch { return false; }
        finally { if (link != null && System.Runtime.InteropServices.Marshal.IsComObject(link)) System.Runtime.InteropServices.Marshal.ReleaseComObject(link); if (shell != null && System.Runtime.InteropServices.Marshal.IsComObject(shell)) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            poll?.Dispose();
            poll = null;
            timer?.Dispose();
            timer = null;
            refreshTimer?.Dispose();
            refreshTimer = null;
        }
        foreach (var watcher in watchers)
        {
            Safe.Try(() =>
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();

            });
        }
        watchers.Clear();
    }
}
