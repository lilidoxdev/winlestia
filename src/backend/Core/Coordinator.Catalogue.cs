// language: C# 12, file: Coordinator.Catalogue.cs, target: Windows 11 / .NET 8 WPF, catalogue d'applications, balayage et mises à jour
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;

internal sealed partial class Coordinator
{
    internal async void RefreshCatalogue()
    {
        if (!catalogueGate.Request())
            return;
        catalogueAttempt = DateTime.UtcNow;
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
                RefreshBoards();
                QtShell?.CatalogueChanged();
            }
        }
        catch (Exception ex) { Store.Log("Inventaire: " + ex.Message); }
        finally { if (catalogueGate.Complete() && !disposed) RefreshCatalogue(); }
    }
    // À l'ouverture de la palette : les applis Store/MSIX échappent à la surveillance du menu Démarrer, un vieux catalogue est donc relu
    // en tâche de fond.
    internal void RefreshCatalogueIfStale()
    {
        if (preview || disposed)
            return;
        string file = Path.Combine(Store.Root, "catalog.json");
        DateTime? written = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : null;
        if (CatalogueWatchRules.PaletteNeedsRefresh(written, catalogueAttempt, DateTime.UtcNow))
            RefreshCatalogue();
    }
    // Les raccourcis vus par la surveillance s'affichent aussitôt ; l'inventaire qui suit les remplace par les entrées complètes.
    int AddCatalogueEntries(List<Entry> found)
    {
        if (disposed)
            return 0;
        int count = 0;
        foreach (var entry in found)
        {
            if (Store.Entries.Any(x => x.Id == entry.Id || (x.Section == entry.Section && x.Name.Equals(entry.Name,
                StringComparison.OrdinalIgnoreCase))))
                continue;
            Store.Entries.Add(entry);
            count++;
        }
        if (count > 0)
        {
            RefreshBoards();
            QtShell?.CatalogueChanged();
        }
        return count;
    }
    int packageCount = -1;
    static bool TargetGone(string target)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(target)
                || target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                || target.Contains("://")
                || target.StartsWith(@"\\", StringComparison.Ordinal)
                || !Path.IsPathRooted(target)
                || Path.GetExtension(target).Length == 0)
                return false;
            return !File.Exists(target) && !Directory.Exists(target);
        }
        catch { return false; }
    }
    // Chaque minute : les ids dont le fichier a disparu sont masqués (favoris compris), et un changement de la liste des
    // paquets installés déclenche un inventaire limité en fréquence.
    void CatalogueTick()
    {
        if (preview || disposed || catalogueGate.Busy)
            return;
        var snapshot = Store.Entries.Select(x => (x.Id, x.Target)).ToArray();
        System.Threading.Tasks.Task.Run(() =>
        {
            var missing = new HashSet<string>();
            foreach (var item in snapshot)
                if (TargetGone(item.Target))
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
                bool packages = packageCount >= 0 && count >= 0 && count != packageCount;
                if (count >= 0)
                    packageCount = count;
                bool changed = !Store.Missing.SetEquals(missing);
                if (changed)
                {
                    Store.Missing = missing;
                    RefreshBoards();
                    QtShell?.CatalogueChanged();
                }
                if (packages || (changed && missing.Count > 0))
                    catalogueWatcher?.RequestRefresh();
            }));
        });
    }
    // Un raccourci (ou dossier) supprimé retire ses entrées aussitôt ; les entrées personnalisées des préférences sont intactes.
    int RemoveCatalogueEntries(List<string> deleted)
    {
        if (disposed)
            return 0;
        int count =
            Store.Entries.RemoveAll(x =>
                !Store.State.Custom.Contains(x)
                    && deleted.Any(d =>
                        x.Target.Equals(d, StringComparison.OrdinalIgnoreCase)
                            || x.Target.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
        if (count > 0)
        {
            RefreshBoards();
            QtShell?.CatalogueChanged();
        }
        return count;
    }
    void DisplayChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke((Action)(() =>
        {
            taskbar?.DisplaysChanged();
            QtShell?.DisplaysChanged();
            if (open)
                Arrange();
            else
                rail.Hide();
        }));
    }
}
