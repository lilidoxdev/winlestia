// language: C# 12, file: PreviewExportService.cs, target: Windows 11 / .NET 8 WPF, export PNG de la mise en page des fenêtres visibles
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace PanneauxGlass;

internal interface IPreviewExportService
{
    void Export(string file = "preview-layout.png");
    void ScheduleExport();
}

internal static class PreviewExportRules
{
    internal const double Margin = 20;
    internal static readonly TimeSpan Delay = TimeSpan.FromSeconds(5);
    // Cadre commun de toutes les fenêtres, élargi d'une marge pour que les ombres et les halos ne soient pas rognés.
    internal static Rect? CaptureBounds(System.Collections.Generic.IReadOnlyCollection<Rect> windows)
    {
        if (windows.Count == 0)
            return null;
        double left = windows.Min(w => w.Left) - Margin,
            top = windows.Min(w => w.Top) - Margin,
            right = windows.Max(w => w.Right) + Margin,
            bottom = windows.Max(w => w.Bottom) + Margin;
        return new Rect(left, top, right - left, bottom - top);
    }
    // Arrondi au pixel supérieur : un bitmap tronqué laisserait un liseré non rendu sur le bord.
    internal static (int Width, int Height) PixelSize(Rect bounds) => ((int)Math.Ceiling(bounds.Width), (int)Math.Ceiling(bounds.Height));
}

internal sealed class PreviewExportService : IPreviewExportService
{
    readonly IconLoader icons;
    internal PreviewExportService(IconLoader icons) => this.icons = icons;
    // Laisse le temps aux fenêtres de l'aperçu de s'afficher et de charger leurs icônes avant la capture.
    public void ScheduleExport()
    {
        var capture = new DispatcherTimer { Interval = PreviewExportRules.Delay };
        capture.Tick += (_, _) =>
        {
            capture.Stop();
            Export();
        };
        capture.Start();
    }
    public void Export(string file = "preview-layout.png")
    {
        try
        {
            var views = System.Windows.Application.Current.Windows.Cast<Window>().OfType<GlassWindow>().Where(w => w.IsVisible).ToList();
            File.WriteAllText(Path.Combine(Store.Root, "window-metrics.json"), JsonSerializer.Serialize(views.Select(Native.Describe),
                Store.Json));
            var frame = PreviewExportRules.CaptureBounds(views.Select(w => new Rect(w.Left, w.Top, w.Width, w.Height)).ToList());
            if (frame == null)
                return;
            var bounds = frame.Value;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, bounds.Width, bounds.Height));
                foreach (var view in views)
                    dc.DrawRectangle(new VisualBrush(view.Surface), null, new Rect(view.Left - bounds.Left, view.Top - bounds.Top,
                        view.ActualWidth, view.ActualHeight));
            }
            var (width, height) = PreviewExportRules.PixelSize(bounds);
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            using (visual.RenderOpen())
            {
            }
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(Store.Root, file));
            encoder.Save(stream);
            Store.Log("Export aperçu · icônes=" + icons.LoadedCount + " · génériques=" + icons.Failures);
        }
        catch (Exception ex) { Store.Log("Export: " + ex); }
    }
}
