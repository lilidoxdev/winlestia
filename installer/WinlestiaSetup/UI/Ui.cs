using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinlestiaSetup;

static class Ui
{
    public static TextBlock Tx(string s, double size = 14, string brush = "TextBrush", FontWeight? w = null, bool wrap = true)
    {
        var t = new TextBlock { Text = s, FontSize = size, FontWeight = w ?? FontWeights.Normal, TextWrapping = wrap ? TextWrapping.Wrap
            : TextWrapping.NoWrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static Border Card(UIElement child, double radius = 16, Thickness? pad = null, string bg = "CardBrush")
    {
        var b = new Border { Child = child, CornerRadius = new CornerRadius(radius), Padding = pad ?? new Thickness(18, 14, 18, 14) };
        b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }

    public static TextBlock Icon(string glyph, double size = 18, string brush = "AccentBrush")
    {
        var t = new TextBlock { Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    public static StackPanel Title(string title, string? sub = null)
    {
        var p = new StackPanel { Margin = new Thickness(0, 0, 0, 22) };
        p.Children.Add(Tx(title, 28, "TextBrush", FontWeights.SemiBold));
        if (sub != null) { var s = Tx(sub, 14.5, "TextDimBrush"); s.Margin = new Thickness(0, 8, 0, 0); s.MaxWidth = 600;
            s.HorizontalAlignment = HorizontalAlignment.Left; p.Children.Add(s); }
        return p;
    }

    // Ligne à puce : glyphe d'accent + texte.
    public static FrameworkElement Bullet(string glyph, string text, double size = 14)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var i = Icon(glyph, 15); i.VerticalAlignment = VerticalAlignment.Top; i.Margin = new Thickness(0, 2, 0, 0);
        var t = Tx(text, size, "TextBrush"); Grid.SetColumn(t, 1);
        g.Children.Add(i); g.Children.Add(t);
        return g;
    }

    public static Border Pill(string text, string fg = "AccentBrush", string bg = "AccentSoftBrush")
    {
        var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 4),
            Child = Tx(text, 12, fg, FontWeights.SemiBold, false), HorizontalAlignment = HorizontalAlignment.Left };
        b.SetResourceReference(Border.BackgroundProperty, bg);
        return b;
    }

    public static ToggleButton Switch(bool on, bool enabled = true) => new() {
        Style = (Style)Application.Current.FindResource("SwitchStyle"), IsChecked = on, IsEnabled = enabled,
        VerticalAlignment = VerticalAlignment.Center };

    public static ImageBrush? Preview(string name)
    {
        try
        {
            var bi = new BitmapImage(new Uri("pack://application:,,,/Assets/previews/" + name + ".png"));
            return new ImageBrush(bi) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top };
        }
        catch { return null; }
    }
}
