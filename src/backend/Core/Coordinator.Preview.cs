// language: C# 12, file: Coordinator.Preview.cs, target: Windows 11 / .NET 8 WPF, aperçus de QA et export de la mise en page

using System;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;

internal sealed partial class Coordinator
{
    internal void ProbeRightEdge()
    {
        Collapse();
        var screen = Forms.Screen.PrimaryScreen!;
        double scale = VisualTreeHelper.GetDpi(toolbar).DpiScaleX;
        var probe =
            new Window
            {
                Title = "Winlestia — test du bord",
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = true,
                Topmost = true,
                Width = 6,
                Height = 90,
                Left = screen.Bounds.Right / scale - 6,
                Top = (screen.WorkingArea.Top + screen.WorkingArea.Height / 2) / scale,
                Background = Brushes.SlateGray
            };
        probe.Show();
    }
    internal void ExportPreview(string file = "preview-layout.png") => previewExport.Export(file);
    internal bool PaletteActivity => state.PaletteOpen;
    internal bool PaletteVisible => palette?.IsVisible == true;
    internal void PreviewPalette()
    {
        ShowPalette();
        if (GlassWindow.CaptureOffset.LengthSquared > 0)
            ShowPaletteOffscreen();
    }
    // La palette se centre seule sur l'écran ; les contrôles hors écran la ramènent hors du bureau.
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
}
