// language: C# 12, file: Coordinator.Dialogs.cs, target: Windows 11 / .NET 8 WPF, boîtes de dialogue des contrôles et raccourcis Windows
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
    internal void RunControlDialog(Window? parent, Action action)
    {
        bool previous = settingsOpen;
        settingsOpen = true;
        DialogLayers(true);
        try
        {
            action();
        }
        finally
        {
            settingsOpen = previous;
            DialogLayers(previous);
            awaySince = DateTime.MinValue;
        }
    }
    internal async Task RunControlTask(Func<Task> action)
    {
        bool previous = settingsOpen;
        settingsOpen = true;
        DialogLayers(true);
        try
        {
            await action();
        }
        finally
        {
            settingsOpen = previous;
            DialogLayers(previous);
            awaySince = DateTime.MinValue;
        }
    }
    internal void EditControls(Window parent, bool add = false) => RunControlDialog(parent, () =>
    {
        var dialog =
        new ControlEditorWindow(this, add) { Owner = parent, ShowInTaskbar = preview };
        dialog.ShowDialog();
    });
    const int ScreenClipDelayMs = 120;
    internal async void LaunchWindowsShortcut(WindowsShortcut shortcut)
    {
        CollapseImmediately();
        try
        {
            if (shortcut.Target.StartsWith("ms-screenclip:", StringComparison.OrdinalIgnoreCase))
                await System.Threading.Tasks.Task.Delay(ScreenClipDelayMs);
            Process.Start(new ProcessStartInfo(shortcut.Target) { Arguments = shortcut.Arguments, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Open();
            Message("Le raccourci « " + shortcut.Name + " » n’a pas pu s’ouvrir.\n" + ex.Message);
        }
    }
}
