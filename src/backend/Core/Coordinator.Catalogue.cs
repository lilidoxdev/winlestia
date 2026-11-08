// language: C# 12, file: Coordinator.Catalogue.cs, target: Windows 11 / .NET 8 WPF, catalogue d'applications, balayage et mises à jour
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
    internal void RefreshCatalogue() => catalogue.Refresh();
    internal void RefreshCatalogueIfStale() => catalogue.RefreshIfStale();
    void DisplayChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke((Action)(() =>
        {
            taskbar?.DisplaysChanged();
            QtShell?.DisplaysChanged();
            if (open)
                Arrange();
            else
                rail.Hide();
        }));
    }
}
