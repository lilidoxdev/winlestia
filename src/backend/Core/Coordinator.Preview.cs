// language: C# 12, file: Coordinator.Preview.cs, target: Windows 11 / .NET 8 WPF, aperçus de QA et export de la mise en page
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
    internal void ProbeRightEdge()
    {
        Collapse();
        var screen = Forms.Screen.PrimaryScreen!;
        double scale = VisualTreeHelper.GetDpi(toolbar).DpiScaleX;
        var probe =
            new Window
            {
                Title = "Panneaux Glass — test du bord",
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
    internal void ExportPreview(string file = "preview-layout.png")
    {
        try
        {
            var views = System.Windows.Application.Current.Windows.Cast<Window>().OfType<GlassWindow>().Where(w => w.IsVisible).ToList();
            File.WriteAllText(Path.Combine(Store.Root, "window-metrics.json"), JsonSerializer.Serialize(views.Select(Native.Describe),
                Store.Json));
            if (views.Count == 0)
                return;
            double left = views.Min(w => w.Left) - 20,
                top = views.Min(w => w.Top) - 20,
                right = views.Max(w => w.Left + w.Width) + 20,
                bottom = views.Max(w => w.Top + w.Height) + 20;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, right - left, bottom - top));
                foreach (var view in views)
                    dc.DrawRectangle(new VisualBrush(view.Surface), null, new Rect(view.Left - left, view.Top - top, view.ActualWidth,
                        view.ActualHeight));
            }
            var bitmap =
                new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(right - left),
                                                                    (int)Math.Ceiling(bottom - top),
                                                                    96,
                                                                    96,
                                                                    PixelFormats.Pbgra32);
            bitmap.Render(visual);
            using (visual.RenderOpen())
            {
            }
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(Store.Root, file));
            encoder.Save(stream);
            Store.Log("Export aperçu · icônes=" + Icons.LoadedCount + " · génériques=" + Icons.Failures);
        }
        catch (Exception ex) { Store.Log("Export: " + ex); }
    }
    internal bool PaletteActivity => paletteOpen;
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
