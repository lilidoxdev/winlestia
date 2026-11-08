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
    static int Radius(int basis) => Theme.Current.Unified ? M3.Round(basis) : Math.Clamp((int)Math.Round(basis * Theme.Current.RadiusScale),
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
        var b = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center };
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
internal partial class GlassWindow : Window
{
    // Rendu hors écran pour les contrôles : chaque fenêtre s'ouvre très loin hors du bureau virtuel,
    // pour qu'un thème puisse être inspecté en PNG sans qu'un seul panneau apparaisse sur le bureau.
    internal static Vector CaptureOffset;
    static bool Offscreen => CaptureOffset.LengthSquared > 0;
    internal readonly Grid Surface = new();
    internal readonly Grid ContentHost = new();
    readonly Border tintLayer = new();
    readonly Border rim = new(), accentRule = new(), glowFrame = new();
    readonly Grid reflections = new(), ambient = new();
    readonly Rectangle ambientA = new(), ambientB = new(), signature = new(), reflectionSheet = new();
    readonly LinearGradientBrush rimBrush = new();
    (double tint, bool light, bool opaque)? material;
    (double width, double height, double radius) geometry;
    readonly TranslateTransform motion = new();
    IntPtr nativeHandle;
    int motionVersion, themeVersion = -1;
    readonly TranslateTransform drawerSlide = new();
    readonly System.Windows.Shapes.Path drawerOutline = new() { IsHitTestVisible = false, StrokeThickness = 1 };
    internal bool TopAttached;
    internal void AttachToTop()
    {
        TopAttached = true;
        AllowsTransparency = true;
        Surface.RenderTransform = drawerSlide;
        Surface.Children.Add(drawerOutline);
    }
    internal int Radius = 32;
    internal int VisualRadius => Math.Max(4, (int)Math.Round(Radius * Theme.Current.RadiusScale));
    internal double TargetLeft, TargetTop;
    internal bool KeepOpen;
    internal bool MaterialVisible = true;
    internal Func<KeyEventArgs, bool>? Shortcut;
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Shortcut != null && Shortcut(e))
        {
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }
    internal GlassWindow(string title)
    {
        Title = title;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = Native.Layered && !Theme.Current.Opaque;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        FontFamily = UI.Font;
        Foreground = UI.White;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Surface.ClipToBounds = true;
        Content = Surface;
        tintLayer.CornerRadius = new CornerRadius(Radius);
        Surface.Children.Add(tintLayer);
        // Néon lights its own frame: a blurred inner rim, clipped by the window like the tint.
        glowFrame.CornerRadius = new CornerRadius(Radius);
        glowFrame.BorderThickness = new Thickness(2);
        glowFrame.IsHitTestVisible = false;
        glowFrame.Effect = new BlurEffect { Radius = 11 };
        glowFrame.Visibility = Visibility.Collapsed;
        Surface.Children.Add(glowFrame);
        ambient.IsHitTestVisible = false;
        ambient.Opacity = .8;
        ambientA.IsHitTestVisible = ambientB.IsHitTestVisible = signature.IsHitTestVisible = false;
        ambient.Children.Add(ambientA);
        ambient.Children.Add(ambientB);
        ambient.Children.Add(signature);
        Surface.Children.Add(ambient);
        reflections.IsHitTestVisible = false;
        reflectionSheet.IsHitTestVisible = false;
        reflections.Children.Add(reflectionSheet);
        Surface.Children.Add(reflections);
        rim.CornerRadius = new CornerRadius(Radius);
        rim.BorderBrush = rimBrush;
        rim.BorderThickness = new Thickness(1);
        rim.IsHitTestVisible = false;
        Surface.Children.Add(rim);
        accentRule.HorizontalAlignment = HorizontalAlignment.Stretch;
        accentRule.VerticalAlignment = VerticalAlignment.Top;
        accentRule.IsHitTestVisible = false;
        accentRule.CornerRadius = new CornerRadius(2);
        BuildThemeLayers();
        Surface.Children.Add(ContentHost);
        Surface.Children.Add(accentRule);
        ContentHost.RenderTransform = motion;
        ContentHost.CacheMode = new BitmapCache { EnableClearType = true, SnapsToDevicePixels = true };
        SourceInitialized += (_, _) =>
        {
            nativeHandle = new WindowInteropHelper(this).Handle;
            ApplyGlass();
            if (TopAttached)
                Native.ClearRegion(this);
        };
        SizeChanged += (_, _) => UpdateCorners();
        LocationChanged += (_, _) => { if (nativeHandle != IntPtr.Zero) Native.SyncBackdrop(this, VisualRadius); };
        IsVisibleChanged += (_, _) => { if (nativeHandle != IntPtr.Zero) Native.SyncBackdrop(this, VisualRadius); };
        Closed += (_, _) => { if (AllowsTransparency && nativeHandle != IntPtr.Zero) Native.BackdropDetach(nativeHandle); };
        DpiChanged += (_, _) =>
        {
            geometry = default;
            UpdateCorners();
        };
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TopmostProperty && nativeHandle != IntPtr.Zero)
            Native.SyncBackdrop(this, VisualRadius);
    }
    void UpdateCorners()
    {
        var next = (ActualWidth, ActualHeight, (double)VisualRadius);
        if (geometry == next)
            return;
        geometry = next;
        if (TopAttached)
        {
            var shape = DrawerShape.Create(Math.Max(1, ActualWidth), Math.Max(1, ActualHeight));
            Surface.Clip = shape;
            drawerOutline.Data = shape;
            drawerOutline.Stroke = UI.Alpha(Theme.Current.Dim, 95);
            rim.Visibility = Visibility.Collapsed;
            return;
        }
        double radius = Math.Min(VisualRadius, Math.Min(ActualWidth, ActualHeight) / 2);
        if (radius < 0)
            return;
        tintLayer.CornerRadius = rim.CornerRadius = glowFrame.CornerRadius = new CornerRadius(radius);
        Surface.Clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)), radius, radius);
        Native.Round(this, (int)radius);
    }
    // Tout ce qui est coloré sur le verre est reconstruit depuis le thème actif, donc un changement atteint aussi les anciennes fenêtres.
    void BuildThemeLayers()
    {
        var theme = Theme.Current;
        themeVersion = Theme.Version;
        ambientA.Fill = new RadialGradientBrush
        {
            Center = new Point(.03, .01),
            GradientOrigin = new Point(.03, .01),
            RadiusX = .92,
            RadiusY = 1.1,
            GradientStops = new() { new(theme.Halo(36, 0), 0), new(Colors.Transparent, 1) }
        };
        ambientB.Fill = new RadialGradientBrush
        {
            Center = new Point(.98, .98),
            GradientOrigin = new Point(.98, .98),
            RadiusX = .95,
            RadiusY = 1,
            GradientStops = new() { new(theme.Halo(24, 1), 0), new(Colors.Transparent, 1) }
        };
        signature.Fill = SignatureBrush(theme);
        signature.Visibility = signature.Fill == null ? Visibility.Collapsed : Visibility.Visible;
        reflectionSheet.Fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(.82, 1),
            GradientStops = new(){
            new(colorWith(Color.FromArgb(27,255,255,255),theme.ReflectionGain),0),new(colorWith(Color.FromArgb(7,255,255,255),theme
                .ReflectionGain),
                                                                                      .35),new(Colors.Transparent,.53),
            new(Color.FromArgb((byte)Math.Clamp(12*theme.ReflectionGain,0,255),theme.Reflection.R,theme.Reflection.G,theme.Reflection.B),
                .70),new(Colors.Transparent,1)}
        };
        rimBrush.StartPoint = new Point(0, 0);
        rimBrush.EndPoint = new Point(1, 1);
        rimBrush.GradientStops.Clear();
        rimBrush.GradientStops.Add(theme.Light || theme.Unified
            ? new(Color.FromArgb(170, theme.RimB.R, theme.RimB.G, theme.RimB.B), 0)
            : new(Color.FromArgb(174, 255, 255, 255), 0));
        rimBrush.GradientStops.Add(new(Color.FromArgb((byte)(50 * theme.ReflectionGain), theme.RimA.R, theme.RimA.G, theme.RimA.B), .3));
        rimBrush.GradientStops.Add(new(Color.FromArgb(23, 255, 255, 255), .50));
        rimBrush.GradientStops.Add(new(Color.FromArgb(105, theme.RimB.R, theme.RimB.G, theme.RimB.B), 1));
        accentRule.Height = theme.Kind switch
        {
            SurfaceKind.Poster => 4,
            SurfaceKind.Retro => 3,
            SurfaceKind.Bento => 5,
            SurfaceKind.Swiss => 2,
            SurfaceKind.Neon => 2,
            _ => 2
        };
        accentRule.Visibility = theme.Kind is SurfaceKind.Paper or SurfaceKind.Ink or SurfaceKind.Poster or SurfaceKind.Retro or SurfaceKind
            .Bento or SurfaceKind.Swiss or SurfaceKind.Neon
            ? Visibility.Visible
            : Visibility.Collapsed;
        accentRule.Background = theme.Kind switch
        {
            SurfaceKind.Poster => new LinearGradientBrush(new GradientStopCollection { new(theme.HaloA, 0), new(theme.HaloB, .55),
                new(theme.HaloA, 1) },
                                                          new Point(0, 0),
                                                          new Point(1, 0)),
            SurfaceKind.Ink => new LinearGradientBrush(Color.FromArgb(190, theme.Accent.R, theme.Accent.G, theme.Accent.B),
                                                       Colors.Transparent,
                                                       new Point(0, 0),
                                                       new Point(1, 0)),
            SurfaceKind.Paper => new SolidColorBrush(Color.FromArgb(90, theme.Accent.R, theme.Accent.G, theme.Accent.B)),
            SurfaceKind.Retro => new LinearGradientBrush(Color.FromArgb(255, theme.Accent.R, theme.Accent.G, theme.Accent.B),
                                                         Color.FromArgb(160, theme.Accent.R, theme.Accent.G, theme.Accent.B),
                                                         new Point(0, 0),
                                                         new Point(1, 0)),
            SurfaceKind.Bento => new LinearGradientBrush(new GradientStopCollection { new(theme.HaloA, 0), new(theme.HaloB, .62),
                new(theme.HaloA, 1) },
                                                         new Point(0, 0),
                                                         new Point(1, 0)),
            SurfaceKind.Swiss => new SolidColorBrush(theme.Accent),
            SurfaceKind.Neon => new LinearGradientBrush(new GradientStopCollection { new(theme.HaloA, 0), new(theme.HaloB, .5),
                new(theme.HaloA, 1) },
                                                        new Point(0, 0),
                                                        new Point(1, 0)),
            _ => Brushes.Transparent
        };
        glowFrame.BorderBrush = new SolidColorBrush(Color.FromArgb(200, theme.Accent.R, theme.Accent.G, theme.Accent.B));
        glowFrame.Visibility = theme.Kind == SurfaceKind.Neon ? Visibility.Visible : Visibility.Collapsed;
        geometry = default;
        material = null;
        UpdateCorners();
    }
    internal void RefreshTheme()
    {
        themeVersion = -1;
        ApplyGlass();
    }
    // Un changement de thème respire au lieu de claquer : la surface baisse puis revient dans les nouvelles couleurs.
    internal void FlashTheme()
    {
        if (!Motion.On || !IsVisible)
            return;
        ContentHost.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.72, 1, Motion.Span(230)) { EasingFunction = Motion.Ease
            });
        var glow = new DoubleAnimation(.55, 1, Motion.Span(320)) { EasingFunction = Motion.Ease };
        ambient.BeginAnimation(UIElement.OpacityProperty, glow);
    }
    internal void ApplyGlass()
    {
        if (themeVersion != Theme.Version)
            BuildThemeLayers();
        tintLayer.Visibility = ambient.Visibility = reflections.Visibility = rim.Visibility = MaterialVisible ? Visibility.Visible
            : Visibility.Collapsed;
        var theme = Theme.Current;
        if (TopAttached)
            rim.Visibility = Visibility.Collapsed;
        var surface = Store.State.LightGlass && !theme.Opaque ? Color.FromRgb(0x2B, 0x3D, 0x52) : theme.Surface;
        byte alpha = theme.Opaque ? (byte)255 : (byte)Math.Clamp((.58 + Store.State.Tint * .4) * 255, 0, 235);
        tintLayer.Background = new SolidColorBrush(Color.FromArgb(alpha, surface.R, surface.G, surface.B));
        reflections.Opacity = Math.Clamp(Store.State.Reflections * theme.ReflectionGain, 0, 1);
        rim.Opacity = .55 + Store.State.Reflections * .45;
        var next = (Store.State.Tint, Theme.Current.Light || Store.State.LightGlass, Theme.Current.Opaque);
        if (material != next)
        {
            Native.Glass(this, next.Item1, next.Item2, VisualRadius);
            material = next;
        }
        UpdateCorners();
    }
    void CancelMotion()
    {
        motionVersion++;
        double currentHeight = Height;
        BeginAnimation(HeightProperty, null);
        if (double.IsFinite(currentHeight))
            Height = currentHeight;
        drawerSlide.BeginAnimation(TranslateTransform.YProperty, null);
        drawerSlide.Y = 0;
        Surface.BeginAnimation(UIElement.OpacityProperty, null);
        Surface.Opacity = 1;
        double x = Left, y = Top;
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        if (double.IsFinite(x))
            Left = x;
        if (double.IsFinite(y))
            Top = y;
        motion.BeginAnimation(TranslateTransform.XProperty, null);
        ContentHost.BeginAnimation(UIElement.OpacityProperty, null);
        motion.X = 0;
        ContentHost.Opacity = 1;
        ContentHost.Effect = null;
    }
}
