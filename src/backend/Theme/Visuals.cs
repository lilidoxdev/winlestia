// C# 12, fichier Visuals.cs, cible Windows 11 / .NET 8 WPF, surfaces de verre et mouvement
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Input;

namespace PanneauxGlass;

internal static partial class UI
{
    static readonly Dictionary<string, SolidColorBrush> brushes = new(StringComparer.OrdinalIgnoreCase);
    static ResourceDictionary? style;
    static bool installed;
    // Mutable volontairement : un changement de thème recolore chaque TextBlock déjà à l'écran.
    internal static readonly SolidColorBrush White = new(System.Windows.Media.Color.FromRgb(0xF5, 0xF7, 0xFC)),
        Dim = new(System.Windows.Media.Color.FromRgb(0xB9, 0xC9, 0xDB)),
        Accent = new(System.Windows.Media.Color.FromRgb(0x9F, 0xE4, 0xF7));
    internal static readonly FontFamily Display = new("Segoe UI Variable Display"),
        Serif = new("Georgia"),
        Mono = new("Consolas"),
        Poster = new("Arial Black"),
        Pixel = new("MS Sans Serif");
    internal static FontFamily Font
        => Theme.Current.Unified ? M3.Body : Theme.Current.Kind switch
        {
            SurfaceKind.Ink => Mono,
            SurfaceKind.Retro => Pixel,
            _ => Display
        };
    internal static FontFamily Heading => Theme.Current.Unified ? M3.Body : Theme.Current.Kind switch
    {
        SurfaceKind.Poster => Poster,
        SurfaceKind.Retro => Pixel,
        _ => Theme.Current.Heading switch { "serif" => Serif, "mono" => Mono, "pixel" => Pixel, _ => Display }
    };
    internal static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    internal static SolidColorBrush Brush(string hex)
    {
        if (brushes.TryGetValue(hex, out var value))
            return value;
        value = new SolidColorBrush(Resolve(hex));
        brushes[hex] = value;
        return value;
    }
    internal static SolidColorBrush Alpha(Color color, byte alpha)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
    // Les thèmes clairs transforment les plaques blanches translucides du chrome en lavis d'encre, une fois, partout.
    static Color Resolve(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        // Sur papier, chaque plaque et étiquette pâle devient un lavis d'encre de même teinte.
        double luminance = .2126 * color.R + .7152 * color.G + .0722 * color.B;
        if (Theme.Current.Light && color.A < 255 && luminance > 170)
            return System.Windows.Media.Color.FromArgb(color.A, (byte)(color.R * .18), (byte)(color.G * .18), (byte)(color.B * .18));
        return color;
    }
    internal static void Repaint()
    {
        foreach (var pair in brushes)
            pair.Value.Color = Resolve(pair.Key);
        RepaintTokens();
    }
    internal static void ApplyTheme()
    {
        var theme = Theme.Current;
        White.Color = theme.Text;
        Dim.Color = theme.Dim;
        Accent.Color = theme.Accent;
        Repaint();
        if (installed)
            Restyle();
        if (System.Windows.Application.Current != null)
            foreach (Window window in System.Windows.Application.Current.Windows)
                ApplyTypography(window);
    }
    // Garde les fenêtres vivantes dans la voix du thème : titres à empattements sur papier, chasse fixe partout en encre.
    internal static void ApplyTypography(DependencyObject root)
    {
        if (root is TextBlock text)
        {
            if (!(text.FontFamily?.Source.Contains("Icons", StringComparison.OrdinalIgnoreCase) ?? false)
                && !(text.FontFamily?.Source.Contains("Material Symbols", StringComparison.OrdinalIgnoreCase) ?? false))
                text.FontFamily = text.FontWeight >= FontWeights.SemiBold && text.FontSize >= 15 ? Heading : Font;
        }
        else if (root is TextBox box)
            box.FontFamily = Font;
        else if (root is Control control && root is not ScrollViewer)
            control.FontFamily = Font;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
            ApplyTypography(VisualTreeHelper.GetChild(root, i));
    }
    internal static string Hex(Color color) => "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
    static string A(byte alpha, string rgb) => "#" + alpha.ToString("X2") + rgb;
    static int Radius(int basis) => Theme.Current.Unified ? M3.Round(basis)
        : Math.Clamp((int)Math.Round(basis * Theme.Current.RadiusScale),
        2, 30);
    internal static void Install()
    {
        if (installed)
            return;
        installed = true;
        Restyle();
    }
    internal static TextBlock Text(string value, double size = 14, Brush? color = null, bool bold = false)
        => new()
        {
            Text = value,
            FontSize = size,
            Foreground = color ?? White,
            FontFamily = bold && size >= 15 ? Heading : Font,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal
        };
    internal static TextBlock Glyph(string code, double size = 18)
        => new()
        {
            Text = code,
            FontFamily = Icons,
            FontSize = size,
            Foreground = White,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
    internal static Button Button(string label, Action action)
    {
        var b = new Button
        {
            Content = label,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        b.Click += (_, _) => action();
        Motion.BindButton(b);
        return b;
    }
    internal static Button IconButton(string glyph, string name, Action action)
    {
        var b = Button("", action);
        b.Tag = "m3icon";
        b.Content = Glyph(glyph);
        b.Width = 38;
        b.Height = 38;
        b.Padding = new Thickness(0);
        b.ToolTip = name;
        System.Windows.Automation.AutomationProperties.SetName(b, name);
        return b;
    }
    internal static MenuItem Menu(string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => action();
        return item;
    }
    internal static Color Color(string hex) => ((SolidColorBrush)Brush(hex)).Color;
    internal static void SetIcon(Image image, ImageSource source)
    {
        image.Source = source;
        image.Effect = null;
        if (!Theme.Current.Light || source is not System.Windows.Media.Imaging.BitmapSource bitmap)
            return;
        try
        {
            var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            var bytes = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(bytes, stride, 0);
            double light = 0;
            int visible = 0;
            for (int y = 0; y < converted.PixelHeight; y += 6)
                for (int x = 0; x < converted.PixelWidth; x += 6)
                {
                    int index = y * stride + x * 4;
                    if (bytes[index + 3] < 170)
                        continue;
                    light += (bytes[index] + bytes[index + 1] + bytes[index + 2]) / 3d;
                    visible++;
                }
            if (visible > 0 && light / visible > 224)
            {
                var shadow = new DropShadowEffect { Color = Theme.Current.Text, BlurRadius = 3, ShadowDepth = 0, Opacity = .8 };
                shadow.Freeze();
                image.Effect = shadow;
            }
        }
        catch (Exception ex)
        {
            Store.Log("Visuals.SetIcon : " + ex.GetType().Name + ": " + ex.Message);
        }
    }
    internal static void Animate(UIElement element, int direction = 1)
    {
        if (element is FrameworkElement framework)
            Motion.Reveal(framework);
    }
}
