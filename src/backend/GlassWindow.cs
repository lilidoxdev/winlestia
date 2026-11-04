// GlassWindow: the translucent top-level window every Panneaux surface derives from.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace PanneauxGlass;

internal class GlassWindow : Window
{
    // Off-screen rendering for the checks: every window opens far outside the virtual desktop,
    // so a theme can be inspected as a PNG without a single panel appearing on the desk.
    internal static Vector CaptureOffset;
    static bool Offscreen => CaptureOffset.LengthSquared > 0;
    internal readonly Grid Surface = new();
    internal readonly Grid ContentHost = new();
    readonly Border tintLayer = new();
    readonly Border rim = new(),
        accentRule = new(),
        glowFrame = new();
    readonly Grid reflections = new(),
        ambient = new();
    readonly Rectangle ambientA = new(),
        ambientB = new(),
        signature = new(),
        reflectionSheet = new();
    readonly LinearGradientBrush rimBrush = new();
    (double tint, bool light, bool opaque)? material;
    (double width, double height, double radius) geometry;
    readonly TranslateTransform motion = new();
    IntPtr nativeHandle;
    int motionVersion,
        themeVersion = -1;
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
    internal double TargetLeft,
        TargetTop;
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
        LocationChanged += (_, _) =>
        {
            if (nativeHandle != IntPtr.Zero)
                Native.SyncBackdrop(this, VisualRadius);
        };
        IsVisibleChanged += (_, _) =>
        {
            if (nativeHandle != IntPtr.Zero)
                Native.SyncBackdrop(this, VisualRadius);
        };
        Closed += (_, _) =>
        {
            if (AllowsTransparency && nativeHandle != IntPtr.Zero)
                Native.BackdropDetach(nativeHandle);
        };
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
        Surface.Clip = new RectangleGeometry(
            new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)),
            radius,
            radius
        );
        Native.Round(this, (int)radius);
    }

    // Everything coloured on the glass is rebuilt from the active theme, so a swap reaches old windows too.
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
            GradientStops = new() { new(theme.Halo(36, 0), 0), new(Colors.Transparent, 1) },
        };
        ambientB.Fill = new RadialGradientBrush
        {
            Center = new Point(.98, .98),
            GradientOrigin = new Point(.98, .98),
            RadiusX = .95,
            RadiusY = 1,
            GradientStops = new() { new(theme.Halo(24, 1), 0), new(Colors.Transparent, 1) },
        };
        signature.Fill = SignatureBrush(theme);
        signature.Visibility = signature.Fill == null ? Visibility.Collapsed : Visibility.Visible;
        reflectionSheet.Fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(.82, 1),
            GradientStops = new()
            {
                new(colorWith(Color.FromArgb(27, 255, 255, 255), theme.ReflectionGain), 0),
                new(colorWith(Color.FromArgb(7, 255, 255, 255), theme.ReflectionGain), .35),
                new(Colors.Transparent, .53),
                new(
                    Color.FromArgb(
                        (byte)Math.Clamp(12 * theme.ReflectionGain, 0, 255),
                        theme.Reflection.R,
                        theme.Reflection.G,
                        theme.Reflection.B
                    ),
                    .70
                ),
                new(Colors.Transparent, 1),
            },
        };
        rimBrush.StartPoint = new Point(0, 0);
        rimBrush.EndPoint = new Point(1, 1);
        rimBrush.GradientStops.Clear();
        rimBrush.GradientStops.Add(
            theme.Light || theme.Unified
                ? new(Color.FromArgb(170, theme.RimB.R, theme.RimB.G, theme.RimB.B), 0)
                : new(Color.FromArgb(174, 255, 255, 255), 0)
        );
        rimBrush.GradientStops.Add(
            new(Color.FromArgb((byte)(50 * theme.ReflectionGain), theme.RimA.R, theme.RimA.G, theme.RimA.B), .3)
        );
        rimBrush.GradientStops.Add(new(Color.FromArgb(23, 255, 255, 255), .50));
        rimBrush.GradientStops.Add(new(Color.FromArgb(105, theme.RimB.R, theme.RimB.G, theme.RimB.B), 1));
        accentRule.Height = theme.Kind switch
        {
            SurfaceKind.Poster => 4,
            SurfaceKind.Retro => 3,
            SurfaceKind.Bento => 5,
            SurfaceKind.Swiss => 2,
            SurfaceKind.Neon => 2,
            _ => 2,
        };
        accentRule.Visibility = theme.Kind
            is SurfaceKind.Paper
                or SurfaceKind.Ink
                or SurfaceKind.Poster
                or SurfaceKind.Retro
                or SurfaceKind.Bento
                or SurfaceKind.Swiss
                or SurfaceKind.Neon
            ? Visibility.Visible
            : Visibility.Collapsed;
        accentRule.Background = theme.Kind switch
        {
            SurfaceKind.Poster => new LinearGradientBrush(
                new GradientStopCollection { new(theme.HaloA, 0), new(theme.HaloB, .55), new(theme.HaloA, 1) },
                new Point(0, 0),
                new Point(1, 0)
            ),
            SurfaceKind.Ink => new LinearGradientBrush(
                Color.FromArgb(190, theme.Accent.R, theme.Accent.G, theme.Accent.B),
                Colors.Transparent,
                new Point(0, 0),
                new Point(1, 0)
            ),
            SurfaceKind.Paper => new SolidColorBrush(
                Color.FromArgb(90, theme.Accent.R, theme.Accent.G, theme.Accent.B)
            ),
            SurfaceKind.Retro => new LinearGradientBrush(
                Color.FromArgb(255, theme.Accent.R, theme.Accent.G, theme.Accent.B),
                Color.FromArgb(160, theme.Accent.R, theme.Accent.G, theme.Accent.B),
                new Point(0, 0),
                new Point(1, 0)
            ),
            SurfaceKind.Bento => new LinearGradientBrush(
                new GradientStopCollection { new(theme.HaloA, 0), new(theme.HaloB, .62), new(theme.HaloA, 1) },
                new Point(0, 0),
                new Point(1, 0)
            ),
            SurfaceKind.Swiss => new SolidColorBrush(theme.Accent),
            SurfaceKind.Neon => new LinearGradientBrush(
                new GradientStopCollection { new(theme.HaloA, 0), new(theme.HaloB, .5), new(theme.HaloA, 1) },
                new Point(0, 0),
                new Point(1, 0)
            ),
            _ => Brushes.Transparent,
        };
        glowFrame.BorderBrush = new SolidColorBrush(
            Color.FromArgb(200, theme.Accent.R, theme.Accent.G, theme.Accent.B)
        );
        glowFrame.Visibility = theme.Kind == SurfaceKind.Neon ? Visibility.Visible : Visibility.Collapsed;
        geometry = default;
        material = null;
        UpdateCorners();
    }

    // Each theme carries one signature shape so two themes never read as the same glass recoloured.
    static Brush? SignatureBrush(ThemeDefinition theme) =>
        theme.Signature switch
        {
            "band" => new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops = new()
                {
                    new(Colors.Transparent, .30),
                    new(theme.Halo(52, 1), .47),
                    new(
                        Color.FromArgb(
                            (byte)Math.Clamp(18 * theme.HaloGain, 0, 255),
                            theme.HaloA.R,
                            theme.HaloA.G,
                            theme.HaloA.B
                        ),
                        .56
                    ),
                    new(Colors.Transparent, .70),
                },
            },
            "sun" => new RadialGradientBrush
            {
                Center = new Point(.88, .98),
                GradientOrigin = new Point(.88, .98),
                RadiusX = .75,
                RadiusY = .95,
                GradientStops = new()
                {
                    new(theme.Halo(78, 1), 0),
                    new(theme.Halo(28, 0), .55),
                    new(Colors.Transparent, 1),
                },
            },
            "petals" => Petals(theme),
            "grid" => Grid(theme),
            "paper" => Paper(theme),
            "scan" => Scan(theme),
            "poster" => Poster(theme),
            "mist" => Mist(theme),
            "bento" => Bento(theme),
            "neon" => Neon(theme),
            "retro" => Retro(theme),
            "swiss" => Swiss(theme),
            "zen" => Zen(theme),
            _ => null,
        };

    // Grain fibres for the paper theme: a tiled sheet, never a stretched image.
    static Brush Paper(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        var fibre = new Pen(new SolidColorBrush(Color.FromArgb(22, 92, 72, 50)), .7);
        for (int i = 0; i < 26; i++)
        {
            double x = i * 37 % 120 + 4,
                y = i * 53 % 120 + 4;
            group.Children.Add(
                new GeometryDrawing(
                    null,
                    fibre,
                    new LineGeometry(new Point(x, y), new Point(x + 18 + i % 5 * 6, y + 2))
                )
            );
        }
        var speck = new SolidColorBrush(Color.FromArgb(12, 70, 55, 35));
        for (int i = 0; i < 44; i++)
        {
            double x = i * 29 % 120,
                y = i * 41 % 120;
            group.Children.Add(new GeometryDrawing(speck, null, new EllipseGeometry(new Point(x, y), .8, .8)));
        }
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 120, 120),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 120, 120),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
    }

    // Phosphor scanlines, one pixel of light every four.
    static Brush Scan(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        var line = new SolidColorBrush(Color.FromArgb(16, theme.HaloA.R, theme.HaloA.G, theme.HaloA.B));
        group.Children.Add(new GeometryDrawing(line, null, new RectangleGeometry(new Rect(0, 0, 4, 1))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 4, 4),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 4, 4),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
    }

    // A printed poster: an arc, a diagonal band and a halftone field.
    static Brush Poster(ThemeDefinition theme)
    {
        // Absolute coordinates: the motif keeps its size at any window width instead of stretching.
        var group = new DrawingGroup();
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(18, theme.HaloB.R, theme.HaloB.G, theme.HaloB.B)), 2);
        group.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(1050, 150), 150, 150)));
        group.Children.Add(
            new GeometryDrawing(
                new SolidColorBrush(Color.FromArgb(10, theme.HaloA.R, theme.HaloA.G, theme.HaloA.B)),
                null,
                new RectangleGeometry(new Rect(0, 520, 780, 54), 2, 2)
            )
        );
        var dot = new SolidColorBrush(Color.FromArgb(22, theme.HaloA.R, theme.HaloA.G, theme.HaloA.B));
        for (int row = 0; row < 5; row++)
        for (int column = 0; column < 10; column++)
            group.Children.Add(
                new GeometryDrawing(
                    dot,
                    null,
                    new EllipseGeometry(new Point(80 + column * 38, 620 + row * 22), 2.4, 2.4)
                )
            );
        return new DrawingBrush(group)
        {
            TileMode = TileMode.None,
            Viewbox = new Rect(0, 0, 1400, 900),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1400, 900),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
    }

    // Soft daylight: nothing but two blurred blooms and a faint grain.
    static Brush Mist(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        foreach (var (x, y, r) in new[] { (0.12, 0.08, 0.55), (0.9, 0.86, 0.6) })
        {
            var brush = new RadialGradientBrush(
                new GradientStopCollection
                {
                    new(theme.Halo(46, 0), 0),
                    new(theme.Halo(20, 1), .62),
                    new(Colors.Transparent, 1),
                }
            )
            {
                Center = new Point(x, y),
                GradientOrigin = new Point(x, y),
                RadiusX = r,
                RadiusY = r,
            };
            group.Children.Add(new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        }
        return new DrawingBrush(group) { Stretch = Stretch.Fill };
    }

    static Brush Petals(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        foreach (
            var (x, y, r) in new[] { (0.16, 0.78, 0.26), (0.84, 0.18, 0.22), (0.66, 0.92, 0.16), (0.34, 0.12, 0.12) }
        )
        {
            group.Children.Add(
                new GeometryDrawing(
                    new RadialGradientBrush
                    {
                        GradientStops = new() { new(theme.Halo(40, 0), 0), new(Colors.Transparent, 1) },
                    },
                    null,
                    new EllipseGeometry(new Point(x, y), r, r * .7)
                )
            );
        }
        return new DrawingBrush(group);
    }

    static Brush Grid(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        var pen = new Pen(
            new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(14 * theme.HaloGain, 0, 255), 255, 255, 255)),
            1
        );
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(28, 0))));
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(0, 28))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 28, 28),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
    }

    // Bento: a faint tray grid with one offset solid cell, so the surface reads as compartments.
    static Brush Bento(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(9, theme.Text.R, theme.Text.G, theme.Text.B)), 1);
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(240, 0))));
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(0, 240))));
        var dot = new SolidColorBrush(Color.FromArgb(30, theme.HaloA.R, theme.HaloA.G, theme.HaloA.B));
        group.Children.Add(new GeometryDrawing(dot, null, new RectangleGeometry(new Rect(0, 0, 2, 2))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 240, 240),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 240, 240),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
    }

    // Néon: two tubes bleeding colour into a dark room, one from each corner.
    static Brush Neon(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        foreach (var (x, y, r, which) in new[] { (0.02, 0.98, 0.85, 0), (0.98, 0.02, 0.8, 1) })
        {
            group.Children.Add(
                new GeometryDrawing(
                    new RadialGradientBrush
                    {
                        GradientStops = new()
                        {
                            new(theme.Halo(58, which), 0),
                            new(theme.Halo(20, which), .5),
                            new(Colors.Transparent, 1),
                        },
                    },
                    null,
                    new RectangleGeometry(new Rect(0, 0, 1, 1))
                )
            );
        }
        return new DrawingBrush(group) { Stretch = Stretch.Fill };
    }

    // Rétro: the classic two-pixel desktop dither.
    static Brush Retro(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        var dot = new SolidColorBrush(Color.FromArgb(18, 0, 0, 0));
        group.Children.Add(new GeometryDrawing(dot, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        group.Children.Add(new GeometryDrawing(dot, null, new RectangleGeometry(new Rect(2, 2, 1, 1))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 4, 4),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 4, 4),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
    }

    // Suisse: a single hairline grid, almost invisible, plus one red rule at the top.
    static Brush Swiss(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(9, 0, 0, 0)), 1);
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(0, 1000))));
        group.Children.Add(
            new GeometryDrawing(
                new SolidColorBrush(Color.FromArgb(255, theme.Accent.R, theme.Accent.G, theme.Accent.B)),
                null,
                new RectangleGeometry(new Rect(0, 0, 52, 3))
            )
        );
        return new DrawingBrush(group)
        {
            TileMode = TileMode.None,
            Viewbox = new Rect(0, 0, 1000, 1000),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1000, 1000),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
    }

    // Zen: raked sand, horizontal grooves with a slow drift.
    static Brush Zen(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(14, 92, 84, 64)), 1);
        for (int i = 0; i < 9; i++)
            group.Children.Add(
                new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 4 + i * 9), new Point(1000, 4 + i * 9)))
            );
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 1000, 90),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1000, 90),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
    }

    static Color colorWith(Color color, double gain) =>
        Color.FromArgb((byte)Math.Clamp(color.A * gain, 0, 255), color.R, color.G, color.B);

    internal void RefreshTheme()
    {
        themeVersion = -1;
        ApplyGlass();
    }

    // A theme swap breathes instead of snapping: the surface dips and comes back in the new colours.
    internal void FlashTheme()
    {
        if (!Motion.On || !IsVisible)
            return;
        ContentHost.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(.72, 1, Motion.Span(230)) { EasingFunction = Motion.Ease }
        );
        var glow = new DoubleAnimation(.55, 1, Motion.Span(320)) { EasingFunction = Motion.Ease };
        ambient.BeginAnimation(UIElement.OpacityProperty, glow);
    }

    internal void ApplyGlass()
    {
        if (themeVersion != Theme.Version)
            BuildThemeLayers();
        tintLayer.Visibility =
            ambient.Visibility =
            reflections.Visibility =
            rim.Visibility =
                MaterialVisible ? Visibility.Visible : Visibility.Collapsed;
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
        double x = Left,
            y = Top;
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

    static DoubleAnimation Tween(double from, double to, double milliseconds) =>
        new(from, to, Motion.Span(milliseconds * Store.State.Motion))
        {
            EasingFunction = Motion.Ease,
            FillBehavior = FillBehavior.Stop,
        };

    internal void TransitionTo(Rect rect, int index, bool animate)
    {
        if (TopAttached && IsVisible && animate && Motion.On && Math.Abs(Height - rect.Height) > .5)
        {
            double from = Height;
            CancelMotion();
            Left = TargetLeft = rect.X;
            Top = TargetTop = rect.Y;
            Width = rect.Width;
            Height = rect.Height;
            BeginAnimation(HeightProperty, Tween(from, rect.Height, 250), HandoffBehavior.SnapshotAndReplace);
            ApplyGlass();
            return;
        }
        if (Offscreen)
            rect.Offset(CaptureOffset.X, CaptureOffset.Y);
        if (!IsVisible || Math.Abs(Width - rect.Width) > .5 || Math.Abs(Height - rect.Height) > .5)
        {
            MoveInto(rect.X, rect.Y, rect.Width, rect.Height, index, animate);
            return;
        }
        if (Math.Abs(TargetLeft - rect.X) < .5 && Math.Abs(TargetTop - rect.Y) < .5)
        {
            ApplyGlass();
            return;
        }
        double x = Left,
            y = Top;
        CancelMotion();
        TargetLeft = rect.X;
        TargetTop = rect.Y;
        Left = rect.X;
        Top = rect.Y;
        int version = motionVersion;
        if (animate && Motion.On)
        {
            void Axis(DependencyProperty property, double from, double to)
            {
                if (Math.Abs(from - to) < .5)
                    return;
                var slide = Tween(from, to, 175);
                slide.Completed += (_, _) =>
                {
                    if (version == motionVersion)
                        BeginAnimation(property, null);
                };
                BeginAnimation(property, slide, HandoffBehavior.SnapshotAndReplace);
            }
            Axis(LeftProperty, x, rect.X);
            Axis(TopProperty, y, rect.Y);
        }
        ApplyGlass();
    }

    internal void HideImmediately()
    {
        CancelMotion();
        if (IsVisible)
            Hide();
    }

    // A window already closing (app shutdown, space collapsing) must not be shown again.
    internal void MoveInto(double x, double y, double width, double height, int index, bool animate)
    {
        if (Offscreen)
        {
            x += CaptureOffset.X;
            y += CaptureOffset.Y;
        }
        bool opening = !IsVisible;
        bool unchanged =
            Math.Abs(Left - x) < .5
            && Math.Abs(Top - y) < .5
            && Math.Abs(Width - width) < .5
            && Math.Abs(Height - height) < .5;
        if (!opening && unchanged)
        {
            ApplyGlass();
            return;
        }
        CancelMotion();
        TargetLeft = x;
        TargetTop = y;
        Width = width;
        Height = height;
        Top = y;
        Left = x;
        if (opening)
        {
            try
            {
                Show();
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }
        ApplyGlass();
        if (opening && animate && Motion.On && TopAttached)
        {
            drawerSlide.BeginAnimation(
                TranslateTransform.YProperty,
                Tween(-height, 0, 300),
                HandoffBehavior.SnapshotAndReplace
            );
            Surface.BeginAnimation(UIElement.OpacityProperty, Tween(.25, 1, 190), HandoffBehavior.SnapshotAndReplace);
            return;
        }
        if (opening && animate && Motion.On)
        {
            int version = motionVersion;
            var slide = Tween(14, 0, 160);
            slide.Completed += (_, _) =>
            {
                if (version != motionVersion)
                    return;
                motion.BeginAnimation(TranslateTransform.XProperty, null);
                ContentHost.BeginAnimation(UIElement.OpacityProperty, null);
            };
            motion.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);
            ContentHost.BeginAnimation(
                UIElement.OpacityProperty,
                Tween(.65, 1, 160),
                HandoffBehavior.SnapshotAndReplace
            );
            Motion.BlurReveal(ContentHost);
        }
    }

    internal void SlideOut(int index, Action completed)
    {
        if (!IsVisible)
        {
            completed();
            return;
        }
        if (TopAttached)
        {
            double from = drawerSlide.Y;
            CancelMotion();
            if (!Motion.On)
            {
                Hide();
                completed();
                return;
            }
            int expected = motionVersion;
            var animation = Tween(from, -ActualHeight, 190);
            animation.Completed += (_, _) =>
            {
                if (expected == motionVersion)
                {
                    HideImmediately();
                    completed();
                }
            };
            drawerSlide.BeginAnimation(TranslateTransform.YProperty, animation, HandoffBehavior.SnapshotAndReplace);
            return;
        }
        double current = motion.X,
            opacity = ContentHost.Opacity;
        CancelMotion();
        if (!Motion.On)
        {
            Hide();
            completed();
            return;
        }
        int version = motionVersion;
        var slide = Tween(current, 12, 100);
        slide.Completed += (_, _) =>
        {
            if (version != motionVersion)
                return;
            HideImmediately();
            completed();
        };
        motion.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);
        ContentHost.BeginAnimation(
            UIElement.OpacityProperty,
            Tween(opacity, .3, 100),
            HandoffBehavior.SnapshotAndReplace
        );
    }
}
