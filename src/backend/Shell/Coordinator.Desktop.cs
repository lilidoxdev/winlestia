// Desktop icons, Explorer restarts, rescue, catalogue refresh and disposal.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace PanneauxGlass.Shell;

internal sealed partial class Coordinator
{
    volatile bool desktopIconsManaged,
        desktopSuspended;
    DesktopIconsGuard? desktopGuard;
    bool taskbarDeferred;
    readonly uint explorerCreated = TaskbarNative.RegisterWindowMessage("TaskbarCreated");

    // Le Bureau d'Explorer se publie après le démarrage : un appel unique arrivait trop tôt,
    // rendait la main sans erreur, et les icônes restaient visibles pour toute la session.
    void HideDesktopIcons()
    {
        if (preview)
            return;
        desktopSuspended = false;
        int attempt = 0;
        void Apply()
        {
            try
            {
                Native.Desktop(true);
                desktopIconsManaged = true;
                if (attempt == 0)
                    Store.Log("Desktop icons hidden.");
            }
            catch (Exception ex)
            {
                attempt++;
                if (attempt < 4)
                {
                    Store.Log("Desktop hiding, retrying: " + ex.Message);
                    var retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(attempt == 1 ? 1 : 2) };
                    retry.Tick += (_, _) =>
                    {
                        retry.Stop();
                        Apply();
                    };
                    retry.Start();
                }
                else
                    Store.Log("Could not hide the Desktop: " + ex.Message);
            }
        }
        Apply();
        StartDesktopGuard();
        foreach (int delay in DesktopReasserts)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(delay) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (disposed || preview)
                    return;
                Apply();
            };
            timer.Start();
        }
    }

    static readonly int[] DesktopReasserts = { 3, 10 };

    // Hook Explorer du Bureau : réaffirme FWF_NOICONS si un moteur de fond d'écran recrée la vue. Un seul hook,
    // réinstallé après TaskbarCreated.
    void StartDesktopGuard(bool reinstall = false)
    {
        if (preview || disposed || desktopSuspended)
            return;
        desktopGuard ??= new DesktopIconsGuard(
            () => desktopIconsManaged,
            () =>
            {
                Native.Desktop(true);
                desktopIconsManaged = true;
            },
            () =>
            {
                desktopSuspended = true;
                desktopIconsManaged = false;
            }
        );
        if (reinstall || !desktopGuard.Installed)
            desktopGuard.Install();
    }

    // TaskbarCreated : Explorer vient de (re)démarrer. La barre différée au lancement est activée, le hook est
    // réinstallé et le Bureau réaffirmé.
    void ExplorerCreated()
    {
        if (preview || disposed)
            return;
        Store.Log("Explorer ready (TaskbarCreated).");
        if (taskbarDeferred)
        {
            taskbarDeferred = false;
            if (Store.State.ReplaceTaskbar && taskbar != null && !taskbar.Enable())
            {
                Store.State.ReplaceTaskbar = false;
                Store.Log("The Windows taskbar stays active: rescue unavailable.");
            }
        }
        if (desktopSuspended)
            return;
        StartDesktopGuard(true);
        desktopGuard?.Request(true);
    }

    internal void RestoreDesktopIcons()
    {
        desktopSuspended = true;
        desktopGuard?.Uninstall();
        if (!desktopIconsManaged)
            return;
        try
        {
            Native.Desktop(false);
            desktopIconsManaged = false;
            Store.Log("Desktop icons restored.");
        }
        catch (Exception ex)
        {
            Store.Log("Desktop restore: " + ex.Message);
        }
    }

    internal void Rescue()
    {
        string executable = Path.Combine(Store.Root, "SECOURS_BUREAU.exe");
        if (!File.Exists(executable))
        {
            Message(Strings.TheIndependentRescueToolIsMissing);
            return;
        }
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
    }

    internal void Message(string text)
    {
        bool previous = settingsOpen;
        settingsOpen = true;
        DialogLayers(true);
        try
        {
            var active =
                System.Windows.Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive)
                ?? DialogParent;
            System.Windows.MessageBox.Show(
                active,
                text,
                "Panneaux Glass",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        finally
        {
            settingsOpen = previous;
            DialogLayers(previous);
        }
    }

    internal async void RefreshCatalogue()
    {
        if (!catalogueGate.Request())
            return;
        catalogueAttempt = DateTime.UtcNow;
        try
        {
            var start = new ProcessStartInfo(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe"
                ),
                "-NoProfile -STA -ExecutionPolicy Bypass -File \"" + Path.Combine(Store.Root, "Inventory.ps1") + "\""
            )
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(start)!;
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch { }
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
        catch (Exception ex)
        {
            Store.Log("Inventaire: " + ex.Message);
        }
        finally
        {
            if (catalogueGate.Complete() && !disposed)
                RefreshCatalogue();
        }
    }

    // Palette opening: the Store/MSIX apps are not covered by the Start menu watch, so an old catalogue is read again
    // in the background.
    internal void RefreshCatalogueIfStale()
    {
        if (preview || disposed)
            return;
        string file = Path.Combine(Store.Root, "catalog.json");
        DateTime? written = File.Exists(file) ? File.GetLastWriteTimeUtc(file) : null;
        if (CatalogueWatchRules.PaletteNeedsRefresh(written, catalogueAttempt, DateTime.UtcNow))
            RefreshCatalogue();
    }

    // Shortcuts seen by the watcher are shown at once; the inventory that follows replaces them with the full entries.
    int AddCatalogueEntries(List<Entry> found)
    {
        if (disposed)
            return 0;
        int count = 0;
        foreach (var entry in found)
        {
            if (
                Store.Entries.Any(x =>
                    x.Id == entry.Id
                    || (x.Section == entry.Section && x.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase))
                )
            )
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

    // A deleted shortcut (or folder) takes its entries out at once; custom entries kept in the preferences are not
    // touched.
    int RemoveCatalogueEntries(List<string> deleted)
    {
        if (disposed)
            return 0;
        int count = Store.Entries.RemoveAll(x =>
            !Store.State.Custom.Contains(x)
            && deleted.Any(d =>
                x.Target.Equals(d, StringComparison.OrdinalIgnoreCase)
                || x.Target.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            )
        );
        if (count > 0)
        {
            RefreshBoards();
            QtShell?.CatalogueChanged();
        }
        return count;
    }

    void DisplayChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(
            (Action)(
                () =>
                {
                    taskbar?.DisplaysChanged();
                    QtShell?.DisplaysChanged();
                    if (open)
                        Arrange();
                    else
                        rail.Hide();
                }
            )
        );
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        catalogueWatcher?.Dispose();
        taskbar?.Dispose();
        QtShell?.Dispose();
        RestoreDesktopIcons();
        desktopGuard?.Dispose();
        dismissal.Dispose();
        Store.Save();
        timer.Stop();
        debounce.Stop();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        shortcut.Dispose();
        source.RemoveHook(Hook);
        clipboard.Dispose();
        tray.Visible = false;
        tray.Dispose();
        Icons.Dispose();
        foreach (var window in Windows.ToArray())
            window.Close();
    }
}
