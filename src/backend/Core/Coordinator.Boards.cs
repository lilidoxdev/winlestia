// language: C# 12, file: Coordinator.Boards.cs, target: Windows 11 / .NET 8 WPF, sites, panneaux et entrées (lancement, édition, ajout)
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
    internal void RefreshWebsites() => websites.Refresh();
    internal void AddWebsite(WebShortcut? site = null, bool chooseImage = false)
    {
        bool previous = settingsOpen;
        settingsOpen = true;
        DialogLayers(true);
        try
        {
            var dialog = new WebShortcutWindow(this, site, chooseImage) { Owner = DialogParent, ShowInTaskbar = preview };
            dialog.ShowDialog();
        }
        finally
        {
            settingsOpen = previous;
            DialogLayers(previous);
            awaySince = DateTime.MinValue;
        }
    }
    internal void RefreshBoards()
    {
        foreach (var board in Boards.Where(b => b.IsVisible))
            board.Refresh(false);
        if (home.IsVisible)
            home.Refresh();
        if (detailed && open)
            websites.Refresh();
    }
    internal bool HandleShortcut(KeyEventArgs e)
    {
        if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!paletteOpen)
                TogglePalette();
            return true;
        }
        if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowHome();
            return true;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is >= Key.D1 and <= Key.D4)
        {
            string[] ids = { "apps", "games", "projects", "documents" };
            ShowWorkspace(ids[(int)(e.Key - Key.D1)]);
            return true;
        }
        return false;
    }
    // L'« Explorateur de fichiers » Windows épinglé (lnk ou explorer.exe nu) ouvre Winlestia Fichiers quand il est le gestionnaire par
    // défaut.
    static bool IsPlainExplorer(string t)
    {
        var n = Path.GetFileName(t ?? "");
        return n.Equals("File Explorer.lnk", StringComparison.OrdinalIgnoreCase)
            || n.Equals("Explorateur de fichiers.lnk", StringComparison.OrdinalIgnoreCase)
            || n.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
    }
    internal void Launch(Entry entry)
    {
        try
        {
            if (Store.State.FilesDefaultHandler && IsPlainExplorer(entry.Target) && File.Exists(FilesDefaultHandler.ExePath))
            {
                FilesDefaultHandler.Open();
                Store.RecordLaunch(entry.Id);
                Store.Save();
                Collapse();
                return;
            }
            if (entry.Target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("explorer.exe", entry.Target) { UseShellExecute = true });
            else
            {
                var start = new ProcessStartInfo(entry.Target) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal };
                if (File.Exists(entry.Target))
                    start.WorkingDirectory = Path.GetDirectoryName(entry.Target);
                Process.Start(start);
            }
            Store.RecordLaunch(entry.Id);
            Store.Save();
            Collapse();
        }
        catch (Exception ex) { Message("L’accès « " + entry.Name + " » n’a pas pu s’ouvrir.\n" + ex.Message); }
    }
    internal void Reveal(Entry e)
    {
        try
        {
            if (Directory.Exists(e.Target))
                Process.Start("explorer.exe", "\"" + e.Target + "\"");
            else if (File.Exists(e.Target))
                Process.Start("explorer.exe", "/select,\"" + e.Target + "\"");
            else
                Message("Cet accès utilise un protocole de lancement Windows.");
        }
        catch (Exception ex) { Message(ex.Message); }
    }
    internal void Settings(string? board = null)
    {
        if (QtShell != null)
        {
            QtShell.Send("settings");
            return;
        }
        if (settingsOpen)
            return;
        if (!open && QtShell == null)
            Open();
        settingsOpen = true;
        DialogLayers(true);
        try
        {
            var dialog = new PersonalizeWindow(this, board) { Owner = DialogParent, ShowInTaskbar = preview };
            dialog.ShowDialog();
        }
        finally
        {
            settingsOpen = false;
            DialogLayers(false);
            awaySince = DateTime.MinValue;
        }
    }
    internal void Edit(Entry e)
    {
        bool previous = settingsOpen;
        settingsOpen = true;
        DialogLayers(true);
        try
        {
            var dialog = new EditWindow(this, e, false) { Owner = DialogParent, ShowInTaskbar = preview };
            dialog.ShowDialog();
        }
        finally
        {
            settingsOpen = previous;
            DialogLayers(previous);
        }
    }
    internal void AddAccess(string board)
    {
        bool previous = settingsOpen;
        settingsOpen = true;
        DialogLayers(true);
        try
        {
            using var picker =
                new Forms.OpenFileDialog
                {
                    Title = "Choisir un fichier, une application ou un raccourci",
                    Filter = "Tous les fichiers|*.*",
                    Multiselect = true
                };
            if (picker.ShowDialog(new DialogOwner(DialogParent)) == Forms.DialogResult.OK)
                foreach (string path in picker.FileNames)
                    AddPath(path, board);
        }
        finally
        {
            settingsOpen = previous;
            DialogLayers(previous);
        }
        RefreshBoards();
    }
    internal void AddFolder(string board)
    {
        bool previous = settingsOpen;
        settingsOpen = true;
        DialogLayers(true);
        try
        {
            using var picker =
                new Forms.FolderBrowserDialog { Description = "Ajouter un dossier à ton espace", UseDescriptionForTitle = true,
                    ShowNewFolderButton = false };
            if (picker.ShowDialog(new DialogOwner(DialogParent)) == Forms.DialogResult.OK)
                AddPath(picker.SelectedPath, board);
        }
        finally
        {
            settingsOpen = previous;
            DialogLayers(previous);
        }
        RefreshBoards();
    }
    internal void AddPath(string path, string board)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return;
        var existing = Store.Entries.FirstOrDefault(e => e.Target.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            Store.State.BoardOverrides[existing.Id] = board;
            Store.State.Hidden.Remove(existing.Id);
        }
        else
        {
            var entry =
                new Entry
                {
                    Id = "custom:" + Guid.NewGuid().ToString("N"),
                    Name = Directory.Exists(path) ? new DirectoryInfo(path).Name : Path.GetFileNameWithoutExtension(path),
                    Target = path,
                    IconPath = path,
                    Section = board switch { "apps" => "Applications", "games" => "Jeux", "projects" => "Projets", _ => "Dossiers" },
                    Category = "Mes accès"
                };
            Store.State.Custom.Add(entry);
            Store.State.BoardOverrides[entry.Id] = board;
            Store.Entries.Add(entry);
        }
        Store.Save();
    }
    volatile bool desktopIconsManaged, desktopSuspended;
    DesktopIconsGuard? desktopGuard;
    bool taskbarDeferred;
}
