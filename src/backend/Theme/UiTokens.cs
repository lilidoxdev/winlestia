// C# 12, fichier UiTokens.cs, cible Windows 11 / .NET 8 WPF, pinceaux de jetons et blocs partagés pour les fenêtres de l'Atelier
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PanneauxGlass;

internal static partial class UI
{
    // Pinceaux de jetons vivants : un pinceau en cache par (rôle, alpha). Ils sont repeints sur place lors d'un changement
    // de thème (voir Repaint), exactement comme UI.Brush, pour que les fenêtres déjà à l'écran suivent le shell
    // sans être reconstruites. C'est pourquoi ils restent non gelés : un pinceau gelé ne pourrait pas suivre.
    static readonly Dictionary<(M3Role, byte), SolidColorBrush> tokenBrushes = new();
    static Color TokenColor(M3Role role, byte alpha)
    {
        var c = Theme.Current.Role(role);
        return System.Windows.Media.Color.FromArgb(alpha, c.R, c.G, c.B);
    }
    internal static SolidColorBrush T(M3Role role, byte alpha = 255)
    {
        if (!tokenBrushes.TryGetValue((role, alpha), out var brush))
        {
            brush = new SolidColorBrush(TokenColor(role, alpha));
            tokenBrushes[(role, alpha)] = brush;
        }
        return brush;
    }
    // Un pinceau gelé dans notre dos (WPF gèle les Freezables qu'il scelle dans des modèles) ne peut pas être repeint : on le retire
    // du cache pour que T() en donne un vivant, sans jamais lever d'exception.
    static void RepaintTokens()
    {
        List<(M3Role, byte)>? frozen = null;
        foreach (var pair in tokenBrushes)
        {
            if (pair.Value.IsFrozen)
            {
                (frozen ??= new()).Add(pair.Key);
                continue;
            }
            try
            {
                pair.Value.Color = TokenColor(pair.Key.Item1, pair.Key.Item2);
            }
            catch (InvalidOperationException) { (frozen ??= new()).Add(pair.Key); }
        }
        if (frozen != null)
            foreach (var key in frozen)
                tokenBrushes.Remove(key);
    }
    // Couches d'état (M3) : survol 8 %, appui 12 % de onSurface.
    internal const byte HoverLayer = 20, PressedLayer = 31;
    internal static SolidColorBrush Hover => T(M3Role.OnSurface, HoverLayer);
    internal static SolidColorBrush Pressed => T(M3Role.OnSurface, PressedLayer);
    internal static SolidColorBrush Selected => T(M3Role.PrimaryContainer);
    internal static SolidColorBrush Label => T(M3Role.OnSurfaceVariant);
    internal static SolidColorBrush Hairline => T(M3Role.OutlineVariant);
    // Conteneurs : la carte sur une fenêtre, une carte surélevée, un creux de saisie.
    internal static SolidColorBrush Card => T(M3Role.OnSurface, 15);
    internal static SolidColorBrush CardHigh => T(M3Role.OnSurface, 28);

    internal static TextBlock Symbol(string material, string fluent, double size = 18)
    {
        var symbols = M3.Symbols;
        return symbols != null
            ? new TextBlock { Text = material, FontFamily = symbols, FontSize = size, Foreground = White,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center }
            : Glyph(fluent, size);
    }
    internal static Button TextButton(string label, Action action)
    {
        var b = Button(label, action);
        b.Tag = "m3text";
        return b;
    }

    // Un bloc vide / message pour toutes les fenêtres d'outils : icône, titre, texte secondaire et action facultative.
    // Les points de code Material Symbols sont utilisés quand la police est livrée avec l'application ; Segoe Fluent sinon.
    internal static FrameworkElement EmptyState(string material, string fluent, string title, string detail, string? actionLabel = null,
        Action? action = null)
    {
        var icon = Symbol(material, fluent, 30);
        icon.Foreground = T(M3Role.OnSecondaryContainer);
        var disc = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Background = T(M3Role.SecondaryContainer),
            Child = icon, HorizontalAlignment = HorizontalAlignment.Center };
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 340, Margin = new Thickness(M3.Sp5) };
        stack.Children.Add(disc);
        var heading = Text(title, M3.FontTitle, White, true);
        heading.TextAlignment = TextAlignment.Center;
        heading.TextWrapping = TextWrapping.Wrap;
        heading.Margin = new Thickness(0, M3.Sp4, 0, M3.Pad1);
        heading.FontWeight = FontWeights.SemiBold;
        var body = Text(detail, M3.FontSmall, Label);
        body.TextAlignment = TextAlignment.Center;
        body.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(heading);
        stack.Children.Add(body);
        if (actionLabel != null && action != null)
        {
            var button = Button(actionLabel, action);
            button.Margin = new Thickness(0, M3.Sp4, 0, 0);
            button.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(button);
        }
        Motion.Reveal(stack);
        return stack;
    }
}
