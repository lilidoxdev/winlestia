// language: C# 12, file: Coordinator.Desktop.cs, target: Windows 11 / .NET 8 WPF, icônes du bureau, reprise après redémarrage
// d'Explorer
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
    // TaskbarCreated : Explorer vient de (re)démarrer. La barre différée au lancement est activée, puis le service du Bureau
    // réinstalle son hook.
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
        desktopIcons.OnExplorerCreated();
    }
    internal void RestoreDesktopIcons() => desktopIcons.Restore();
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
