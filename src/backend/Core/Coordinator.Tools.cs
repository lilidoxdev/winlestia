// language: C# 12, file: Coordinator.Tools.cs, target: Windows 11 / .NET 8 WPF, outils (capture, pipette, presse-papiers…) et raccourcis
// globaux
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
    const int CaptureCollapseDelayMs = 260;
    const int ToolCollapseDelayMs = 180;
    internal async void OpenCapture(bool textOnly = false)
    {
        if (QtShell != null)
        {
            QtShell.Send(textOnly ? "capture-ocr" : "capture-select");
            return;
        }
        CloseDialogs();
        CollapseImmediately();
        await Task.Delay(CaptureCollapseDelayMs);
        CaptureOverlay.Begin(this, textOnly);
    }
    internal async void OpenColorPicker()
    {
        CloseDialogs();
        CollapseImmediately();
        await Task.Delay(ToolCollapseDelayMs);
        ColorTools.OpenPicker(this);
    }
    internal async void OpenRuler()
    {
        CloseDialogs();
        CollapseImmediately();
        await Task.Delay(ToolCollapseDelayMs);
        ColorTools.OpenRuler(this);
    }
    internal void OpenMemo()
    {
        CollapseImmediately();
        PinWindow.OpenMemo(this);
    }
    internal void OpenClipboard()
    {
        if (QtShell != null)
        {
            QtShell.Send("clipboard");
            return;
        }
        OpenClipboardLegacy();
    }
    internal ClipboardService QtClipboard => clipboard;
    internal void OpenClipboardLegacy() => RunControlDialog(open ? Anchor : null, () =>
    {
        var dialog =
        new ClipboardWindow(this, clipboard) { Owner = open ? Anchor : null, ShowInTaskbar = preview };
        dialog.ShowDialog();
    });
    internal void OpenAudio()
    {
        if (QtShell != null)
        {
            QtShell.Send("audio");
            return;
        }
        OpenLegacyAudio();
    }
    void OpenLegacyAudio() => RunControlDialog(open ? Anchor : null, () =>
    {
        var dialog =
        new AudioWindow(this) { Owner = open ? Anchor : null, ShowInTaskbar = preview };
        dialog.ShowDialog();
    });
    internal void OpenWorkspaces() => RunControlDialog(open ? Anchor : null, () =>
    {
        var dialog =
        new WorkspaceWindow(this) { Owner = open ? Anchor : null, ShowInTaskbar = preview };
        dialog.ShowDialog();
    });
    internal void OpenLargeFiles() => RunControlDialog(open ? Anchor : null, () =>
    {
        var dialog =
        new LargeFilesWindow(this) { Owner = open ? Anchor : null, ShowInTaskbar = preview };
        dialog.ShowDialog();
    });
    internal void OpenTools()
    {
        if (QtShell != null)
        {
            QtShell.Send("tools");
            return;
        }
        OpenLegacyTools();
    }
    void OpenLegacyTools() => RunControlDialog(open ? Anchor : null, () =>
    {
        var dialog =
        new ToolsWindow(this) { Owner = open ? Anchor : null, ShowInTaskbar = preview };
        dialog.ShowDialog();
    });
    internal void OpenHelp()
    {
        RunControlDialog(open ? Anchor : null, () =>
        {
            var dialog = new HelpWindow(this) { Owner = open ? Anchor : null, ShowInTaskbar = preview };
            dialog.ShowDialog();
        });
    }
    internal void RefreshPaletteLabels()
    {
        if (paletteChip != null)
        {
            paletteChip.Content = PaletteLabel;
            paletteChip.ToolTip = "Commande rapide · " + PaletteLabel;
        }
        clock.RefreshLabels();
        palette?.ReloadTabs();
    }
    internal bool SetWallpaperShortcut(uint modifiers, uint key, out string error)
    {
        if (!shortcut.TrySetWallpaper(modifiers, key, out error))
            return false;
        Store.State.WallpaperShortcut = new()
        {
            Modifiers = modifiers,
            Key = key
        };
        Store.Save();
        return true;
    }
    internal bool SetPaletteShortcut(uint modifiers, uint key, out string error)
    {
        if (!shortcut.TrySetPalette(modifiers, key, out error))
            return false;
        Store.State.PaletteShortcut = new()
        {
            Modifiers = modifiers,
            Key = key
        };
        Store.Save();
        RefreshPaletteLabels();
        return true;
    }
    internal bool SetShortcut(uint modifiers, uint key, string view, out string error)
    {
        if (!shortcut.TrySet(modifiers, key, out error))
            return false;
        Store.State.Shortcut = new()
        {
            Modifiers = modifiers,
            Key = key,
            View = view
        };
        Store.State.Shortcut.Normalize();
        Store.Save();
        BuildRail();
        return true;
    }
    internal void ToggleShortcut()
    {
        if (QtShell != null)
        {
            QtShell.Send("toggle");
            return;
        }
        if (open)
        {
            Collapse();
            return;
        }
        var choice = Store.State.Shortcut.View;
        bool full = choice == "workspace" || choice == "last" && detailed,
            windows = choice == "controls" || choice == "last" && controlsExpanded,
            status = choice == "pc" || choice == "last" && pcExpanded;
        OpenView(full, windows, status);
    }
}
