// Feuille épinglée au-dessus des fenêtres, extraite de CaptureTools.

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

// Une feuille scotchée au-dessus de toutes les fenêtres : une capture ou un mémo. Chaque détail posé - texte, taille,
// position, transparence, angle - vit dans les Preferences et revient au prochain démarrage.
internal sealed class PinWindow : Window
{
    // Regroupe les déplacements et frappes avant d'écrire sur disque.
    const int SaveDebounceMs = 450;
    static readonly List<PinWindow> pins = new();
    readonly Coordinator owner;
    readonly PinNote note;
    readonly Border card;
    readonly RotateTransform rotation;
    readonly TranslateTransform drop = new();
    readonly TextBox? editor;
    readonly DispatcherTimer save = new() { Interval = TimeSpan.FromMilliseconds(SaveDebounceMs) };
    readonly bool isImage;
    double ratio;
    double baseAngle;
    bool placed;

    internal static void RestoreAll(Coordinator coordinator)
    {
        if (System.Windows.Application.Current == null)
            return;
        int restored = 0;
        foreach (var pinned in Store.State.Pins.ToArray())
        {
            try
            {
                BitmapSource? image = null;
                if (pinned.Kind == "image")
                {
                    if (!File.Exists(pinned.ImagePath))
                    {
                        Store.State.Pins.Remove(pinned);
                        continue;
                    }
                    image = Load(pinned.ImagePath);
                }
                Show(coordinator, pinned, image, false);
                restored++;
            }
            catch (Exception ex) { Store.Log("Papier restauré: " + ex.Message); }
        }
        if (restored > 0)
            Store.Log("Papiers restaurés · " + restored);
        Store.Save();
    }
    internal static void Open(Coordinator coordinator, BitmapSource image)
    {
        if (System.Windows.Application.Current == null)
            return;
        try
        {
            string folder = Path.Combine(Store.DataDir, "pins");
            Directory.CreateDirectory(folder);
            var pinned = new PinNote { Kind = "image", Width = Math.Min(420, image.PixelWidth * (96 / Math.Max(1, image.DpiX))),
                Angle = -2.6 };
            pinned.ImagePath = Path.Combine(folder, pinned.Id + ".png");
            using (var stream = File.Create(pinned.ImagePath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(stream);
            }
            pinned.Height = pinned.Width * image.PixelHeight / Math.Max(1, image.PixelWidth);
            Store.State.Pins.Insert(0, pinned);
            Store.Save();
            Show(coordinator, pinned, image, true);
        }
        catch (Exception ex) { coordinator.Message("Le papier n’a pas pu être épinglé.\n" + ex.Message); }
    }
    internal static void OpenMemo(Coordinator coordinator, string text = "")
    {
        if (System.Windows.Application.Current == null)
            return;
        var pinned = new PinNote { Kind = "memo", Text = text, Width = 300, Angle = -2.4 };
        Store.State.Pins.Insert(0, pinned);
        Store.Save();
        var window = Show(coordinator, pinned, null, true);
        window.editor?.Focus();
    }
    static PinWindow Show(Coordinator coordinator, PinNote pinned, BitmapSource? image, bool activate)
    {
        var window = new PinWindow(coordinator, pinned, image);
        pins.Add(window);
        window.Closed += (_, _) => pins.Remove(window);
        window.Show();
        if (activate)
            Native.Raise(window);
        return window;
    }
    // Une boîte de dialogue prend le dessus sans détacher le papier : il revient au-dessus quand la boîte se ferme.
    internal static void Lower(bool lower)
    {
        foreach (var window in pins)
            try
            {
                window.Topmost = !lower;
            }
            catch (Exception err)
            {
                Store.Log("CaptureTools.Lower : " + err.GetType().Name + ": " + err.Message);
            }
    }
    // Tant que l'espace est ouvert, les feuilles flottent entre la barre d'outils et les panneaux, estompées et
    // transparentes aux entrées : encore lisibles, mais aucun clic de l'interface ne vous est volé.
    internal static void SpaceOpen(bool open, IntPtr toolbar)
    {
        foreach (var window in pins)
            try
            {
                window.ApplySpace(open, toolbar);
            }
            catch (Exception ex) { Store.Log("Papier espace: " + ex.Message); }
    }
    // Activer un panneau le place au-dessus des feuilles ; ceci les ramène sans prendre le focus.
    internal static void EnsureAbove()
    {
        foreach (var window in pins)
            try
            {
                Native.KeepAbove(new WindowInteropHelper(window).Handle);
            }
            catch (Exception err)
            {
                Store.Log("CaptureTools.EnsureAbove : " + err.GetType().Name + ": " + err.Message);
            }
    }
    void ApplySpace(bool open, IntPtr toolbar)
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        Native.ClickThrough(handle, open);
        Topmost = true;
        BeginAnimation(OpacityProperty,
                       new DoubleAnimation(Opacity, open ? note.Opacity * .78 : note.Opacity, Motion.Span(180)) {
                           EasingFunction = Motion.Ease });
    }
    internal static void CloseAll()
    {
        foreach (var window in pins.ToArray())
            window.Close();
    }
    static BitmapSource Load(string path)
    {
        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource = new Uri(path);
        source.EndInit();
        source.Freeze();
        return source;
    }
    PinWindow(Coordinator coordinator, PinNote pinned, BitmapSource? image)
    {
        owner = coordinator;
        note = pinned;
        baseAngle = pinned.Angle;
        isImage = image != null;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        Opacity = Math.Clamp(pinned.Opacity, .35, 1);
        double width = Math.Clamp(pinned.Width, 160, 1200);
        if (image != null)
        {
            ratio = image.PixelWidth / (double)Math.Max(1, image.PixelHeight);
            double maxWidth = SystemParameters.WorkArea.Width * .46, maxHeight = SystemParameters.WorkArea.Height * .62,
                height = width / ratio;
            if (height > maxHeight)
            {
                height = maxHeight;
                width = height * ratio;
            }
            Width = width + 16;
            Height = height + 16;
        }
        else
        {
            SizeToContent = SizeToContent.Height;
            Width = width + 16;
            MinHeight = 120;
        }
        var root = new Grid();
        card = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(253, 251, 244), 0),
                new GradientStop(Color.FromRgb(246, 240, 227), 1)
            },
                                                 new Point(.12, .05),
                                                 new Point(.9, 1)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 150, 116, 86)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 10, Direction = 270, Opacity = .45 },
            RenderTransformOrigin = new Point(.5, .5)
        };
        rotation = new RotateTransform(baseAngle - 6.5);
        card.RenderTransform = new TransformGroup { Children = { rotation, drop } };
        if (image != null)
        {
            var picture = new Image { Source = image, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
            card.Child = new Border { Clip = new RectangleGeometry(new Rect(0, 0, width, width / ratio), 5, 5), Child = picture };
        }
        else
        {
            editor = new TextBox
            {
                Text = pinned.Text,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Segoe Print"),
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(48, 45, 42)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 2, 4, 2),
                MinHeight = 96,
                MaxHeight = 480,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            System.Windows.Automation.AutomationProperties.SetName(editor, "Texte du papier");
            editor.TextChanged += (_, _) =>
            {
                note.Text = editor.Text;
                Schedule();
            };
            card.Child = editor;
        }
        root.Children.Add(card);
        var tape =
            new Border
            {
                Width = Math.Min(132, width * .44),
                Height = 20,
                Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(225, 255, 249, 224), 0),
                    new GradientStop(Color.FromArgb(235, 243, 229, 187), 1)
                },
                                                     new Point(0, 0),
                                                     new Point(1, 1)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 163, 140, 96)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -9, 0, 0),
                Opacity = .95,
                Cursor = Cursors.SizeAll,
                RenderTransform = new RotateTransform(-3),
                ToolTip = "Glisser par le ruban pour déplacer"
            };
        tape.MouseLeftButtonDown += (_, _) => { try { DragMove(); } catch (InvalidOperationException) {
            /* DragMove lève si le bouton est déjà relâché : glissement sans objet. */ } };
        root.Children.Add(tape);
        var close = UI.IconButton("\uE711", "Détacher et supprimer ce papier", Detach);
        close.Width = 24;
        close.Height = 24;
        close.FontSize = 9;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Margin = new Thickness(0, -10, -10, 0);
        close.Opacity = 0;
        close.Background = new SolidColorBrush(Color.FromArgb(220, 28, 36, 48));
        close.BorderBrush = Brushes.Transparent;
        root.Children.Add(close);
        card.MouseEnter += (_, _) =>
        {
            close.Opacity = 1;
            Animate(0);
        };
        card.MouseLeave += (_, _) =>
        {
            close.Opacity = 0;
            Animate(baseAngle);
        };
        if (image != null)
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is Button)
                    return;
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                    // DragMove lève si le bouton est déjà relâché : glissement sans objet.
                }
            };
        card.PreviewMouseWheel += (_, e) => Adjust(e);
        LocationChanged += (_, _) => { if (placed) Schedule(); };
        save.Tick += (_, _) => Persist();
        Closed += (_, _) => Persist();
        Content = root;
        // La feuille atterrit : quelques pixels de chute, puis elle se pose dans son angle, ruban adhésif compris.
        Loaded += (_, _) =>
        {
            Place();
            rotation.BeginAnimation(RotateTransform.AngleProperty,
                                    new DoubleAnimation(baseAngle - 6.5, baseAngle, Motion.Span(420))
                                    {
                                        EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = .45 }
                                    });
            drop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-18, 0, Motion.Span(420)) { EasingFunction = Motion.Ease
                });
            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Span(180)));
            Schedule();
        };
    }
    void Place()
    {
        var area = SystemParameters.WorkArea;
        double width = Width, height = Height;
        double left = note.Left is double savedLeft && double.IsFinite(savedLeft) ? savedLeft : area.Left + area.Width * .5 - width * .5;
        double top = note.Top is double savedTop && double.IsFinite(savedTop) ? savedTop : area.Top + area.Height * .28;
        Left = Math.Clamp(left, area.Left - 20, Math.Max(area.Left, area.Right - width + 20));
        Top = Math.Clamp(top, area.Top - 20, Math.Max(area.Top, area.Bottom - height + 20));
        placed = true;
    }
    // La molette dimensionne la feuille, Ctrl + molette la rend plus ou moins transparente. Les deux sont conservés.
    void Adjust(MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            note.Opacity = Math.Clamp(note.Opacity + (e.Delta > 0 ? .08 : -.08), .35, 1);
            Opacity = note.Opacity;
            Schedule();
            e.Handled = true;
            return;
        }
        double next = Math.Clamp(Width * (e.Delta > 0 ? 1.08 : 1 / 1.08), 176, 1416);
        Width = next;
        if (isImage && ratio > 0)
            Height = next / ratio;
        note.Width = next - 16;
        if (isImage)
            note.Height = Math.Max(40, Height - 16);
        Schedule();
        e.Handled = true;
    }
    void Schedule()
    {
        if (!placed)
            return;
        save.Stop();
        save.Start();
    }
    void Persist()
    {
        save.Stop();
        if (editor != null)
            note.Text = editor.Text;
        note.Left = Left;
        note.Top = Top;
        note.Width = Math.Clamp(Width - 16, 160, 1400);
        if (isImage)
            note.Height = Math.Max(40, Height - 16);
        Store.Save();
    }
    void Detach()
    {
        save.Stop();
        Store.State.Pins.Remove(note);
        if (!string.IsNullOrWhiteSpace(note.ImagePath))
            try
            {
                File.Delete(note.ImagePath);
            }
            catch (Exception ex) { Store.Log("Papier: " + ex.Message); }
        Store.Save();
        Close();
    }
    void Animate(double angle)
    {
        rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, Motion.Span(220)) { EasingFunction = Motion.Ease
            });
    }
}
