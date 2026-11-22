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

    public const string DiscordUrl = "https://discord.gg/5jXv7NRj3Q";

    public static void OpenDiscord()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DiscordUrl) { UseShellExecute = true }); }
        catch { }
    }

    // Logo Discord (Simple Icons, viewBox 24x24), rempli avec le pinceau d'accent du thème.
    public static FrameworkElement DiscordLogo(double size = 26)
    {
        var p = new System.Windows.Shapes.Path { Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
            Data = Geometry.Parse("M20.317 4.3698a19.7913 19.7913 0 00-4.8851-1.5152.0741.0741 0 00-.0785.0371c-.211.3753-.4447.8648-.6083 1.2495-1.8447-.2762-3.68-.2762-5.4868 0-.1636-.3933-.4058-.8742-.6177-1.2495a.077.077 0 00-.0785-.037 19.7363 19.7363 0 00-4.8852 1.515.0699.0699 0 00-.0321.0277C.5334 9.0458-.319 13.5799.0992 18.0578a.0824.0824 0 00.0312.0561c2.0528 1.5076 4.0413 2.4228 5.9929 3.0294a.0777.0777 0 00.0842-.0276c.4616-.6304.8731-1.2952 1.226-1.9942a.076.076 0 00-.0416-.1057c-.6528-.2476-1.2743-.5495-1.8722-.8923a.077.077 0 01-.0076-.1277c.1258-.0943.2517-.1923.3718-.2914a.0743.0743 0 01.0776-.0105c3.9278 1.7933 8.18 1.7933 12.0614 0a.0739.0739 0 01.0785.0095c.1202.099.246.1981.3728.2924a.077.077 0 01-.0066.1276 12.2986 12.2986 0 01-1.873.8914.0766.0766 0 00-.0407.1067c.3604.698.7719 1.3628 1.225 1.9932a.076.076 0 00.0842.0286c1.961-.6067 3.9495-1.5219 6.0023-3.0294a.077.077 0 00.0313-.0552c.5004-5.177-.8382-9.6739-3.5485-13.6604a.061.061 0 00-.0312-.0286zM8.02 15.3312c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9555-2.4189 2.157-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.9555 2.4189-2.1569 2.4189zm7.9748 0c-1.1825 0-2.1569-1.0857-2.1569-2.419 0-1.3332.9554-2.4189 2.1569-2.4189 1.2108 0 2.1757 1.0952 2.1568 2.419 0 1.3332-.946 2.4189-2.1568 2.4189Z") };
        p.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
        return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { p } },
            VerticalAlignment = VerticalAlignment.Center };
    }

    // Carte d'invitation Discord : logo, texte, bouton.
    public static Border DiscordCard()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var logo = DiscordLogo(); logo.Margin = new Thickness(0, 0, 14, 0);
        var t = Tx(Loc.T("discord.text"), 13.5); t.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(t, 1);
        var btn = new Button { Style = (Style)Application.Current.FindResource("PrimaryButton"), Content = Loc.T("discord.btn"),
            Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        btn.Click += (_, _) => OpenDiscord(); Grid.SetColumn(btn, 2);
        g.Children.Add(logo); g.Children.Add(t); g.Children.Add(btn);
        return Card(g, 16, new Thickness(20, 12, 14, 12));
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
