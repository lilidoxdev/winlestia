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
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    string Manifest => Path.Combine(root, "manifest.json");
    List<Backup> Read() => File.Exists(Manifest) ? JsonSerializer.Deserialize<List<Backup>>(File.ReadAllText(Manifest)) ?? new() : new();
    void Save(List<Backup> data)
    {
        Directory.CreateDirectory(root);
        Winlestia.Shared.AtomicFile.WriteAllText(Manifest, JsonSerializer.Serialize(data));
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
