// language: C# 12, file: ShellChrome.cs, target: Windows 11 / .NET 8 WPF, contenu de la poignée latérale et de la barre d'outils
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PanneauxGlass;

// Actions de la barre d'outils : la construction visuelle ne connaît ni le Coordinator ni les autres fenêtres.
internal sealed record ToolbarCommands(Action Palette, Action Home, Action ResetLayout, Action RefreshBoards, Action Settings,
    Action Rescue, Action Collapse);

// Extraits de BuildRail/BuildToolbar : construction d'éléments WPF sans état propre, l'état partagé arrive par ShellState.
internal static class ShellChrome
{
    // Fond des boutons à bascule : teinte d'accent quand la bascule est active, voile discret sinon.
    static Brush Toggled(bool on) => on ? UI.Brush("#45C5E8FF") : UI.Brush("#0FFFFFFF");
    internal static void BuildRail(GlassWindow rail, Action open)
    {
        rail.ContentHost.Children.Clear();
        rail.Width = Store.State.RailWidth;
        rail.KeepOpen = true;
        rail.Radius = 32;
        var hit = UI.Button("", open);
        hit.Padding = new Thickness(0);
        hit.BorderThickness = new Thickness(0);
        hit.BorderBrush = Brushes.Transparent;
        hit.Background = Brushes.Transparent;
        hit.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        hit.VerticalContentAlignment = VerticalAlignment.Stretch;
        hit.ToolTip = "Ouvrir ton espace · " + GlobalShortcut.Label(Store.State.Shortcut.Modifiers, Store.State.Shortcut.Key);
        System.Windows.Automation.AutomationProperties.SetName(hit, "Ouvrir Panneaux Glass");
        var layout = new Grid();
        layout.Children.Add(new Border
        {
            Width = 3,
            Height = 100,
            CornerRadius = new CornerRadius(2),
            Background = UI.Accent,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        var glyph = UI.Glyph("\uE8A9", 20);
        glyph.VerticalAlignment = VerticalAlignment.Top;
        glyph.Margin = new Thickness(0, 28, 0, 0);
        glyph.Opacity = .9;
        layout.Children.Add(glyph);
        layout.Children.Add(new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = UI.Accent,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 28)
        });
        hit.Content = layout;
        rail.ContentHost.Children.Add(hit);
    }
    // Renvoie la pastille de la commande rapide : le Coordinator doit pouvoir rafraîchir son libellé quand le raccourci change.
    internal static Button BuildToolbar(GlassWindow toolbar, ShellState state, string paletteLabel, ToolbarCommands commands)
    {
        toolbar.Radius = 30;
        toolbar.Height = LayoutRules.ToolbarHeight;
        toolbar.ContentHost.Margin = new Thickness(18, 10, 12, 10);
        var row = new Grid();
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 30, 0) };
        var logo = UI.Glyph("\uE8A9", 22);
        logo.Margin = new Thickness(5, 0, 12, 0);
        brand.Children.Add(logo);
        brand.Children.Add(UI.Text("Panneaux", 18, null, true));
        var dj = UI.Text(" / dj", 13, UI.Dim);
        dj.VerticalAlignment = VerticalAlignment.Center;
        brand.Children.Add(dj);
        row.Children.Add(brand);
        // Le champ vit désormais dans la commande rapide (Ctrl F2) : la barre ne garde que l'espace et ses raccourcis.
        var hint = UI.Text("Ctrl F2 · tout ton espace en une ligne", 12, UI.Dim);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Opacity = .75;
        Grid.SetColumn(hint, 1);
        row.Children.Add(hint);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 0, 0, 0) };
        var favorite = UI.IconButton("\uE734", "Afficher uniquement les favoris", () =>
        {
            state.FavoritesOnly = !state.FavoritesOnly;
            commands.RefreshBoards();
        });
        favorite.Click += (_, _) => favorite.Background = Toggled(state.FavoritesOnly);
        var chip = UI.Button(paletteLabel, commands.Palette);
        chip.Padding = new Thickness(11, 7, 11, 7);
        chip.FontSize = 12;
        chip.ToolTip = "Commande rapide · " + paletteLabel;
        System.Windows.Automation.AutomationProperties.SetName(chip, "Ouvrir la commande rapide");
        actions.Children.Add(chip);
        actions.Children.Add(UI.IconButton("\uE80F", "Revenir à l’accueil", commands.Home));
        actions.Children.Add(UI.IconButton("\uE8A9", "Rétablir la disposition des panneaux", commands.ResetLayout));
        actions.Children.Add(favorite);
        var pin = UI.IconButton("\uE718", "Garder l’espace ouvert", () => { state.Pinned = !state.Pinned; });
        pin.Background = Toggled(state.Pinned);
        pin.Click += (_, _) => pin.Background = Toggled(state.Pinned);
        actions.Children.Add(pin);
        actions.Children.Add(UI.IconButton("\uE713", "Personnaliser l’espace", commands.Settings));
        actions.Children.Add(UI.IconButton("\uE777", "Retrouver le Bureau organisé", commands.Rescue));
        actions.Children.Add(UI.IconButton("\uE711", "Replier l’espace", commands.Collapse));
        foreach (Button button in actions.Children)
            button.Margin = new Thickness(3, 0, 0, 0);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        toolbar.ContentHost.Children.Add(row);
        toolbar.KeyDown += (_, e) => { if (e.Key == Key.Escape) commands.Collapse(); };
        return chip;
    }
}
