// C# 12, fichier CaptureTools.cs, cible Windows 11 / .NET 8 WPF, capture, annotation, papier épinglé, OCR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using BitmapFrame = System.Windows.Media.Imaging.BitmapFrame;
using WinBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace PanneauxGlass;

internal static class ScreenShot
{
    internal static System.Drawing.Rectangle VirtualBounds() => System.Windows.Forms.SystemInformation.VirtualScreen;
    internal static BitmapSource Capture(System.Drawing.Rectangle bounds)
    {
        using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size, System.Drawing.CopyPixelOperation.SourceCopy);
        IntPtr handle = bitmap.GetHbitmap();
        try
        {
            var source =
                System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally { Native.DeleteObject(handle); }
    }
    internal static byte[] Png(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
    internal static void Save(BitmapSource image)
    {
        var dialog =
            new SaveFileDialog
            {
                Title = "Enregistrer la capture",
                Filter = "Image PNG|*.png",
                FileName = "capture-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png"
            };
        if (dialog.ShowDialog() != true)
            return;
        using var stream = File.Create(dialog.FileName);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
    }
}
// Fige l'écran, laisse dj cadrer une zone, puis l'annoter directement sur les pixels.
internal sealed partial class CaptureOverlay : Window
{
    static CaptureOverlay? current;
    readonly Coordinator owner;
    readonly BitmapSource screen;
    readonly double scaleX, scaleY;
    readonly bool textOnly;
    readonly Grid root = new();
    readonly Image background;
    readonly System.Windows.Shapes.Path shade = new();
    readonly Border frame = new();
    readonly TextBlock sizeLabel;
    InkCanvas? ink;
    Border? tools;
    bool drawing, annotating;
    Point origin;
    Rect selection = new();
    internal static void Begin(Coordinator owner, bool textOnly = false)
    {
        if (current != null)
        {
            current.Activate();
            return;
        }
        try
        {
            current = new CaptureOverlay(owner, textOnly);
            current.Show();
            Native.Raise(current);
        }
        catch (Exception ex)
        {
            current = null;
            owner.Message("La capture n’a pas pu démarrer.\n" + ex.Message);
        }
    }
    CaptureOverlay(Coordinator coordinator, bool textOnly)
    {
        owner = coordinator;
        this.textOnly = textOnly;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        var bounds = ScreenShot.VirtualBounds();
        screen = ScreenShot.Capture(bounds);
        scaleX = screen.PixelWidth / Math.Max(1, Width);
        scaleY = screen.PixelHeight / Math.Max(1, Height);
        Content = root;
        root.Children.Add(background = new Image { Source = screen, Stretch = Stretch.Fill });
        shade.Fill = new SolidColorBrush(Color.FromArgb(120, 4, 8, 14));
        shade.IsHitTestVisible = false;
        root.Children.Add(shade);
        frame.BorderBrush = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
        frame.BorderThickness = new Thickness(1);
        frame.Background = Brushes.Transparent;
        frame.IsHitTestVisible = false;
        root.Children.Add(frame);
        sizeLabel = UI.Text("", 12, UI.White);
        sizeLabel.Background = new SolidColorBrush(Color.FromArgb(190, 15, 22, 32));
        sizeLabel.Padding = new Thickness(8, 3, 8, 3);
        sizeLabel.IsHitTestVisible = false;
        sizeLabel.Visibility = Visibility.Collapsed;
        root.Children.Add(sizeLabel);
        var hint =
            UI.Text(textOnly ? "Encadre le texte à lire · Échap pour annuler" : "Glisse pour encadrer · Échap pour annuler",
                    12,
                    new SolidColorBrush(Color.FromArgb(210, 235, 245, 255)));
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.VerticalAlignment = VerticalAlignment.Top;
        hint.Margin = new Thickness(0, 26, 0, 0);
        hint.IsHitTestVisible = false;
        hint.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = .7 };
        root.Children.Add(hint);
        root.MouseLeftButtonDown += (_, e) =>
        {
            if (annotating)
                return;
            origin = e.GetPosition(root);
            drawing = true;
            selection = new Rect(origin, new Size(0, 0));
            DrawShade();
        };
        root.MouseMove += (_, e) =>
        {
            if (!drawing)
                return;
            var point = e.GetPosition(root);
            selection = new Rect(new Point(Math.Min(origin.X, point.X), Math.Min(origin.Y, point.Y)),
                                 new Point(Math.Max(origin.X, point.X), Math.Max(origin.Y, point.Y)));
            DrawShade();
        };
        root.MouseLeftButtonUp += (_, _) =>
        {
            if (!drawing)
                return;
            drawing = false;
            if (selection.Width < 10 || selection.Height < 10)
            {
                DrawShade();
                return;
            }
            // Le mode texte saute entièrement le stylo : on cadre, on lit, c'est fini.
            if (textOnly)
            {
                var image = Render();
                Close();
                _ = OcrService.ReadAsync(owner, image);
                return;
            }
            StartAnnotating();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Copy();
                e.Handled = true;
            }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Copy();
                e.Handled = true;
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Save();
                e.Handled = true;
            }
        };
        Closed += (_, _) => { if (ReferenceEquals(current, this)) current = null; };
        Loaded += (_, _) =>
        {
            DrawShade();
            Activate();
        };
    }
    void DrawShade()
    {
        if (selection.Width < 1 || selection.Height < 1)
        {
            shade.Data = Geometry.Empty;
            frame.Visibility = Visibility.Collapsed;
            sizeLabel.Visibility = Visibility.Collapsed;
            return;
        }
        var all = new RectangleGeometry(new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        var hole = new RectangleGeometry(selection, 3, 3);
        shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, all, hole);
        frame.Margin = new Thickness(selection.X, selection.Y, 0, 0);
        frame.Width = selection.Width;
        frame.Height = selection.Height;
        frame.HorizontalAlignment = HorizontalAlignment.Left;
        frame.VerticalAlignment = VerticalAlignment.Top;
        frame.Visibility = Visibility.Visible;
        sizeLabel.Text = (int)(selection.Width * scaleX) + " × " + (int)(selection.Height * scaleY);
        sizeLabel.Visibility = Visibility.Visible;
        sizeLabel.Measure(new Size(400, 40));
        sizeLabel.Margin = new Thickness(Math.Max(4, selection.X), Math.Max(4, selection.Y - sizeLabel.DesiredSize.Height - 6), 0, 0);
        sizeLabel.HorizontalAlignment = HorizontalAlignment.Left;
        sizeLabel.VerticalAlignment = VerticalAlignment.Top;
    }
    void StartAnnotating()
    {
        annotating = true;
        ink = new InkCanvas
        {
            Width = selection.Width,
            Height = selection.Height,
            Background = null,
            EditingMode = InkCanvasEditingMode.Ink,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(selection.X, selection.Y, 0, 0),
            Cursor = Cursors.Pen
        };
        ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = Color.FromRgb(255, 86, 86),
            Width = 3,
            Height = 3,
            FitToCurve = true,
            StylusTip = StylusTip.Ellipse
        };
        root.Children.Add(ink);
        tools = BuildToolbar();
        root.Children.Add(tools);
        PositionToolbar();
        systemBar();
    }
    void systemBar()
    {
        // InkCanvas avale la souris dans la sélection : la barre d'annotation est la seule porte de sortie.
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) Undo(); };
    }
}
