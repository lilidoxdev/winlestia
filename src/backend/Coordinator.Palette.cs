// Command palette, quick tabs, previews, notifications and entry actions.

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

namespace PanneauxGlass;

internal sealed partial class Coordinator
{
    internal bool PaletteActivity => paletteOpen;
    internal bool PaletteVisible => palette?.IsVisible == true;

    internal void ClosePalette() => palette?.Close();

    internal void TogglePalette()
    {
        if (QtShell != null)
        {
            QtShell.Send("palette");
            return;
        }
        if (paletteOpen)
        {
            palette?.Close();
            return;
        }
        ShowPalette();
    }

    internal void ShowPalette()
    {
        if (QtShell != null)
        {
            QtShell.Send("palette");
            return;
        }
        if (paletteOpen)
            return;
        paletteOpen = true;
        var dialog = new CommandPalette(this) { Owner = open ? Anchor : null, ShowInTaskbar = preview };
        palette = dialog;
        DialogLayers(true);
        dialog.Closed += (_, _) =>
        {
            paletteOpen = false;
            if (ReferenceEquals(palette, dialog))
                palette = null;
            DialogLayers(settingsOpen);
            awaySince = DateTime.MinValue;
        };
        dialog.Show();
        Native.Raise(dialog);
    }

    internal void OpenFocus() =>
        RunControlDialog(
            open ? Anchor : null,
            () =>
            {
                var dialog = new FocusSetupWindow(this) { Owner = open ? Anchor : null, ShowInTaskbar = preview };
                dialog.ShowDialog();
            }
        );

    internal void EditQuickTabs(Window? parent = null)
    {
        bool previous = settingsOpen;
        settingsOpen = true;
        DialogLayers(true);
        try
        {
            var dialog = new QuickTabsWindow(this) { Owner = parent ?? Anchor, ShowInTaskbar = preview };
            dialog.ShowDialog();
        }
        finally
        {
            settingsOpen = previous;
            DialogLayers(previous);
            awaySince = DateTime.MinValue;
        }
    }

    internal void RunQuickTab(WindowsShortcut tab)
    {
        string target = tab.Target ?? "";
        if (target.StartsWith("glass:", StringComparison.OrdinalIgnoreCase))
        {
            switch (target[6..].Trim().ToLowerInvariant())
            {
                case "home":
                    ShowHome();
                    break;
                case "apps":
                    ShowWorkspace("apps");
                    break;
                case "games":
                    ShowWorkspace("games");
                    break;
                case "projects":
                    ShowWorkspace("projects");
                    break;
                case "documents":
                    ShowWorkspace("documents");
                    break;
                case "workspace":
                    ShowWorkspace();
                    break;
                case "note":
                    OpenNotes();
                    break;
                case "focus":
                    OpenFocus();
                    break;
                case "settings":
                    Settings();
                    break;
                case "tools":
                    OpenTools();
                    break;
                case "capture":
                    OpenCapture();
                    break;
                case "text":
                    OpenCapture(textOnly: true);
                    break;
                case "clipboard":
                    OpenClipboard();
                    break;
                case "audio":
                    OpenAudio();
                    break;
                case "workspaces":
                    OpenWorkspaces();
                    break;
                case "files":
                    OpenLargeFiles();
                    break;
                case "help":
                    OpenHelp();
                    break;
                case "pin":
                    ToggleAlwaysOnTop();
                    break;
                case "memo":
                    OpenMemo();
                    break;
                case "controls":
                    ToggleControls();
                    break;
                case "pc":
                    TogglePc();
                    break;
                case "palette":
                    TogglePalette();
                    break;
                default:
                    Message("Cet onglet rapide pointe vers une vue inconnue.\n" + target);
                    break;
            }
            return;
        }
        LaunchWindowsShortcut(tab);
    }

    internal void OpenNotes()
    {
        RunControlDialog(
            open ? Anchor : null,
            () =>
            {
                var dialog = new NotesWindow(this) { Owner = open ? Anchor : null, ShowInTaskbar = preview };
                dialog.ShowDialog();
            }
        );
        awaySince = DateTime.MinValue;
    }

    internal void StartFocus(int seconds) => FocusTimer.Start(this, seconds);

    internal void PreviewPalette()
    {
        ShowPalette();
        if (GlassWindow.CaptureOffset.LengthSquared > 0)
            ShowPaletteOffscreen();
    }

    // The palette centres itself on the screen; the off-screen checks put it back outside the desk.
    void ShowPaletteOffscreen()
    {
        if (palette == null)
            return;
        palette.Left = GlassWindow.CaptureOffset.X;
        palette.Top = GlassWindow.CaptureOffset.Y;
    }

    internal void PreviewPaletteQuery(string text)
    {
        ShowPalette();
        palette?.Prefill(text);
        ShowPaletteOffscreen();
    }

    internal void PreviewNotes()
    {
        var dialog = new NotesWindow(this) { Owner = home, ShowInTaskbar = true };
        dialog.ShowDialog();
    }

    internal void PreviewFocus() => OpenFocus();

    internal void PreviewTabs() => EditQuickTabs();

    internal void ShowToast(string text) => Toast.Show(text, Anchor);

    internal void Notify(string title, string text)
    {
        try
        {
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = text;
            tray.BalloonTipIcon = Forms.ToolTipIcon.Info;
            tray.ShowBalloonTip(4000);
        }
        catch (Exception ex)
        {
            Store.Log("Notification: " + ex.Message);
        }
    }

    internal bool Confirm(string text)
    {
        bool previous = settingsOpen;
        settingsOpen = true;
        DialogLayers(true);
        try
        {
            var active =
                System.Windows.Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive)
                ?? DialogParent;
            return System.Windows.MessageBox.Show(
                    active,
                    text,
                    "Panneaux Glass",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                ) == MessageBoxResult.Yes;
        }
        finally
        {
            settingsOpen = previous;
            DialogLayers(previous);
            awaySince = DateTime.MinValue;
        }
    }

    internal void WebSearch(string value, bool address = false)
    {
        try
        {
            if (address && !value.Contains("://"))
                value = "https://" + value;
            var target = address ? value : "https://duckduckgo.com/?q=" + Uri.EscapeDataString(value);
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            CollapseImmediately();
        }
        catch (Exception ex)
        {
            Message("La recherche n’a pas pu s’ouvrir.\n" + ex.Message);
        }
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

    internal void Launch(Entry entry)
    {
        try
        {
            if (entry.Target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("explorer.exe", entry.Target) { UseShellExecute = true });
            else
            {
                var start = new ProcessStartInfo(entry.Target)
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal,
                };
                if (File.Exists(entry.Target))
                    start.WorkingDirectory = Path.GetDirectoryName(entry.Target);
                Process.Start(start);
            }
            Store.RecordLaunch(entry.Id);
            Store.Save();
            Collapse();
        }
        catch (Exception ex)
        {
            Message("L’accès « " + entry.Name + " » n’a pas pu s’ouvrir.\n" + ex.Message);
        }
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
        catch (Exception ex)
        {
            Message(ex.Message);
        }
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
            using var picker = new Forms.OpenFileDialog
            {
                Title = "Choisir un fichier, une application ou un raccourci",
                Filter = "Tous les fichiers|*.*",
                Multiselect = true,
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
            using var picker = new Forms.FolderBrowserDialog
            {
                Description = "Ajouter un dossier à ton espace",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false,
            };
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
            var entry = new Entry
            {
                Id = "custom:" + Guid.NewGuid().ToString("N"),
                Name = Directory.Exists(path) ? new DirectoryInfo(path).Name : Path.GetFileNameWithoutExtension(path),
                Target = path,
                IconPath = path,
                Section = board switch
                {
                    "apps" => "Applications",
                    "games" => "Jeux",
                    "projects" => "Projets",
                    _ => "Dossiers",
                },
                Category = "Mes accès",
            };
            Store.State.Custom.Add(entry);
            Store.State.BoardOverrides[entry.Id] = board;
            Store.Entries.Add(entry);
        }
        Store.Save();
    }
}
