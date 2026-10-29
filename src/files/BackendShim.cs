// Minimal stand-ins for the backend types that the linked Tokens.cs / UiTokens.cs depend on
// (Theme, Store, ThemeDefinition, WallpaperMonitor, UI helpers, Motion). Palette starts as M3Palette.DarkDefault(), swapped by PaletteLoader.
using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PanneauxGlass;

internal sealed class ThemeDefinition { public Color Surface, Text, Accent, Dim; public bool Light; }
internal sealed class WallpaperMonitor { public int x, y; public string? path, background; public long modified; }
// Same JSON keys as the backend Preferences (preferences-glass.json): only what the palette decision tree needs.
internal sealed class Preferences {
    public bool? ReducedEffects { get; set; } public bool AutoTheme { get; set; } public string Theme { get; set; } = "nuit";
    public System.Collections.Generic.Dictionary<string, string> CustomPalette { get; set; } = new();
}

internal sealed class CurrentTheme {
    M3Palette palette = M3Palette.DarkDefault();
    internal bool IsDark => palette.Dark;
    internal void Set(M3Palette next) => palette = next;
    internal Color Role(M3Role role) => role switch {
        M3Role.Surface => palette.Surface, M3Role.SurfaceLow => palette.SurfaceLow, M3Role.SurfaceContainer => palette.SurfaceContainer,
        M3Role.SurfaceHigh => palette.SurfaceHigh, M3Role.SurfaceHighest => palette.SurfaceHighest, M3Role.OnSurface => palette.OnSurface,
        M3Role.OnSurfaceVariant => palette.OnSurfaceVariant, M3Role.Primary => palette.Primary, M3Role.OnPrimary => palette.OnPrimary,
        M3Role.PrimaryContainer => palette.PrimaryContainer, M3Role.OnPrimaryContainer => palette.OnPrimaryContainer,
        M3Role.SecondaryContainer => palette.SecondaryContainer, M3Role.OnSecondaryContainer => palette.OnSecondaryContainer,
        M3Role.Outline => palette.Outline, M3Role.OutlineVariant => palette.OutlineVariant, M3Role.Error => palette.Error, _ => palette.OnError };
}

internal static class Theme {
    internal static readonly CurrentTheme Current = new();
    internal static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}

// Same install folder as the backend (Glass-v5): reducedEffects is read from its preferences file.
internal static class Store {
    internal static string Root = AppDomain.CurrentDomain.BaseDirectory;
    internal static Preferences State = Load();
    internal static void Reload() => State = Load();
    static Preferences Load() {
        var prefs = new Preferences();
        try {
            string path = Environment.GetEnvironmentVariable("WINLESTIA_FILES_PALETTE_FILE") is { Length: > 0 } over ? over : Path.Combine(Root, "preferences-glass.json");   // override: tests only
            if (File.Exists(path)) prefs = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? prefs;
        } catch { }
        return prefs;
    }
}

internal static partial class UI {
    // Repaints every live token brush in place after Theme.Current.Set (RepaintTokens lives in UiTokens.cs).
    internal static void RepaintAll() => RepaintTokens();
    internal static SolidColorBrush White => T(M3Role.OnSurface);
    // Files-only palette tokens (live, repainted by RepaintAll): panels from surfaceContainer, recess from surfaceHigh, hover tinted with primary.
    internal static SolidColorBrush Panel => T(M3Role.SurfaceContainer, 0xF2);
    internal static SolidColorBrush Recess => T(M3Role.SurfaceHigh);
    internal static SolidColorBrush Tint => T(M3Role.Primary, 40);
    internal static SolidColorBrush Accent => T(M3Role.Primary);
    internal static TextBlock Text(string value, double size = 14, Brush? color = null, bool bold = false) =>
        new() { Text = value, FontSize = size, Foreground = color ?? White, FontFamily = M3.Body, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
    internal static TextBlock Glyph(string code, double size = 18) =>
        new() { Text = code, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size, Foreground = White, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    internal static Button Button(string label, Action action) {
        var b = Flat(label, 36, new CornerRadius(M3.Small), T(M3Role.SecondaryContainer));
        b.Padding = new Thickness(M3.Sp4, 0, M3.Sp4, 0);
        b.Click += (_, _) => action();
        return b;
    }
    // Flat rounded button with a hover layer; content is a string or an element.
    internal static Button Flat(object content, double size, CornerRadius radius, Brush? rest = null) {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, radius);
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var b = new Button { Content = content, Height = size, MinWidth = size, Template = new ControlTemplate(typeof(Button)) { VisualTree = border },
            Background = rest ?? Brushes.Transparent, Foreground = White, FontFamily = M3.Body, FontSize = M3.FontSmall, Cursor = System.Windows.Input.Cursors.Hand, OverridesDefaultStyle = true };
        var restBrush = b.Background;
        b.MouseEnter += (_, _) => { if (b.IsEnabled) b.Background = Tint; };
        b.MouseLeave += (_, _) => b.Background = restBrush;
        b.IsEnabledChanged += (_, _) => b.Opacity = b.IsEnabled ? 1 : .38;
        return b;
    }
}

internal static class Motion {
    internal static void Reveal(FrameworkElement element) {
        if (M3.ReducedEffects) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(M3.AnimFast)));
    }
}
