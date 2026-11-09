// language: C# 12, file: Coordinator.Catalogue.cs, target: Windows 11 / .NET 8 WPF, catalogue d'applications, balayage et mises à jour

using System;
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
            if (state.Open)
                Arrange();
            else
                rail.Hide();
        }));
    }
}
