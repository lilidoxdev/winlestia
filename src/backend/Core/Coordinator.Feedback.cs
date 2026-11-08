// language: C# 12, file: Coordinator.Feedback.cs, target: Windows 11 / .NET 8 WPF, toasts, notifications et boîtes de message
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
    internal void ShowToast(string text) => Toast.Show(text, Anchor);
    internal void Notify(string title, string text) => tray.Notify(title, text);
    internal bool Confirm(string text)
    {
        bool previous = settingsOpen;
        settingsOpen = true;
        DialogLayers(true);
        try
        {
            var active = System.Windows.Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive) ?? DialogParent;
            return System.Windows.MessageBox.Show(active, text, "Panneaux Glass", MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;
        }
        finally
        {
            settingsOpen = previous;
            DialogLayers(previous);
            awaySince = DateTime.MinValue;
        }
    }
    internal void Message(string text)
    {
        bool previous = settingsOpen;
        settingsOpen = true;
        DialogLayers(true);
        try
        {
            var active = System.Windows.Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive) ?? DialogParent;
            System.Windows.MessageBox.Show(active, text, "Panneaux Glass", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            settingsOpen = previous;
            DialogLayers(previous);
        }
    }
}
