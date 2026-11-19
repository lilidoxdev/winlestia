// C# 12 / QtDockActions.cs / Windows 11. État du dock et actions de bureau validées.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    object[] runningApps = Array.Empty<object>();
    // Bulle d'aperçu des fenêtres (DockPreviewPopup.cs) : hwnd/titre par id d'élément du dock, rafraîchis à chaque RefreshDockApps.
    readonly DockPreviewHost dockPreview = new(); readonly Dictionary<string, PreviewWindow[]> dockWindows = new();
    void DockPreviewOpen(JsonElement args)
    {
        string id = Text(args, "id");
        double dpr = Number(args, "dpr") > 0 ? Number(args, "dpr") : 1, x = Number(args, "x"), y = Number(args, "y"), cx = Number(args,
            "cx"), cy = Number(args, "cy");
        bool light = LightRendering;
        dispatcher.BeginInvoke((Action)(() =>
        {
            if (disposed || !owner.TaskbarEnabled || !dockWindows.TryGetValue(id, out var list) || list.Length == 0)
            {
                dockPreview.Close();
                return;
            }
            // pixels physiques ; le centre de l'icône et le haut du dock sont des décalages depuis le curseur en px logiques *
            // devicePixelRatio (sûr par moniteur)
            Native.GetCursorPos(out var cursor);
            dockPreview.Open(id, list, (int)Math.Round(cursor.X + (x - cx) * dpr), (int)Math.Round(cursor.Y + (y - cy) * dpr), light);
        }));
    }
    void DockPreviewEnd(JsonElement args)
    {
        bool now = Flag(args, "now");
        dispatcher.BeginInvoke((Action)(() => { if (now) dockPreview.Close(); else dockPreview.Retire(150); }));
    }
    void SyncDockPreview()
    {
        if (!dockPreview.IsOpen)
            return;
        if (dockWindows.TryGetValue(dockPreview.OwnerId, out var list) && list.Length > 0)
            dockPreview.Update(list);
        else
            dockPreview.Close();
    }
    [DllImport("user32.dll", SetLastError = true)] static extern bool PostMessageW(IntPtr window, uint message, IntPtr wp, IntPtr lp);
    static bool SamePath(string a, string b) => !string.IsNullOrWhiteSpace(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    static bool CanStop(string path) => File.Exists(path) && !path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !SamePath(path, Environment.ProcessPath ?? "")
        && !StopPolicy.IsDenied(path);
    // Processus propres au shell (backend, frontend et leurs descendants) : jamais comptés ni arrêtés.
    int[] ShellRoots() => new[] { Environment.ProcessId, System.Threading.Volatile.Read(ref frontend)?.Id
        ?? 0, (int)System.Threading.Volatile.Read(ref frontendWindowPid) };
    // Dernières mesures par exécutable ; renouvelées seulement par runningRefresh (la fenêtre « Applications » ouverte).
    Dictionary<string, ProcessUsageSampler.Usage> usageByPath = new(StringComparer.OrdinalIgnoreCase);
    readonly ProcessUsageSampler sampler = new(); readonly Dictionary<string, string> backgroundNames =
        new(StringComparer.OrdinalIgnoreCase);
    (double Cpu, int Memory, int Count) UsageOf(string path) => usageByPath.TryGetValue(path, out var u)
        ? (Math.Round(u.Cpu, 1), (int)Math.Round(u.MemoryBytes / 1048576.0), u.Count) : (0, 0, 0);
    void RefreshDockApps()
    {
        // Nom historique conservé pour compatibilité (préfixe de titre des fenêtres Qt, défini dans les fichiers QML).
        var windows = TaskbarNative.Applications().Where(w => !w.Title.Contains("Caelestia") && !w.Title.StartsWith("Panneaux ·",
            StringComparison.Ordinal)).ToArray();
        var livePseudo = new HashSet<string>();
        var result = new List<object>();
        var running = new List<object>();
        var pinnedEntries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        dockEntries.Clear();
        dockWindows.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = windows.GroupBy(w => CatalogueWatchRules.WindowGroupKey(w.Path, w.Handle.ToInt64()), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        var entries = Store.Entries.Where(e => !string.IsNullOrEmpty(e.Target)).ToArray();
        object Value(Entry entry, TaskbarNative.AppWindow[] matches, bool pinned, bool background = false)
        {
            string path = TaskbarNative.LaunchPath(entry.Target);
            dockEntries[entry.Id] = entry;
            if (CatalogueWatchRules.IsPseudoPath(path))
            {
                livePseudo.Add(entry.Id);
                LoadWindowIcon(entry, matches.FirstOrDefault()?.Handle ?? IntPtr.Zero);
            }
            else
                LoadIcon(entry);
            dockWindows[entry.Id] = matches.Select(w => new PreviewWindow(w.Handle.ToInt64(), w.Title)).ToArray();
            var usage = UsageOf(path);
            return new
            {
                id = entry.Id,
                name = entry.Name,
                path,
                hwnd = matches.FirstOrDefault()?.Handle.ToInt64() ?? 0,
                pinned,
                favorite = Store.State.Favorites.Contains(entry.Id),
                background,
                canStop = CanStop(path),
                count = matches.Length,
                active = matches.Any(w => w.Handle == Native.GetForegroundWindow()),
                windows = matches.Select(w => new { hwnd = w.Handle.ToInt64(), title = w.Title }).ToArray(),
                cpu = usage.Cpu,
                mem = usage.Memory,
                procs = usage.Count
            };
        }
        bool pinsChanged = false;
        for (int i = 0; i < Store.State.TaskbarPins.Count; i++)
        {
            string old = Store.State.TaskbarPins[i];
            var source = entries.FirstOrDefault(e => e.Id == old) ?? Store.State.Custom.FirstOrDefault(e => e.Id == old);
            if (source == null && File.Exists(old))
                source = new()
                {
                    Id = old,
                    Name = Path.GetFileNameWithoutExtension(old),
                    Target = old,
                    IconPath = old
                };
            if (source == null)
                continue;
            var stable = PersistDockEntry(source);
            if (old != stable.Id)
            {
                Store.State.TaskbarPins[i] = stable.Id;
                pinsChanged = true;
            }
        }
        if (pinsChanged)
            Store.Save();
        entries = Store.Entries.Where(e => !string.IsNullOrEmpty(e.Target)).ToArray();
        foreach (string id in Store.State.TaskbarPins.Take(24))
        {
            var e = entries.FirstOrDefault(e => e.Id == id);
            if (e == null && File.Exists(id))
                e = new()
                {
                    Id = id,
                    Name = Path.GetFileNameWithoutExtension(id),
                    Target = id,
                    IconPath = id
                };
            if (e == null)
                continue;
            string path = TaskbarNative.LaunchPath(e.Target);
            if (!seen.Add(path))
                continue;
            var matches = groups.GetValueOrDefault(path) ?? windows.Where(w => Store.Fold(Path.GetFileNameWithoutExtension(w.Path))
                .Replace(" ", "") == Store.Fold(e.Name).Replace(" ", "")).ToArray();
            pinnedEntries[path] = e;
            foreach (var match in matches)
            {
                seen.Add(match.Path);
                pinnedEntries[match.Path] = e;
            }
            result.Add(Value(e, matches, true));
        }
        foreach (var group in groups)
        {
            var e = pinnedEntries.GetValueOrDefault(group.Key) ?? entries.FirstOrDefault(e => SamePath(e.Target, group.Key)) ?? new Entry
            {
                Id = "task:" + group.Key,
                Name = CatalogueWatchRules.IsPseudoPath(group.Key) || group.Key.StartsWith("shell:",
                StringComparison.OrdinalIgnoreCase) ? group.Value[0].Title : Path.GetFileNameWithoutExtension(group.Key),
                Target = group.Key,
                IconPath = group.Key
            };
            bool pinned = Store.State.TaskbarPins.Contains(e.Id) || Store.State.TaskbarPins.Contains(group.Key);
            var value = Value(e, group.Value, pinned);
            running.Add(value);
            if (seen.Add(group.Key))
                result.Add(value);
        }
        foreach (var e in backgroundEntries)
        {
            if (groups.ContainsKey(e.Target) || !seen.Add(e.Target))
                continue;
            running.Add(Value(e, Array.Empty<TaskbarNative.AppWindow>(), Store.State.TaskbarPins.Contains(e.Id), true));
        }
        tasks = result.Take(48).ToArray();
        runningApps = running.ToArray();
        ForgetWindowIcons(livePseudo);
        SyncDockPreview();
    }
    // Vue d'ensemble : l'id d'entrée du dock (nécessaire à dockApp) et l'icône viennent de dockWindows ; si une fenêtre y manque
    // (dock masqué, liste périmée), le dock est rafraîchi une fois. Thread UI.
    OverviewItem[] OverviewItems(List<OverviewRaw> raw, ISet<uint> shellPids)
    {
        if (disposed)
            return Array.Empty<OverviewItem>();
        OverviewItem[] Build()
        {
            var owners = new Dictionary<long, string>();
            foreach (var pair in dockWindows)
                foreach (var w in pair.Value)
                    owners.TryAdd(w.Hwnd, pair.Key);
            return WindowOverview.Project(raw, shellPids, w =>
            {
                string id = owners.GetValueOrDefault(w.Hwnd, "");
                return (id.Length > 0 && dockEntries.TryGetValue(id, out var entry) ? entry.Name : "", id, id);
            });
        }
        // Jamais de RefreshDockApps ici : le sondage tourne toutes les 1,5 s. Sans entrée de dock, la fenêtre garde son nom de fichier.
        var items = Build();
        return items;
    }
    Entry[] backgroundEntries = Array.Empty<Entry>(); bool scanningBackground; DateTime backgroundAt;
    // Un seul balayage à la fois : le cache de noms n'est touché que par ce thread de travail.
    string BackgroundName(string path)
    {
        if (!backgroundNames.TryGetValue(path, out string? name))
        {
            name = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (string.IsNullOrWhiteSpace(name))
                name = Path.GetFileNameWithoutExtension(path);
            backgroundNames[path] = name;
        }
        return name;
    }
    void RefreshBackgroundApps()
    {
        if (scanningBackground || DateTime.UtcNow < backgroundAt)
            return;
        scanningBackground = true;
        // Plancher d'une seconde : le frontend relance toutes les 2 s tant que la fenêtre est ouverte.
        backgroundAt = DateTime.UtcNow.AddSeconds(1);
        var known = Store.Entries.ToArray();
        var roots = ShellRoots();
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            var result = new List<Entry>();
            List<ProcessUsageSampler.Usage> usages;
            try
            {
                usages = sampler.Sample(roots);
            }
            catch (Exception)
            {
                usages = new();
            }
            foreach (var usage in usages)
                try
                {
                    string path = usage.Path;
                    if (!CanStop(path))
                        continue;
                    result.Add(known.FirstOrDefault(e => SamePath(e.Target, path)) ?? new Entry
                    {
                        Id = "background:" + path,
                        Name = BackgroundName(path),
                        Target = path,
                        IconPath = path
                    });
                }
                catch (Exception)
                {
                    // Fichier inaccessible : ignoré.
                }
            dispatcher.BeginInvoke((Action)(() =>
            {
                scanningBackground = false;
                if (!disposed)
                {
                    backgroundEntries = result.ToArray();
                    usageByPath = usages.ToDictionary(u => u.Path, u => u, StringComparer.OrdinalIgnoreCase);
                    nextTasks = DateTime.MinValue;
                }
            }));
        });
    }
    // Fenêtres à pseudo-chemin : l'icône vient de la fenêtre elle-même (WM_GETICON avec délai, puis l'icône de classe), jamais d'un
    // fichier.
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message,
        IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr GetClassLongPtrW(IntPtr window, int index);
    readonly Dictionary<string, int> windowIconTries = new();
    string WindowIconFile(string id) => Path.Combine(cache, "icons", "win-"
        + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id)))[..24] + ".png");
    // Appelé à chaque rafraîchissement du dock : une fenêtre disparue perd son compteur d'essais, son entrée dans la table d'icônes
    // et son PNG.
    void ForgetWindowIcons(HashSet<string> live)
    {
        foreach (string id in windowIconTries.Keys.Where(k => !live.Contains(k)).ToArray())
        {
            windowIconTries.Remove(id);
            requested.Remove(id);
            icons.Remove(id);
            Safe.Try(() => File.Delete(WindowIconFile(id)));
        }
    }
    void LoadWindowIcon(Entry entry, IntPtr window)
    {
        if (window == IntPtr.Zero || !requested.Add(entry.Id))
            return;
        int tries = windowIconTries.GetValueOrDefault(entry.Id) + 1;
        windowIconTries[entry.Id] = tries;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                IntPtr icon = IntPtr.Zero;
                try
                {
                    SendMessageTimeoutW(window, 0x007F, new IntPtr(2), IntPtr.Zero, 0x2, 100, out icon);
                    if (icon == IntPtr.Zero)
                        SendMessageTimeoutW(window, 0x007F, new IntPtr(1), IntPtr.Zero, 0x2, 100, out icon);
                }
                catch { icon = IntPtr.Zero; }
                if (icon == IntPtr.Zero)
                    icon = GetClassLongPtrW(window, -34);
                if (icon == IntPtr.Zero)
                    icon = GetClassLongPtrW(window, -14);
                if (icon == IntPtr.Zero)
                    throw new InvalidOperationException("no window icon");
                var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon, System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                string file = WindowIconFile(entry.Id);
                SaveImage(bitmap, file);
                dispatcher.BeginInvoke((Action)(() =>
                {
                    if (disposed)
                        return;
                    if (windowIconTries.ContainsKey(entry.Id))
                    {
                        icons[entry.Id] = file;
                        iconRevision++;
                    }
                    else
                    {
                        Safe.Try(() => File.Delete(file));
                    }
                }));
            }
            catch { dispatcher.BeginInvoke((Action)(() => { if (tries < 3) requested.Remove(entry.Id); })); }
        });
    }
    Entry PersistDockEntry(Entry entry)
    {
        // Applis Store : on épingle l'AUMID (shell:AppsFolder\...), pas le dossier versionné qui disparaît à la mise à jour.
        entry = PackagedApps.Canonical(entry, Store.Entries) ?? entry;
        var saved = Store.State.Custom.FirstOrDefault(e => e.Id.StartsWith("dock:") && SamePath(e.Target, entry.Target));
        if (saved != null)
            return saved;
        saved = new()
        {
            Id = "dock:" + entry.Target,
            Name = entry.Name,
            Target = entry.Target,
            IconPath = entry.IconPath,
            Section = "Applications",
            Category = "Applications"
        };
        Store.State.Custom.Add(saved);
        Store.Entries.Add(saved);
        return saved;
    }
    string? DockAction(JsonElement args)
    {
        string id = Text(args, "id"), action = Text(args, "action");
        long handle = (long)Number(args, "hwnd");
        if (!dockEntries.TryGetValue(id, out var entry))
        {
            entry = Store.Entries.FirstOrDefault(e => e.Id == id);
            if (entry == null)
                return "Cette application n’est plus disponible.";
        }
        string path = TaskbarNative.LaunchPath(entry.Target);
        var matches = TaskbarNative.Applications().Where(w => SamePath(w.Path, path)).ToArray();
        var selected = matches.FirstOrDefault(w => w.Handle.ToInt64() == handle) ?? matches.FirstOrDefault();
        // Une fenêtre dont le programme est illisible (élevé) a un pseudo-chemin : on peut l'activer ou la fermer, jamais la lancer,
        // l'épingler ni la révéler.
        if (CatalogueWatchRules.IsPseudoPath(path) && action is "open" or "admin" or "pin" or "favorite" or "reveal" or "stop")
            return "Cette fenêtre n’a pas de programme lançable.";
        switch (action)
        {
            case "reorder":
                int from = Store.State.TaskbarPins.IndexOf(id), to = (int)Number(args, "index");
                if (from >= 0)
                {
                    Store.State.TaskbarPins.RemoveAt(from);
                    Store.State.TaskbarPins.Insert(Math.Clamp(to, 0, Store.State.TaskbarPins.Count), id);
                    Store.Save();
                }
                break;
            case "open":
                owner.Launch(entry);
                break;
            case "admin":
                if (File.Exists(path))
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" });
                break;
            case "activate":
                if (selected != null)
                {
                    if (TaskbarNative.IsIconic(selected.Handle))
                        TaskbarNative.ShowWindow(selected.Handle, 9);
                    TaskbarNative.SetForegroundWindow(selected.Handle);
                }
                break;
            case "minimize":
                if (selected != null)
                    TaskbarNative.ShowWindow(selected.Handle, 6);
                break;
            case "maximize":
                if (selected != null)
                    TaskbarNative.ShowWindow(selected.Handle, 3);
                break;
            case "close":
                if (selected != null && !PostMessageW(selected.Handle, 0x10, IntPtr.Zero, IntPtr.Zero))
                    return "Windows a refusé la fermeture de cette fenêtre.";
                break;
            case "closeAll":
                foreach (var window in matches)
                    if (!PostMessageW(window.Handle, 0x10, IntPtr.Zero, IntPtr.Zero))
                        return "Certaines fenêtres n’ont pas pu être fermées.";
                break;
            case "pin":
                var pin = PersistDockEntry(entry);
                bool wasPinned = Store.State.TaskbarPins.Contains(pin.Id) || Store.State.TaskbarPins.Contains(entry.Id)
                    || Store.State.TaskbarPins.Contains(entry.Target);
                Store.State.TaskbarPins.Remove(pin.Id);
                Store.State.TaskbarPins.Remove(entry.Id);
                Store.State.TaskbarPins.Remove(entry.Target);
                if (!wasPinned)
                    Store.State.TaskbarPins.Add(pin.Id);
                Store.Save();
                break;
            case "favorite":
                var favorite = PersistDockEntry(entry);
                if (!Store.State.Favorites.Remove(favorite.Id))
                {
                    Store.State.Favorites.Add(favorite.Id);
                    if (!Store.State.TaskbarPins.Contains(favorite.Id))
                        Store.State.TaskbarPins.Add(favorite.Id);
                }
                Store.Save();
                break;
            case "reveal":
                owner.Reveal(entry);
                break;
            case "stop":
                if (!Flag(args, "confirmed") || !CanStop(path))
                    return "Arrêt refusé pour ce processus.";
                int session = Process.GetCurrentProcess().SessionId;
                // Backend, frontend, descendants et ancêtres (terminal, lanceur) du shell survivent même si leur chemin correspond.
                var shellPids = ProcessUsageSampler.ProtectedPids(ShellRoots());
                foreach (var p in Process.GetProcesses())
                    using (p)
                    {
                        Safe.Try(() =>
                        {
                            if (!shellPids.Contains(p.Id) && p.SessionId == session
                                && SamePath(TaskbarNative.ProcessImagePath((uint)p.Id), path))
                                p.Kill();

                        });
                    }
                backgroundEntries = backgroundEntries.Where(e => !SamePath(e.Target, path)).ToArray();
                backgroundAt = DateTime.MinValue;
                RefreshBackgroundApps();
                break;
        }
        return null;
    }
    void DockSystem(string id, bool confirmed = false)
    {
        if (!confirmed && id is "shutdown" or "restart" or "logout" or "hibernate" or "sleep")
            return;
        string target, arguments = "";
        bool admin = false;
        switch (id)
        {
            case "shutdown":
                target = "shutdown.exe";
                arguments = "/s /t 0";
                break;
            case "restart":
                target = "shutdown.exe";
                arguments = "/r /t 0";
                break;
            case "lock":
                SystemActions.LockNow();
                return;
            case "logout":
                target = "shutdown.exe";
                arguments = "/l";
                break;
            case "hibernate":
                System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Hibernate, false, false);
                return;
            case "sleep":
                System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false);
                return;
            case "terminal":
                target = "wt.exe";
                break;
            case "terminalAdmin":
                target = "wt.exe";
                admin = true;
                break;
            case "taskmgr":
                target = "taskmgr.exe";
                break;
            case "settings":
                target = "ms-settings:";
                break;
            case "apps":
                target = "ms-settings:appsfeatures";
                break;
            case "power":
                target = "ms-settings:powersleep";
                break;
            case "system":
                target = "ms-settings:about";
                break;
            case "network":
                target = "ms-settings:network-status";
                break;
            case "devices":
                target = "devmgmt.msc";
                break;
            case "events":
                target = "eventvwr.msc";
                break;
            case "disks":
                target = "diskmgmt.msc";
                break;
            case "computer":
                target = "compmgmt.msc";
                break;
            case "explorer":
                target = "explorer.exe";
                break;
            case "control":
                target = "control.exe";
                break;
            case "desktop":
                TaskbarNative.WindowsKey(0x44);
                return;
            default:
                return;
        }
        Process.Start(new ProcessStartInfo(target, arguments) { UseShellExecute = true, Verb = admin ? "runas" : "open" });
    }
}
