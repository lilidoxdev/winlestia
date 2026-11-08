// language: C# 12, file: Coordinator.Palette.cs, target: Windows 11 / .NET 8 WPF, palette de recherche, onglets rapides et notes
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
    internal void OpenFocus() => RunControlDialog(open ? Anchor : null, () =>
    {
        var dialog =
        new FocusSetupWindow(this) { Owner = open ? Anchor : null, ShowInTaskbar = preview };
        dialog.ShowDialog();
    });
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
        RunControlDialog(open ? Anchor : null, () =>
        {
            var dialog = new NotesWindow(this) { Owner = open ? Anchor : null, ShowInTaskbar = preview };
            dialog.ShowDialog();
        });
        awaySince = DateTime.MinValue;
    }
    internal void StartFocus(int seconds) => FocusTimer.Start(this, seconds);
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
        catch (Exception ex) { Message("La recherche n’a pas pu s’ouvrir.\n" + ex.Message); }
    }
}
