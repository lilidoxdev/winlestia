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

internal sealed partial class CaptureOverlay : Window
{
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
            var window = new TextResultWindow(owner, "Texte lu par Windows", text) { Owner = owner.Anchor,
                ShowInTaskbar = owner.PreviewMode
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
