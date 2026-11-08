// C# 12 / ExplorerAppearance.cs / Windows 11 : icônes de dossier natives et couleurs DWM réversibles, sans injection dans le shell.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows.Threading;
using D = System.Drawing;
using G = System.Drawing.Drawing2D;
namespace PanneauxGlass;

internal sealed partial class ExplorerAppearance : IDisposable
{
    // Regroupe les événements de fenêtre rapprochés avant de relancer le scan.
    const int ScanDebounceMs = 150;
    internal sealed class Backup
    {
        public string Folder { get; set; } = ""; public string? Ini
        {
            get; set;
        }
        public int Attributes
        {
            get; set;
        }
        public int IniAttributes
        {
            get; set;
        }
        public string? AppliedHash
        {
            get; set;
        }
        public DateTime? OriginalWriteUtc
        {
            get; set;
        }
        public bool DateRepaired
        {
            get; set;
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Settings
    {
        public uint Size, Mask;
        public IntPtr View, Template;
        public uint TemplateLength;
        public IntPtr Version, Tip;
        public uint TipLength;
        public IntPtr Class;
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)] public string Icon;
        public uint IconLength;
        public int Index;
        public IntPtr Logo;
        public uint LogoLength;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHGetSetFolderCustomSettings(ref Settings settings,
        string path,
        uint flags);
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern bool PathMakeSystemFolderW(string path);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern void SHChangeNotify(uint kind, uint flags, string? first,
        IntPtr second);
    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref uint value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out uint value, int size);
    readonly object gate = new();
    readonly string root;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    internal uint ExcludedProcess;
    internal string Status { get; private set; } = "Personnalisation inactive.";
    readonly Dictionary<IntPtr, uint[]> frames = new();
    readonly Dictionary<IntPtr, uint> frameOwners = new();
    readonly HashSet<IntPtr> captionFrames = new();
    readonly Dictionary<string, string> scanned = new(StringComparer.OrdinalIgnoreCase);
    bool scanning;
    DateTime scanAt;
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr Style(IntPtr window, int index);
    static void Release(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
    // Rescans pilotés par événements : un changement d'ensemble ou de titre de fenêtre (vu par FrameTick, sans E/S) ou un sous-dossier
    // créé/renommé/supprimé dans un dossier visible (FileSystemWatcher) arme UN scan 1,5 s plus tard.
    readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    string windowSignature = "";
    DateTime scanDue = DateTime.MaxValue;
    internal void RequestScan()
    {
        scanDue = DateTime.UtcNow.AddSeconds(1.5);
        if (disposed)
            return;
        void Fast()
        {
            if (!disposed && !eventPending && timer.Interval > TimeSpan.FromSeconds(1))
            {
                timer.Stop();
                timer.Interval = TimeSpan.FromSeconds(1);
                timer.Start();
            }
        }
        if (timer.Dispatcher.CheckAccess())
            Fast();
        else
            timer.Dispatcher.BeginInvoke(Fast);
    }
    void Watch(HashSet<string> visible)
    {
        lock (watchers)
        {
            foreach (var gone in watchers.Keys.Where(k => !visible.Contains(k)).ToArray())
            {
                watchers[gone].Dispose();
                watchers.Remove(gone);
            }
            foreach (string path in visible)
            {
                if (watchers.ContainsKey(path) || disposed || !Directory.Exists(path) || path.StartsWith(@"\\"))
                    continue;
                try
                {
                    var w =
                        new FileSystemWatcher(path) { NotifyFilter = NotifyFilters.DirectoryName, IncludeSubdirectories = false,
                            EnableRaisingEvents = true };
                    FileSystemEventHandler h = (_, _) => RequestScan();
                    w.Created += h;
                    w.Deleted += h;
                    w.Renamed += (_, _) => RequestScan();
                    watchers[path] = w;
                }
                catch (Exception ex)
                {
                    Store.Log("ExplorerAppearance.Watch : " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }
    }
    // Anti-rebond des notifications par dossier : au plus une notification du shell par dossier et par 10 s. Renvoie si elle a été
    // envoyée.
    readonly Dictionary<string, DateTime> notifiedAt = new(StringComparer.OrdinalIgnoreCase);
    internal int NotifyCount;
    internal bool Notify(string folder, DateTime? now = null)
    {
        var t = now ?? DateTime.UtcNow;
        lock (notifiedAt)
        {
            if (notifiedAt.TryGetValue(folder, out var last) && (t - last).TotalSeconds < 10)
                return false;
            notifiedAt[folder] = t;
            if (notifiedAt.Count > 4096)
                notifiedAt.Clear();
        }
        NotifyCount++;
        SHChangeNotify(0x2000, 5 | 0x3000, folder, IntPtr.Zero);
        return true;
    }
    // Vrai quand le desktop.ini du dossier est exactement celui que nous avons écrit en dernier (hash) et pointe déjà vers cette icône
    // (stable) : rien à écrire. Un ini qui pointe encore vers un de nos anciens fichiers <hex>-<kind>-v2.ico est « à nous » mais
    // renvoie false, donc migré (réécrit) à la prochaine visite ou par MigrateAsync.
    internal bool AlreadyThemed(string folder, string icon, List<Backup> data)
    {
        try
        {
            var saved = data.FirstOrDefault(p => p.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase));
            if (saved?.AppliedHash == null)
                return false;
            string ini = Path.Combine(folder, "desktop.ini");
            return File.Exists(ini) && Hash(ini) == saved.AppliedHash && File.ReadAllText(ini).Contains(icon,
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
    void ScanVisibleFolders()
    {
        if (scanning || DateTime.UtcNow < scanAt || !Store.State.ExplorerTheme)
            return;
        scanning = true;
        scanAt = DateTime.UtcNow.AddSeconds(4);
        var visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            object? shell = null, windows = null;
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type == null)
                    return;
                shell = Activator.CreateInstance(type);
                windows = ((dynamic)shell!).Windows();
                int count = Math.Min(32, (int)((dynamic)windows).Count);
                var budget = new[] { 1024 };
                for (int i = 0; i < count && !disposed; i++)
                {
                    object? window = null, document = null, folder = null, self = null;
                    try
                    {
                        window = ((dynamic)windows).Item(i);
                        if (window == null)
                            continue;
                        document = ((dynamic)window).Document;
                        folder = ((dynamic)document).Folder;
                        self = ((dynamic)folder).Self;
                        string path = (string)((dynamic)self).Path;
                        if (path.StartsWith("::{"))
                            continue;
                        visible.Add(path);
                        ColourVisibleTree(path, budget);
                    }
                    catch (Exception ex) { Store.Log("Dossiers visibles: " + ex.Message); }
                    finally
                    {
                        Release(self);
                        Release(folder);
                        Release(document);
                        Release(window);
                    }
                }
            }
            catch (Exception ex) { Store.Log("Explorateur visible: " + ex.Message); }
            finally
            {
                Release(windows);
                Release(shell);
                try
                {
                    Watch(visible);
                }
                catch (Exception err)
                {
                    Store.Log("ExplorerAppearance.ScanVisibleFolders : " + err.GetType().Name + ": " + err.Message);
                }
                scanning = false;
            }
        });
    }
    // Utilisable depuis un thread d'arrière-plan (l'application Fichiers l'appellera à la navigation) : anti-rebond de 3 s par dossier
    // et budget global gérés dans ColourVisibleFolder (ExplorerAppearance.StableIcons.cs).
    static bool Protected(string folder)
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var roots =
            new[]
            {
                windows,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            }.Where(p => !string.IsNullOrEmpty(p));
        return folder.Equals(Path.GetPathRoot(folder), StringComparison.OrdinalIgnoreCase)
            || roots.Any(p =>
                folder.Equals(p, StringComparison.OrdinalIgnoreCase) || folder.StartsWith(p + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));
    }
    internal ExplorerAppearance(string? directory = null)
    {
        root = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winlestia",
            "Explorer");
        timer.Tick += (_, _) =>
        {
            try
            {
                FrameTick();
            }
            finally
            {
                eventPending = false;
                timer.Interval = scanDue != DateTime.MaxValue ? TimeSpan.FromSeconds(1) : IdleInterval;
            }
        };
        timer.Start();
        hookProc = (_, evt, h, id, _, _, _) =>
        {
            if (evt == 3 && h != IntPtr.Zero && id == 0 && !disposed && GetAncestor(h, 2) == h)
                ForegroundChanged(h);
            if (h == IntPtr.Zero || id != 0 || disposed || eventPending || GetAncestor(h, 2) != h)
                return;
            eventPending = true;
            timer.Stop();
            timer.Interval = TimeSpan.FromMilliseconds(ScanDebounceMs);
            timer.Start();
        };
        foreach (var r in new (uint, uint)[] { (3, 3), (0x16, 0x17), (0x8000, 0x8003), (0x800C, 0x800C) })
            hooks.Add(SetWinEventHook(r.Item1, r.Item2, IntPtr.Zero, hookProc, 0, 0, 0));
    }
    // Ralentissement au repos : les événements de fenêtre (premier plan/réduction/création/destruction/affichage/masquage/changement
    // de nom) réarment un tick anti-rebond de 150 ms ; sinon tick de sécurité toutes les 5 s (1 s tant qu'un scan est dû).
    static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(5);
    bool eventPending;
    readonly List<IntPtr> hooks = new();
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    readonly WinEventProc? hookProc;
    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int id, int child, uint thread, uint time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid,
        uint thread,
        uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    string Manifest => Path.Combine(root, "manifest.json");
    List<Backup> Read() => File.Exists(Manifest) ? JsonSerializer.Deserialize<List<Backup>>(File.ReadAllText(Manifest)) ?? new() : new();
    void Save(List<Backup> data)
    {
        Directory.CreateDirectory(root);
        string temp = Manifest + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data));
        File.Move(temp, Manifest, true);
    }
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    internal static string Known(string guid)
    {
        var id = new Guid(guid);
        if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pointer) != 0)
            return "";
        try
        {
            return Marshal.PtrToStringUni(pointer) ?? "";
        }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }
    internal static (string path, string kind)[] PersonalFolders()
        => new[]
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "desktop"),
            (Known("374DE290-123F-4565-9164-39C4925E467B"), "download"),
            (Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "document"),
            (Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "picture"),
            (Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "music"),
            (Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "video")
        };
    internal string Sync(string a, string bg, string fg, bool force = false)
    {
        lock (gate)
        {
            if (disposed)
                return "";
            foreach (string value in new[] { a, bg, fg })
                if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$"))
                    throw new ArgumentException("Palette invalide");
            accent = a;
            background = bg;
            text = fg;
            if (Store.State.AccentBorders && Store.State.ActiveBorderOnly)
                RefreshFrames();
            if (!Store.State.ExplorerTheme)
                return "";
            if (!repairPassDone)
            {
                repairPassDone = true;
                _ = System.Threading.Tasks.Task.Run(RepairManifestDates);
            }
            StartIconMaintenance();
            string key = a + bg + fg + string.Join("|", Store.State.ExplorerFolders);
            if (!force && key == signature)
                return "";
            var report = new List<string>();
            var folders =
                PersonalFolders().Concat(Store.State.ExplorerFolders.Select(p => (p, "folder")))
                    .DistinctBy(p => p.Item1, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            foreach (var item in folders)
            {
                try
                {
                    if (!Directory.Exists(item.Item1))
                        continue;
                    string icon = EnsureIcon(item.Item2);
                    ApplyFolder(item.Item1, icon);
                    report.Add("OK " + item.Item1);
                }
                catch (Exception ex) { report.Add("SKIPPED " + item.Item1 + ": " + ex.Message); }
            }
            Directory.CreateDirectory(root);
            File.WriteAllLines(Path.Combine(root, "last-report.txt"), report);
            signature = key;
            Status = report.Count(p => p.StartsWith("OK ")) + " dossier(s) assorti(s) · " + report.Count(p => p.StartsWith("SKIPPED "))
                + " ignoré(s).";
            return string.Join(Environment.NewLine, report);
        }
    }
    internal void ApplyFolder(string folder, string icon)
    {
        lock (gate)
        {
            var data = Read();
            if (AlreadyThemed(folder, icon, data))
                return;
            int before = data.Count;
            var prep = Prepare(folder, data);
            if (data.Count > before)
                Save(data);
            WriteFolder(prep.folder, icon, prep.saved);
            Save(data);
        }
    }
    // Valide le dossier et enregistre sa sauvegarde en mémoire (l'appelant la persiste avant WriteFolder).
    (string folder, Backup saved) Prepare(string folder, List<Backup> data)
    {
        folder = Path.GetFullPath(folder);
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            folder = new DirectoryInfo(folder).ResolveLinkTarget(true)?.FullName ?? throw new IOException("Cible du dossier introuvable.");
        if (folder.StartsWith(@"\\") || folder.Length > 240 || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Dossier distant, redirigé ou chemin non pris en charge.");
        if (Protected(folder))
            throw new IOException("Emplacement système exclu.");
        if (File.Exists(Path.Combine(folder, "desktop.ini")) && (File.GetAttributes(Path.Combine(folder,
            "desktop.ini")) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("desktop.ini redirigé : conservé.");
        var saved = data.FirstOrDefault(p => p.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase));
        string ini = Path.Combine(folder, "desktop.ini");
        if (saved == null)
        {
            saved = new()
            {
                Folder = folder,
                Ini = File.Exists(ini) ? Convert.ToBase64String(File.ReadAllBytes(ini)) : null,
                Attributes = (int)File.GetAttributes(folder),
                IniAttributes = File.Exists(ini) ? (int)File.GetAttributes(ini) : 0,
                OriginalWriteUtc = Directory.GetLastWriteTimeUtc(folder)
            };
            data.Add(saved);
        }
        else if (saved.AppliedHash != null && (!File.Exists(ini) || Hash(ini) != saved.AppliedHash))
            throw new IOException("desktop.ini modifié ailleurs : conservé.");
        return (folder, saved);
    }
    // Date « Modifié » du dossier : conservée d'une écriture à l'autre (original stocké, sinon heuristique ponctuelle pour les
    // entrées thémées avant l'existence d'OriginalWriteUtc).
    DateTime? Stamp(Backup saved)
    {
        try
        {
            if (saved.OriginalWriteUtc != null)
                return saved.OriginalWriteUtc;
            if (!saved.DateRepaired)
            {
                RepairDatesFromChildren(saved.Folder);
                saved.DateRepaired = true;
            }
            return Directory.GetLastWriteTimeUtc(saved.Folder);
        }
        catch { return null; }
    }
    static void SetStamp(string folder, DateTime? when)
    {
        if (when == null)
            return;
        try
        {
            Directory.SetLastWriteTimeUtc(folder, when.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Store.Log("ExplorerAppearance.SetStamp : " + ex.GetType().Name + ": " + ex.Message);
        }
    }
    // Fixe la date de modification du dossier sur celle de l'enfant le plus récent (desktop.ini et nos propres icônes exclus) ;
    // inchangée
    // s'il n'y en a pas.
    internal void RepairDatesFromChildren(string folder)
    {
        try
        {
            string icons = Path.Combine(root, "icons");
            DateTime? newest = null;
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                if (entry.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
                    || entry.FullName.StartsWith(icons, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (newest == null || entry.LastWriteTimeUtc > newest)
                    newest = entry.LastWriteTimeUtc;
            }
            SetStamp(folder, newest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Store.Log("ExplorerAppearance.RepairDatesFromChildren : " + ex.GetType().Name + ": " + ex.Message);
        }
    }
    // Ponctuel (premier Sync après le démarrage) : répare les dossiers thémés jamais réécrits (inchangés, non visités). 256
    // entrées au plus, un seul Save.
    bool repairPassDone;
    internal void RepairManifestDates()
    {
        lock (gate)
        {
            if (disposed)
                return;
            try
            {
                var data = Read();
                int done = 0;
                foreach (var saved in data)
                {
                    if (saved.DateRepaired || saved.OriginalWriteUtc != null || !Directory.Exists(saved.Folder))
                        continue;
                    if (done++ >= 256)
                        break;
                    RepairDatesFromChildren(saved.Folder);
                    saved.DateRepaired = true;
                }
                if (done > 0)
                    Save(data);
            }
            catch (Exception ex) { Store.Log("Réparation des dates: " + ex.Message); }
        }
    }
    void WriteFolder(string folder, string icon, Backup saved)
    {
        DateTime? stamp = Stamp(saved);
        try
        {
            WriteFolderCore(folder, icon, saved);
        }
        finally { SetStamp(folder, stamp); }
    }
    void WriteFolderCore(string folder, string icon, Backup saved)
    {
        string ini = Path.Combine(folder, "desktop.ini");
        if (!PathMakeSystemFolderW(folder))
            throw new IOException("Windows refuse la personnalisation.");
        var settings = new Settings { Size = (uint)Marshal.SizeOf<Settings>(), Mask = 0x10, Icon = icon };
        Marshal.ThrowExceptionForHR(SHGetSetFolderCustomSettings(ref settings, folder, 2));
        saved.AppliedHash = Hash(ini);
        Notify(folder);
    }
    internal string Restore()
    {
        lock (gate)
        {
            int navigationConflicts = ExplorerNavigationIcons.Restore(root);
            SHChangeNotify(0x08000000, 0, null, IntPtr.Zero);
            var data = Read();
            var remaining = new List<Backup>();
            foreach (var saved in data)
            {
                try
                {
                    if (!Directory.Exists(saved.Folder))
                        continue;
                    DateTime? stamp = Stamp(saved);
                    string ini = Path.Combine(saved.Folder, "desktop.ini");
                    if (saved.AppliedHash != null && (!File.Exists(ini) || Hash(ini) != saved.AppliedHash))
                        throw new IOException("desktop.ini modifié ailleurs : restauration manuelle requise.");
                    if (File.Exists(ini))
                        File.SetAttributes(ini, FileAttributes.Normal);
                    if (saved.Ini == null)
                    {
                        if (File.Exists(ini))
                            File.Delete(ini);
                    }
                    else
                    {
                        File.WriteAllBytes(ini, Convert.FromBase64String(saved.Ini));
                        File.SetAttributes(ini, (FileAttributes)saved.IniAttributes);
                    }
                    File.SetAttributes(saved.Folder, (FileAttributes)saved.Attributes);
                    SHChangeNotify(0x2000, 5, saved.Folder, IntPtr.Zero);
                    SetStamp(saved.Folder, stamp);
                }
                catch (Exception ex)
                {
                    remaining.Add(saved);
                    Store.Log("Explorateur restauration: " + ex.Message);
                }
            }
            Save(remaining);
            scanned.Clear();
            signature = "";
            if (navigationConflicts > 0)
            {
                Status = navigationConflicts + " réglage(s) de navigation modifié(s) ailleurs, conservé(s).";
                return Status;
            }
            Status = remaining.Count == 0 ? "Icônes d’origine restaurées." : remaining.Count + " conflit(s) conservé(s).";
            return remaining.Count == 0 ? "Icônes d’origine restaurées." : remaining.Count
                + " dossier(s) conservé(s), consulter le journal.";
        }
    }
    static uint Colour(string hex)
    {
        var c = D.ColorTranslator.FromHtml(hex);
        return (uint)(c.R | c.G << 8 | c.B << 16);
    }
    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        if (timer.Dispatcher.CheckAccess())
        {
            foreach (var k in hooks)
                UnhookWinEvent(k);
            hooks.Clear();
        }
        else
            timer.Dispatcher.Invoke(() =>
            {
                foreach (var k in hooks)
                    UnhookWinEvent(k);
                hooks.Clear();
            });
        DisposeIcons();
        lock (watchers)
        {
            foreach (var w in watchers.Values)
                w.Dispose();
            watchers.Clear();
        }
        RestoreFrames();
    }
}
