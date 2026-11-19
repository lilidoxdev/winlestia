// language: C# 12, file: CatalogueService.cs, target: Windows 11 / .NET 8, inventaire et surveillance du catalogue d'applications
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PanneauxGlass;

internal interface ICatalogueService : IDisposable
{
    void Refresh();
    void RefreshIfStale();
    void StartWatching();
}

// Le service ne connaît ni les planches ni le pont Qt : tout changement du catalogue passe par le rappel `changed`.
internal sealed class CatalogueService : ICatalogueService
{
    readonly bool preview;
    readonly Action changed;
    AppCatalogueWatcher? watcher;
    readonly RefreshGate gate = new();
    DateTime? attempt;
    int packageCount = -1;
    bool disposed;
    internal CatalogueService(bool preview, Action changed)
    {
        this.preview = preview;
        this.changed = changed;
    }
    public async void Refresh()
    {
        if (!gate.Request())
            return;
        attempt = DateTime.UtcNow;
        try
        {
            var start =
                new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe"),
                                     "-NoProfile -STA -ExecutionPolicy Bypass -File \"" + Path.Combine(Store.Root, "Inventory.ps1") + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
            using var process = Process.Start(start)!;
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception err) when (err is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Store.Log("Coordinator.RefreshCatalogue : " + err.GetType().Name + ": " + err.Message);
            }
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Store.Log("Inventaire: " + await output + await error);
            if (process.ExitCode == 0 && !disposed)
            {
                Store.LoadCatalogue();
                changed();
            }
        }
        catch (Exception ex) { Store.Log("Inventaire: " + ex.Message); }
        finally { if (gate.Complete() && !disposed) Refresh(); }
    }
    // À l'ouverture de la palette : les applis Store/MSIX échappent à la surveillance du menu Démarrer, un vieux catalogue
    // est donc relu en tâche de fond.
    public void RefreshIfStale()
    {
        if (preview || disposed)
            return;
        string file = Path.Combine(Store.Root, "catalog.json");
        DateTime? written = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : null;
        if (CatalogueWatchRules.PaletteNeedsRefresh(written, attempt, DateTime.UtcNow))
            Refresh();
    }
    public void StartWatching()
    {
        if (preview)
            return;
        watcher = new AppCatalogueWatcher(System.Windows.Application.Current.Dispatcher, AddEntries, RemoveEntries, Refresh, Tick);
        watcher.Start();
    }
    // Les raccourcis vus par la surveillance s'affichent aussitôt ; l'inventaire qui suit les remplace par les entrées complètes.
    int AddEntries(List<Entry> found)
    {
        if (disposed)
            return 0;
        var added = CatalogueRules.NewEntries(Store.Entries, found);
        Store.Entries.AddRange(added);
        if (added.Count > 0)
            changed();
        return added.Count;
    }
    // Un raccourci (ou dossier) supprimé retire ses entrées aussitôt ; les entrées personnalisées des préférences sont intactes.
    int RemoveEntries(List<string> deleted)
    {
        if (disposed)
            return 0;
        int count = Store.Entries.RemoveAll(x => !Store.State.Custom.Contains(x) && CatalogueRules.IsUnderDeleted(x.Target, deleted));
        if (count > 0)
            changed();
        return count;
    }
    // Chaque minute : les ids dont le fichier a disparu sont masqués (favoris compris), et un changement de la liste des
    // paquets installés déclenche un inventaire limité en fréquence.
    void Tick()
    {
        if (preview || disposed || gate.Busy)
            return;
        var snapshot = Store.Entries.Select(x => (x.Id, x.Target)).ToArray();
        System.Threading.Tasks.Task.Run(() =>
        {
            var missing = new HashSet<string>();
            foreach (var item in snapshot)
                if (CatalogueRules.TargetGone(item.Target))
                    missing.Add(item.Id);
            int count = -1;
            try
            {
                using var key =
                    Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
                count = key?.SubKeyCount ?? -1;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
            {
                Store.Log("Coordinator.CatalogueTick : " + ex.GetType().Name + ": " + ex.Message);
            }
            System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
            {
                if (disposed)
                    return;
                bool packages = CatalogueRules.PackagesChanged(packageCount, count);
                if (count >= 0)
                    packageCount = count;
                bool missingChanged = !Store.Missing.SetEquals(missing);
                if (missingChanged)
                {
                    Store.Missing = missing;
                    changed();
                }
                if (CatalogueRules.NeedsInventory(packages, missingChanged, missing.Count))
                    watcher?.RequestRefresh();
            }));
        });
    }
    public void Dispose()
    {
        disposed = true;
        watcher?.Dispose();
    }
}
