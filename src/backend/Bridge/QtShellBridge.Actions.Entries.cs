// language: C# 12, file: QtShellBridge.Actions.Entries.cs, target: Windows 11 / .NET 8, actions du pont : applications, catalogue,
// presse-papiers et outils
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
    [BridgeAction("systemAction")]
    void OnSystemAction(BridgeCall c)
    {
        DockSystem(Text(c.Args, "id"), Flag(c.Args, "confirmed"));
    }

    [BridgeAction("icon")]
    void OnIcon(BridgeCall c)
    {
        var iconEntry = Store.Entries.FirstOrDefault(e => e.Id == c.Id) ?? dockEntries.GetValueOrDefault(c.Id);
        if (iconEntry == null)
        {
            lock (DesktopRecents.Entries)
                DesktopRecents.Entries.TryGetValue(c.Id, out iconEntry);
        }
        if (iconEntry != null)
            LoadIcon(iconEntry);
    }

    [BridgeAction("launch")]
    void OnLaunch(BridgeCall c)
    {
        var entry = Store.Visible().FirstOrDefault(e => e.Id == c.Id);
        if (entry != null)
        {
            Send("close");
            owner.Launch(entry);
        }
    }

    [BridgeAction("activate")]
    void OnActivate(BridgeCall c)
    {
        long hwnd = (long)Number(c.Args, "hwnd");
        var app = TaskbarNative.Applications().FirstOrDefault(w => w.Handle.ToInt64() == hwnd);
        if (app != null)
            TaskbarNative.Activate(app);
        else if (dockEntries.TryGetValue(c.Id, out var pin))
            owner.Launch(pin);
    }

    [BridgeAction("palette")]
    void OnPalette(BridgeCall c)
    {
        Send("palette");
    }

    [BridgeAction("copyText")]
    void OnCopyText(BridgeCall c)
    {
        string copied = Text(c.Args, "text");
        if (copied.Length <= 512)
            System.Windows.Clipboard.SetText(copied);
    }

    [BridgeAction("paletteRun")]
    void OnPaletteRun(BridgeCall c)
    {
        RunPalette(Text(c.Args, "kind"), c.Id, Text(c.Args, "query"));
    }

    [BridgeAction("clipboardCopy")]
    void OnClipboardCopy(BridgeCall c)
    {
        var clip = owner.QtClipboard.Items.FirstOrDefault(e => e.Id == c.Id);
        if (clip != null)
            owner.QtClipboard.CopyBack(clip);
    }

    [BridgeAction("clipboardPin")]
    void OnClipboardPin(BridgeCall c)
    {
        var pinnedClip = owner.QtClipboard.Items.FirstOrDefault(e => e.Id == c.Id);
        if (pinnedClip != null)
            owner.QtClipboard.TogglePin(pinnedClip);
    }

    [BridgeAction("refreshCatalogue")]
    void OnRefreshCatalogue(BridgeCall c)
    {
        owner.RefreshCatalogue();
    }

    [BridgeAction("move")]
    void OnMove(BridgeCall c)
    {
        string board = Text(c.Args, "board");
        if (board is "apps" or "games" or "projects" or "documents" && Store.Entries.Any(e => e.Id == c.Id))
        {
            Store.State.BoardOverrides[c.Id] = board;
            Store.Save();
        }
    }

    [BridgeAction("editEntry")]
    void OnEditEntry(BridgeCall c)
    {
        var edited = Store.Entries.FirstOrDefault(e => e.Id == c.Id);
        if (edited != null)
        {
            string name = Text(c.Args, "name").Trim(), category = Text(c.Args, "category").Trim();
            if (name.Length is > 0 and <= 120)
            {
                edited.Name = name;
                Store.State.Names[c.Id] = name;
            }
            if (category.Length <= 100)
            {
                edited.Category = category;
                Store.State.Categories[c.Id] = category;
            }
            Store.Save();
        }
    }

    [BridgeAction("entryIcon")]
    void OnEntryIcon(BridgeCall c)
    {
        var changedIcon = Store.Entries.FirstOrDefault(e => e.Id == c.Id);
        string path = Text(c.Args, "path");
        if (changedIcon != null
            && File.Exists(path)
            && new[] { ".png", ".ico", ".jpg", ".jpeg", ".webp" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            changedIcon.IconPath = path;
            Store.State.Icons[c.Id] = path;
            Store.Save();
            requested.Remove(c.Id);
            LoadIcon(changedIcon);
        }
    }

    [BridgeAction("addAccess")]
    void OnAddAccess(BridgeCall c)
    {
        Send("close");
        string addBoard = Text(c.Args, "board");
        _ = dispatcher.BeginInvoke((Action)(() =>
            WithDialog(() => owner.AddAccess(addBoard is "apps" or "games" or "projects" or "documents" ? addBoard : "apps"))));
    }

    [BridgeAction("addFolder")]
    void OnAddFolder(BridgeCall c)
    {
        Send("close");
        string folderBoard = Text(c.Args, "board");
        _ = dispatcher.BeginInvoke((Action)(() =>
            WithDialog(() => owner.AddFolder(folderBoard is "apps" or "games" or "projects" or "documents" ? folderBoard : "documents"))));
    }

    [BridgeAction("tools")]
    void OnTools(BridgeCall c)
    {
        Send("tools");
    }

    [BridgeAction("toolRun")]
    void OnToolRun(BridgeCall c)
    {
        RunTool(c.Id);
    }

    [BridgeAction("captureExport")]
    async Task OnCaptureExport(BridgeCall c)
    {
        c.Notice = await CaptureExport(c.Args);
    }

    [BridgeAction("clipboard")]
    void OnClipboard(BridgeCall c)
    {
        Send("clipboard");
    }

    [BridgeAction("profiles")]
    void OnProfiles(BridgeCall c)
    {
        Send("close");
        _ = dispatcher.BeginInvoke((Action)(() => WithDialog(owner.OpenWorkspaces)));
    }

    [BridgeAction("websites")]
    void OnWebsites(BridgeCall c)
    {
        Send("close");
        _ = dispatcher.BeginInvoke((Action)(() => WithDialog(() => owner.AddWebsite())));
    }

    [BridgeAction("capture")]
    void OnCapture(BridgeCall c)
    {
        Send("capture");
    }

    [BridgeAction("restoreTaskbar")]
    void OnRestoreTaskbar(BridgeCall c)
    {
        owner.SetTaskbar(false);
    }

    [BridgeAction("quit")]
    void OnQuit(BridgeCall c)
    {
        _ = dispatcher.BeginInvoke((Action)(() => System.Windows.Application.Current.Shutdown()));
    }

    [BridgeAction("favorite")]
    void OnFavorite(BridgeCall c)
    {
        if (Store.Entries.Any(e => e.Id == c.Id))
        {
            if (!Store.State.Favorites.Remove(c.Id))
                Store.State.Favorites.Add(c.Id);
            Store.Save();
        }
    }

    [BridgeAction("reveal")]
    void OnReveal(BridgeCall c)
    {
        var reveal = Store.Entries.FirstOrDefault(e => e.Id == c.Id);
        if (reveal != null)
            owner.Reveal(reveal);
    }

    [BridgeAction("edit")]
    void OnEdit(BridgeCall c)
    {
        var edit = Store.Entries.FirstOrDefault(e => e.Id == c.Id);
        if (edit != null)
            _ = dispatcher.BeginInvoke((Action)(() => WithDialog(() => owner.Edit(edit))));
    }

    [BridgeAction("website")]
    void OnWebsite(BridgeCall c)
    {
        var site = Store.State.Websites.FirstOrDefault(w => w.Id == c.Id);
        if (site != null)
            owner.LaunchTarget(site.Url);
    }

    [BridgeAction("raiseMedia")]
    void OnRaiseMedia(BridgeCall c)
    {
        var target = Store.Entries.FirstOrDefault(e => e.Name.Equals(player?.App, StringComparison.OrdinalIgnoreCase));
        if (target != null)
            owner.Launch(target);
    }
}
