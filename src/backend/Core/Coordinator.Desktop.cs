// language: C# 12, file: Coordinator.Desktop.cs, target: Windows 11 / .NET 8 WPF, icônes du bureau et reprise après redémarrage d'Explorer
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
                    Store.Log("Icônes du Bureau masquées.");
            }
            catch (Exception ex)
            {
                attempt++;
                if (attempt < 4)
                {
                    Store.Log("Masquage du Bureau, nouvel essai: " + ex.Message);
                    var retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(attempt == 1 ? 1 : 2) };
                    retry.Tick += (_, _) =>
                    {
                        retry.Stop();
                        Apply();
                    };
                    retry.Start();
                }
                else
                    Store.Log("Masquage du Bureau impossible: " + ex.Message);
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
    // Hook Explorer du Bureau : réaffirme FWF_NOICONS si un moteur de fond d'écran recrée la vue. Un seul hook, réinstallé après
    // TaskbarCreated.
    void StartDesktopGuard(bool reinstall = false)
    {
        if (preview || disposed || desktopSuspended)
            return;
        desktopGuard ??= new DesktopIconsGuard(() => desktopIconsManaged, () =>
        {
            Native.Desktop(true);
            desktopIconsManaged = true;
        }, () =>
        {
            desktopSuspended = true;
            desktopIconsManaged = false;
        });
        if (reinstall || !desktopGuard.Installed)
            desktopGuard.Install();
    }
    // TaskbarCreated : Explorer vient de (re)démarrer. La barre différée au lancement est activée, le hook est réinstallé et le Bureau
    // réaffirmé.
    void ExplorerCreated()
    {
        if (preview || disposed)
            return;
        Store.Log("Explorateur prêt (TaskbarCreated).");
        if (taskbarDeferred)
        {
            taskbarDeferred = false;
            if (Store.State.ReplaceTaskbar && taskbar != null && !taskbar.Enable())
            {
                Store.State.ReplaceTaskbar = false;
                Store.Log("La barre Windows reste active : secours indisponible.");
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
            Store.Log("Icônes du Bureau rétablies.");
        }
        catch (Exception ex) { Store.Log("Rétablissement du Bureau: " + ex.Message); }
    }
    internal void Rescue()
    {
        string executable = Path.Combine(Store.Root, "SECOURS_BUREAU.exe");
        if (!File.Exists(executable))
        {
            Message("Le secours indépendant est absent.\nDocuments\\SECOURS_BUREAU.exe peut également être utilisé.");
            return;
        }
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
    }
}
