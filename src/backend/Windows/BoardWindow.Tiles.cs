// C# 12, fichier BoardWindow.cs, cible Windows 11 / .NET 8 WPF, quatre carrousels d'icônes configurables
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls.Primitives;

namespace PanneauxGlass;

internal sealed partial class BoardWindow : GlassWindow
{
    Button CreateTile(Entry entry, double icon, int number = 0)
    {
        string mode = Theme.Current.Tiles;
        var button = new Button
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6),
            Margin = new Thickness(4, 3, 4, 3),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            ToolTip = entry.Name + "\n" + entry.Category + "\n" + entry.Target
        };
        System.Windows.Automation.AutomationProperties.SetName(button, entry.Name);
        FrameworkElement content;
        if (mode == "list")
        {
            button.Background = UI.Alpha(UI.White.Color, 10);
            button.BorderBrush = UI.Alpha(UI.White.Color, 20);
            button.Padding = new Thickness(12, 7, 12, 7);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            var row = new DockPanel();
            var picture = new Image { Width = 28, Height = 28, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 13, 0) };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            owner.Icons.Load(entry, source => picture.Source = source, Model.Id);
            row.Children.Add(picture);
            var text = new StackPanel();
            var label = UI.Text(entry.Name, 13, null, true);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(label);
            var category = UI.Text(entry.Category, 10, UI.Dim);
            category.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(category);
            row.Children.Add(text);
            content = row;
        }
        else if (mode == "bento")
        {
            // Bento : un compartiment vertical, bloc de couleur derrière le logo, nom en bas.
            button.Background = number % 2 == 0 ? UI.Alpha(UI.White.Color, 13) : UI.Alpha(UI.Accent.Color, 23);
            button.BorderBrush = UI.Alpha(UI.White.Color, 26);
            button.BorderThickness = new Thickness(1);
            button.Padding = new Thickness(17, 15, 16, 14);
            button.Margin = new Thickness(5);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.VerticalContentAlignment = VerticalAlignment.Stretch;
            button.MinHeight = 118;
            var cell = new Grid();
            cell.RowDefinitions.Add(new()
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
            cell.RowDefinitions.Add(new()
            {
                Height = GridLength.Auto
            });
            var picture = new Image
            {
                Width = Math.Clamp(icon * .72, 34, 50),
                Height = Math.Clamp(icon * .72, 34, 50),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            owner.Icons.Load(entry, source => picture.Source = source, Model.Id);
            cell.Children.Add(picture);
            var foot = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
            Grid.SetRow(foot, 1);
            var name = UI.Text(entry.Name, 13, null, true);
            name.TextWrapping = TextWrapping.Wrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.MaxHeight = 34;
            foot.Children.Add(name);
            var category = UI.Text(entry.Category.ToUpperInvariant(), 9, UI.Dim, true);
            category.TextTrimming = TextTrimming.CharacterEllipsis;
            category.Margin = new Thickness(0, 4, 0, 0);
            foot.Children.Add(category);
            cell.Children.Add(foot);
            content = cell;
        }
        else if (mode == "index")
        {
            // Suisse : un index imprimé — numéro, logo, nom, catégorie, un filet sous chaque ligne.
            button.Background = Brushes.Transparent;
            button.BorderBrush = UI.Alpha(UI.White.Color, 48);
            button.BorderThickness = new Thickness(0, 0, 0, 1);
            button.Padding = new Thickness(2, 9, 8, 9);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            var row = new Grid();
            row.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(42)
            });
            row.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(32)
            });
            row.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            row.ColumnDefinitions.Add(new()
            {
                Width = GridLength.Auto
            });
            var ordinal = UI.Text(number.ToString("00"), 11, UI.Dim);
            ordinal.FontFamily = UI.Mono;
            ordinal.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(ordinal);
            var picture = new Image
            {
                Width = 22,
                Height = 22,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            owner.Icons.Load(entry, source => picture.Source = source, Model.Id);
            Grid.SetColumn(picture, 1);
            row.Children.Add(picture);
            var label = UI.Text(entry.Name, 14, null, true);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 2);
            row.Children.Add(label);
            var category = UI.Text(entry.Category.ToUpperInvariant(), 9, UI.Dim);
            category.VerticalAlignment = VerticalAlignment.Center;
            category.Margin = new Thickness(14, 0, 0, 0);
            Grid.SetColumn(category, 3);
            row.Children.Add(category);
            content = row;
        }
        else if (mode == "stack")
        {
            // Zen : une ligne large et calme avec de l'espace autour du logo et un chevron à la fin.
            button.Background = Brushes.Transparent;
            button.BorderBrush = UI.Alpha(UI.White.Color, 34);
            button.BorderThickness = new Thickness(0, 0, 0, 1);
            button.Padding = new Thickness(10, 11, 12, 11);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            var row = new DockPanel();
            var chevron = UI.Glyph("\uE76C", 12);
            chevron.Foreground = UI.Dim;
            chevron.Opacity = .8;
            DockPanel.SetDock(chevron, Dock.Right);
            row.Children.Add(chevron);
            var picture = new Image
            {
                Width = 34,
                Height = 34,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(4, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            owner.Icons.Load(entry, source => picture.Source = source, Model.Id);
            DockPanel.SetDock(picture, Dock.Left);
            row.Children.Add(picture);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var label = UI.Text(entry.Name, 15, null, true);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(label);
            var category = UI.Text(entry.Category, 11, UI.Dim);
            category.TextTrimming = TextTrimming.CharacterEllipsis;
            category.Margin = new Thickness(0, 3, 0, 0);
            text.Children.Add(category);
            row.Children.Add(text);
            content = row;
        }
        else if (mode == "cards")
        {
            button.Background = UI.Alpha(UI.White.Color, 15);
            button.BorderBrush = UI.Alpha(UI.White.Color, 30);
            button.BorderThickness = new Thickness(Theme.Current.BorderWeight);
            button.Padding = new Thickness(14, 13, 14, 12);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.VerticalContentAlignment = VerticalAlignment.Top;
            var card = new StackPanel();
            var picture = new Image
            {
                Width = Math.Clamp(icon * .62, 30, 44),
                Height = Math.Clamp(icon * .62, 30, 44),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            owner.Icons.Load(entry, source => picture.Source = source, Model.Id);
            card.Children.Add(picture);
            var label = UI.Text(entry.Name, 13, null, true);
            label.TextWrapping = TextWrapping.Wrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.MaxHeight = 36;
            label.Margin = new Thickness(0, 10, 0, 0);
            card.Children.Add(label);
            var category = UI.Text(entry.Category, 11, UI.Dim);
            category.TextTrimming = TextTrimming.CharacterEllipsis;
            category.Margin = new Thickness(0, 3, 0, 0);
            card.Children.Add(category);
            content = card;
        }
        else
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var picture = new Image { Width = icon, Height = icon, Stretch = Stretch.Uniform, Opacity = 1 };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            owner.Icons.Load(entry, source => picture.Source = source, Model.Id);
            stack.Children.Add(picture);
            var label = UI.Text(entry.Name, 12);
            label.TextAlignment = TextAlignment.Center;
            label.TextWrapping = TextWrapping.Wrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.MaxHeight = 35;
            label.Margin = new Thickness(0, 10, 0, 0);
            label.LineHeight = 16;
            stack.Children.Add(label);
            content = stack;
        }
        var layout = new Grid();
        layout.Children.Add(content);
        if (Store.State.Favorites.Contains(entry.Id))
        {
            var star = UI.Text("★", 12, UI.Brush(Model.Accent));
            star.HorizontalAlignment = HorizontalAlignment.Right;
            star.VerticalAlignment = VerticalAlignment.Top;
            star.Margin = new Thickness(0, 2, 5, 0);
            layout.Children.Add(star);
        }
        button.Content = layout;
        button.Click += (_, _) => { if (DateTime.UtcNow >= blockClick) owner.Launch(entry); };
        button.MouseRightButtonUp += (_, e) => { OpenEntryMenu(entry, button); e.Handled = true; };
        button.PreviewMouseLeftButtonDown += (_, e) => dragStart = e.GetPosition(button);
        button.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed)
                return;
            var point = e.GetPosition(button);
            if ((point - dragStart).Length < 9)
                return;
            blockClick = DateTime.UtcNow.AddMilliseconds(450);
            DragDrop.DoDragDrop(button,
            new DataObject("PanneauxEntry", entry.Id), DragDropEffects.Move);
            blockClick = DateTime.UtcNow.AddMilliseconds(450);
        };
        button.AllowDrop = true;
        button.Drop += (_, e) => HandleDrop(e, entry.Id);
        return button;
    }
    void OpenEntryMenu(Entry entry, Button anchor)
    {
        blockClick = DateTime.UtcNow.AddMilliseconds(350);
        GlassMenu.Open(owner, this, anchor, entry.Name, new[] { new MenuAction("Ouvrir", "\uE8A7", () => owner.Launch(entry)),
            new MenuAction(Store.State.Favorites.Contains(entry.Id) ? "Retirer des favoris" : "Ajouter aux favoris", "\uE734", () => {
            if (!Store.State.Favorites.Remove(entry.Id)) Store.State.Favorites.Add(entry.Id); Store.Save(); owner.RefreshBoards(); }),
            new MenuAction(Store.State.TaskbarPins.Contains(entry.Id) ? "Détacher de la barre" : "Épingler à la barre", "\uE718", ()
            => owner.ToggleTaskbarPin(entry)), new MenuAction("Personnaliser cet accès…", "\uE70F", () => owner.Edit(entry),
            Separator: true), new MenuAction("Afficher l’emplacement", "\uE8B7", () => owner.Reveal(entry)),
            new MenuAction("Copier le chemin", "\uE8C8", () => System.Windows.Clipboard.SetText(entry.Target)),
            new MenuAction("Masquer cet accès", "\uE890", () => { Store.State.Hidden.Add(entry.Id); Store.Save(); owner.RefreshBoards();
                },
            Separator: true) }, true);
    }
    void HandleDrop(DragEventArgs e, string? targetId)
    {
        blockClick = DateTime.UtcNow.AddMilliseconds(450);
        if (e.Data.GetDataPresent("PanneauxEntry"))
        {
            string id = (string)e.Data.GetData("PanneauxEntry");
            Store.State.BoardOverrides[id] = Model.Id;
            var destination = Store.Sorted(Store.Entries.Where(x => Store.BoardFor(x) == Model.Id
                && !Store.State.Hidden.Contains(x.Id))).Select(x => x.Id).ToList();
            destination.Remove(id);
            int index = targetId == null ? -1 : destination.IndexOf(targetId);
            if (index < 0)
                destination.Add(id);
            else
                destination.Insert(index, id);
            Store.State.Order = Store.State.Order.Where(x => !destination.Contains(x)).Concat(destination).ToList();
            Store.Save();
            owner.RefreshBoards();
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            foreach (string path in (string[])e.Data.GetData(DataFormats.FileDrop))
                owner.AddPath(path, Model.Id);
            owner.RefreshBoards();
        }
        e.Handled = true;
    }
}
