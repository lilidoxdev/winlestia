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
internal sealed class CaptureOverlay : Window
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
    Border BuildToolbar()
    {
        var bar =
            new Border
            {
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Color.FromArgb(242, 20, 28, 40)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 2, Opacity = .55 }
            };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        Button Tool(string glyph, string label, Action action)
        {
            var button = UI.IconButton(glyph, label, action);
            button.Width = 32;
            button.Height = 32;
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(1, 0, 1, 0);
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            return button;
        }
        void Mode(InkCanvasEditingMode mode, DrawingAttributes attributes, Button button)
        {
            ink!.EditingMode = mode;
            if (attributes != null)
                ink.DefaultDrawingAttributes = attributes;
            foreach (var child in row.Children.OfType<Button>())
                child.Background = Brushes.Transparent;
            button.Background = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
        }
        var pen = Tool("\uE70F", "Stylo", () => { });
        var highlight = Tool("\uE7E6", "Surligneur", () => { });
        var eraser = Tool("\uE75C", "Gomme", () => { });
        pen.Click += (_, _) =>
            Mode(InkCanvasEditingMode.Ink,
                 new DrawingAttributes { Color = Color.FromRgb(255, 86, 86), Width = 3, Height = 3, FitToCurve = true,
                     StylusTip = StylusTip.Ellipse },
                 pen);
        highlight.Click += (_, _) =>
            Mode(InkCanvasEditingMode.Ink,
                 new DrawingAttributes
                 {
                     Color = Color.FromArgb(150, 255, 222, 92),
                     Width = 14,
                     Height = 14,
                     FitToCurve = true,
                     StylusTip = StylusTip.Rectangle,
                     IsHighlighter = true
                 },
                 highlight);
        eraser.Click += (_, _) => Mode(InkCanvasEditingMode.EraseByStroke, null, eraser);
        row.Children.Add(pen);
        row.Children.Add(highlight);
        row.Children.Add(eraser);
        row.Children.Add(new Border
        {
            Width = 1,
            Height = 20,
            Background = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
            Margin = new Thickness(5, 6, 5, 6)
        });
        row.Children.Add(Tool("\uE7A7", "Annuler", Undo));
        row.Children.Add(Tool("\uE8A5", "Copier le texte de la zone", ExtractText));
        row.Children.Add(new Border
        {
            Width = 1,
            Height = 20,
            Background = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
            Margin = new Thickness(5, 6, 5, 6)
        });
        row.Children.Add(Tool("\uE840", "Épingler sur l’écran", Pin));
        row.Children.Add(Tool("\uE74E", "Enregistrer en PNG", Save));
        var copy = Tool("\uE8C8", "Copier", Copy);
        copy.Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
        row.Children.Add(copy);
        row.Children.Add(Tool("\uE711", "Fermer", Close));
        bar.Child = row;
        return bar;
    }
    void PositionToolbar()
    {
        if (tools == null)
            return;
        tools.Measure(new Size(600, 60));
        var size = tools.DesiredSize;
        double left = Math.Clamp(selection.X, 8, Math.Max(8, Width - size.Width - 8));
        double top = selection.Y - size.Height - 10;
        if (top < 8)
            top = Math.Min(Height - size.Height - 8, selection.Bottom + 10);
        tools.HorizontalAlignment = HorizontalAlignment.Left;
        tools.VerticalAlignment = VerticalAlignment.Top;
        tools.Margin = new Thickness(left, Math.Max(8, top), 0, 0);
    }
    void Undo()
    {
        if (ink != null && ink.Strokes.Count > 0)
            ink.Strokes.RemoveAt(ink.Strokes.Count - 1);
    }
    BitmapSource Render()
    {
        int x = (int)Math.Round(selection.X * scaleX),
            y = (int)Math.Round(selection.Y * scaleY),
            w = Math.Max(1, (int)Math.Round(selection.Width * scaleX)),
            h = Math.Max(1, (int)Math.Round(selection.Height * scaleY));
        var cropped =
            new CroppedBitmap(screen,
                              new Int32Rect(Math.Clamp(x, 0, screen.PixelWidth - 1),
                                            Math.Clamp(y, 0, screen.PixelHeight - 1),
                                            Math.Min(w, screen.PixelWidth - x),
                                            Math.Min(h, screen.PixelHeight - y)));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(cropped, new Rect(0, 0, cropped.PixelWidth, cropped.PixelHeight));
            if (ink != null && ink.Strokes.Count > 0)
                dc.DrawRectangle(new VisualBrush(ink) { Stretch = Stretch.Fill }, null, new Rect(0, 0, cropped.PixelWidth,
                    cropped.PixelHeight));
        }
        var bitmap = new RenderTargetBitmap(cropped.PixelWidth, cropped.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
    void Copy()
    {
        var image = Render();
        Close();
        try
        {
            System.Windows.Clipboard.SetImage(image);
            owner.ShowToast("Capture copiée");
        }
        catch (Exception ex) { owner.Message("La capture n’a pas pu être copiée.\n" + ex.Message); }
    }
    void Pin()
    {
        var image = Render();
        Close();
        PinWindow.Open(owner, image);
    }
    void Save()
    {
        var image = Render();
        Close();
        ScreenShot.Save(image);
        owner.ShowToast("Capture enregistrée");
    }
    void ExtractText()
    {
        var image = Render();
        Close();
        _ = OcrService.ReadAsync(owner, image);
    }
}
internal static class OcrService
{
    internal static async Task ReadAsync(Coordinator owner, BitmapSource image)
    {
        try
        {
            string text = await RecognizeAsync(image);
            if (string.IsNullOrWhiteSpace(text))
            {
                owner.ShowToast("Aucun texte lisible dans cette zone");
                return;
            }
            SystemActions.Copy(owner, text, "Texte");
            var window = new TextResultWindow(owner, "Texte lu par Windows", text) { Owner = owner.Anchor, ShowInTaskbar = owner.PreviewMode
                };
            window.ShowDialog();
        }
        catch (Exception ex) { owner.Message("La lecture du texte a échoué.\n" + ex.Message); }
    }
    internal static async Task<string> RecognizeAsync(BitmapSource source)
    {
        byte[] png = ScreenShot.Png(source);
        var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(png);
        await writer.StoreAsync();
        await writer.FlushAsync();
        writer.DetachStream();
        writer.Dispose();
        stream.Seek(0);
        var decoder = await WinBitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync();
        var engine =
            OcrEngine.TryCreateFromUserProfileLanguages()
                ?? throw new InvalidOperationException("Aucun moteur de reconnaissance n’est installé pour cette langue.");
        var result = await engine.RecognizeAsync(bitmap);
        var lines = new List<string>();
        foreach (var line in result.Lines)
            lines.Add(string.Join(" ", line.Words.Select(word => word.Text)));
        return string.Join(Environment.NewLine, lines);
    }
}
internal sealed class TextResultWindow : GlassWindow
{
    internal TextResultWindow(Coordinator owner, string title, string text) : base("Panneaux Glass — " + title)
    {
        Width = 620;
        Height = Math.Min(520, SystemParameters.WorkArea.Height - 90);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Radius = 32;
        ShowActivated = true;
        ContentHost.Margin = new Thickness(28, 24, 28, 22);
        ContentHost.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        ContentHost.RowDefinitions.Add(new()
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        ContentHost.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        var header = new DockPanel();
        var close = UI.IconButton("\uE711", "Fermer", Close);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var titles = new StackPanel();
        titles.Children.Add(UI.Text(title, 23, null, true));
        var sub = UI.Text("Copié dans le presse-papiers · modifie ou recopie si besoin", 11, UI.Dim);
        sub.Margin = new Thickness(0, 6, 0, 0);
        titles.Children.Add(sub);
        header.Children.Add(titles);
        ContentHost.Children.Add(header);
        var editor =
            new TextBox
            {
                Text = text,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Padding = new Thickness(15, 12, 15, 12),
                VerticalContentAlignment = VerticalAlignment.Top,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 16, 0, 14)
            };
        System.Windows.Automation.AutomationProperties.SetName(editor, "Texte reconnu");
        Grid.SetRow(editor, 1);
        ContentHost.Children.Add(editor);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var copy = UI.Button("Copier à nouveau", () => SystemActions.Copy(owner, editor.Text, "Texte"));
        actions.Children.Add(copy);
        var done = UI.Button("Terminer", Close);
        done.Margin = new Thickness(10, 0, 0, 0);
        done.MinWidth = 104;
        actions.Children.Add(done);
        Grid.SetRow(actions, 2);
        ContentHost.Children.Add(actions);
        Loaded += (_, _) => ApplyGlass();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }
}
