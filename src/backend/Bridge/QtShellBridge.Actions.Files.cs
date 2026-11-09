// language: C# 12, file: QtShellBridge.Actions.Files.cs, target: Windows 11 / .NET 8, actions du pont : fichiers, corbeille, bureaux
// virtuels, dock et recherche de fichiers
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Devices.Radios;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    [BridgeAction("clipboardFiles")]
    void OnClipboardFiles(BridgeCall c)
    {
        if (c.Args.TryGetProperty("paths", out var clipPaths))
        {
            RecycleActions.ClipboardFiles(clipPaths.EnumerateArray().Select(p => p.GetString() ?? "").ToArray());
            c.Notice = "Fichiers copiés dans le presse-papiers.";
        }
    }

    [BridgeAction("zipFiles")]
    async Task OnZipFiles(BridgeCall c)
    {
        if (c.Args.TryGetProperty("paths", out var zipPaths))
        {
            var files = zipPaths.EnumerateArray().Select(p => p.GetString() ?? "").ToArray();
            string? destination = RecycleActions.ChooseZip(files);
            if (destination != null)
            {
                await Task.Run(() => RecycleActions.Zip(files, destination));
                c.Notice = "Archive ZIP créée.";
            }
        }
    }

    [BridgeAction("explorerPalette")]
    async Task OnExplorerPalette(BridgeCall c)
    {
        c.Notice = null;
        await Task.Run(() => shellPort.SyncExplorerPalette(Text(c.Args, "accent"), Text(c.Args, "background"), Text(c.Args, "text")));
    }

    [BridgeAction("explorerFolder")]
    void OnExplorerFolder(BridgeCall c)
    {
        string? picked = shellPort.PickFolder();
        if (picked != null && !Store.State.ExplorerFolders.Contains(picked))
        {
            Store.State.ExplorerFolders.Add(picked);
            Store.Save();
        }
    }

    [BridgeAction("explorerRestore")]
    async Task OnExplorerRestore(BridgeCall c)
    {
        Store.State.ExplorerTheme = false;
        Store.Save();
        c.Notice = await Task.Run(() => shellPort.RestoreExplorer());
    }

    [BridgeAction("explorerReport")]
    void OnExplorerReport(BridgeCall c)
    {
        owner.LaunchTarget(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                        "Winlestia",
                                        "Explorer",
                                        "last-report.txt"));
    }

    [BridgeAction("explorerOpen")]
    void OnExplorerOpen(BridgeCall c)
    {
        owner.LaunchTarget(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
    }

    [BridgeAction("emptyRecycleBin")]
    void OnEmptyRecycleBin(BridgeCall c)
    {
        shellPort.EmptyRecycleBin();
    }

    [BridgeAction("desktopSwitch")]
    void OnDesktopSwitch(BridgeCall c)
    {
        int rel = (int)Math.Clamp(Number(c.Args, "relative"), -1, 1), to = (int)Number(c.Args, "index");
        _ = Task.Run(() => desktopPort.SwitchDesktop(to, rel));
    }

    [BridgeAction("desktopNew")]
    void OnDesktopNew(BridgeCall c)
    {
        _ = Task.Run(() => desktopPort.NewDesktop());
    }

    [BridgeAction("desktopClose")]
    void OnDesktopClose(BridgeCall c)
    {
        int at = (int)Number(c.Args, "index") - 1;
        _ = Task.Run(() => desktopPort.CloseDesktop(at));
    }

    [BridgeAction("recycleFiles")]
    void OnRecycleFiles(BridgeCall c)
    {
        if (c.Args.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Array)
            RecycleActions.Recycle(paths.EnumerateArray().Select(p => p.GetString() ?? "").ToArray());
    }

    [BridgeAction("dockApp")]
    void OnDockApp(BridgeCall c)
    {
        c.Notice = DockAction(c.Args);
        nextTasks = DateTime.MinValue;
    }

    [BridgeAction("dockPreview")]
    void OnDockPreview(BridgeCall c)
    {
        DockPreviewOpen(c.Args);
    }

    [BridgeAction("dockPreviewEnd")]
    void OnDockPreviewEnd(BridgeCall c)
    {
        DockPreviewEnd(c.Args);
    }

    [BridgeAction("runningRefresh")]
    void OnRunningRefresh(BridgeCall c)
    {
        RefreshBackgroundApps();
        nextTasks = DateTime.MinValue;
    }

    [BridgeAction("openFiles")]
    void OnOpenFiles(BridgeCall c)
    {
        shellPort.OpenFilesHandler();
    }

    [BridgeAction("windowList")]
    async Task OnWindowList(BridgeCall c)
    {
        c.WindowItems = await Task.Run(() => desktopPort.PaletteWindows().Take(60).ToArray());
    }

    [BridgeAction("fileSearch")]
    void OnFileSearch(BridgeCall c)
    {
        fileSearch.Start(Text(c.Args, "term"),
                         (long)Number(c.Args, "gen"),
                         () => _ = dispatcher.BeginInvoke((Action)(() => { if (!disposed) Send("files"); })));
    }

    [BridgeAction("fileResults")]
    void OnFileResults(BridgeCall c)
    {
        c.FileItems = fileSearch.Payload();
    }

    [BridgeAction("fileCancel")]
    void OnFileCancel(BridgeCall c)
    {
        fileSearch.Cancel();
    }

    [BridgeAction("fileOpen")]
    void OnFileOpen(BridgeCall c)
    {
        if (fileSearch.TryResolve((long)Number(c.Args, "gen"), (int)Number(c.Args, "index"), out var filePath))
        {
            try
            {
                if (FileQuery.IsExecutable(filePath))
                {
                    var selectInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                    selectInfo.ArgumentList.Add("/select," + filePath.Replace("\"", ""));
                    shellPort.StartProcess(selectInfo);
                }
                else
                    shellPort.StartProcess(new ProcessStartInfo(filePath) { UseShellExecute = true });
            }
            catch { c.Notice = "Impossible d’ouvrir ce fichier."; }
        }
    }
}
