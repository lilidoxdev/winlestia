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
    readonly Coordinator owner; internal BoardState Model; readonly TextBlock heading, caption, pageText; readonly Button selector,
        previous, next, star;
    readonly StackPanel filters = new() { Orientation = Orientation.Horizontal }; readonly Grid itemsHost = new();
        readonly Border emblem = new();
    List<Entry> filtered = new(); int page, capacity = 24, columns = 6, rows = 3; string quick = "", renderKey = "", quickKey = "~";
        bool rendering; Point dragStart; DateTime blockClick;
    internal BoardWindow(Coordinator coordinator, BoardState model) : base("Panneaux Glass — " + model.Title)
    {
        owner = coordinator;
        Model = model;
        ContentHost.Margin = new Thickness(28, 23, 28, 19);
        ContentHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ContentHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ContentHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ContentHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid();
        header.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        header.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        header.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        var sectionGlyph = UI.Glyph(model.Id switch
        {
            "apps" => "\uE71D",
            "games" => "\uE7FC",
            "projects" => "\uE943",
            _ => "\uE8B7"
        }, 22);
        emblem.Child = sectionGlyph;
        emblem.Width = 48;
        emblem.Height = 48;
        emblem.CornerRadius = new CornerRadius(17);
        emblem.Background = UI.T(M3Role.OnSurface, 0x17);
        emblem.BorderBrush = UI.T(M3Role.OnSurface, 0x28);
        emblem.BorderThickness = new Thickness(1);
        emblem.Margin = new Thickness(0, 0, 15, 0);
        header.Children.Add(emblem);
        var titleHit = new Grid { Background = Brushes.Transparent };
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        heading = UI.Text(model.Title, 25, null, true);
        caption = UI.Text("", 12, UI.Dim);
        caption.Margin = new Thickness(0, 5, 0, 0);
        titles.Children.Add(heading);
        titles.Children.Add(caption);
        titleHit.Children.Add(titles);
        Grid.SetColumn(titleHit, 1);
        header.Children.Add(titleHit);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        star = UI.IconButton("\uE734", "Favoris · " + model.Title, () => { Model.FavoritesOnly = !Model.FavoritesOnly; page = 0;
            Store.Save(); Refresh(); });
        star.Margin = new Thickness(0, 0, 7, 0);
        actions.Children.Add(star);
        var add = UI.IconButton("\uE710", "Ajouter un accès à " + model.Title, () => owner.AddAccess(model.Id));
        add.Margin = new Thickness(0, 0, 7, 0);
        actions.Children.Add(add);
        actions.Children.Add(UI.IconButton("\uE713", "Personnaliser " + model.Title, () => owner.Settings(model.Id)));
        Grid.SetColumn(actions, 2);
        header.Children.Add(actions);
        ContentHost.Children.Add(header);
        var filterRow = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 23, 0, 17) };
        Grid.SetRow(filterRow, 1);
        ContentHost.Children.Add(filterRow);
        selector = UI.Button("Toutes les catégories  ⌄", OpenCategories);
        selector.Padding = new Thickness(12, 7, 12, 7);
        selector.FontSize = 12;
        selector.Margin = new Thickness(0, 0, 9, 0);
        filterRow.Children.Add(selector);
        filterRow.Children.Add(filters);
        Grid.SetRow(itemsHost, 2);
        ContentHost.Children.Add(itemsHost);
        itemsHost.ClipToBounds = true;
        var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        footer.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        footer.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        Grid.SetRow(footer, 3);
        ContentHost.Children.Add(footer);
        var note = UI.Text("Glisser le titre · Étirer les bords", 11, UI.Label);
        note.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(note);
        var pages = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(pages, 1);
        footer.Children.Add(pages);
        previous = UI.IconButton("\uE76B", "Page précédente", () => Turn(-1));
        previous.Width = 30;
        previous.Height = 28;
        pages.Children.Add(previous);
        pageText = UI.Text("1 / 1", 11, UI.Dim);
        pageText.VerticalAlignment = VerticalAlignment.Center;
        pageText.Margin = new Thickness(11, 0, 11, 0);
        pages.Children.Add(pageText);
        next = UI.IconButton("\uE76C", "Page suivante", () => Turn(1));
        next.Width = 30;
        next.Height = 28;
        pages.Children.Add(next);
        itemsHost.SizeChanged += (_, _) => { if (!rendering) RenderItems(false, 1); };
        new PanelInteraction(this, owner, titleHit);
        PreviewMouseWheel += (_, e) => { if (e.Delta != 0) { Turn(e.Delta < 0 ? 1 : -1); e.Handled = true; } };
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent("PanneauxEntry") ? DragDropEffects.Move
            : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => HandleDrop(e, null);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) owner.Collapse(); if (e.Key == Key.PageDown) Turn(1);
            if (e.Key == Key.PageUp) Turn(-1); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) { owner.Icons.CancelScope(Model.Id); renderKey = ""; } };
    }
    bool MatchQuick(Entry entry)
    {
        string text = Store.Fold(entry.Name + " " + entry.Category);
        return quick switch
        {
            "IA" => entry.Category == "IA et assistants de code" || entry.Category == "Intelligence artificielle",
            "Dev" => text.Contains("developpement") || text.Contains("python") || text.Contains("compilation"),
            "Création" => text.Contains("3d") || text.Contains("conception") || text.Contains("graphisme") || text.Contains("video"),
            "Canada" => Store.Fold(entry.Target).Contains("canada"),
            "Médias" => entry.Section == "Médias",
            "Cheats" => text.Contains("cheat") || text.Contains("my ch"),
            "Favoris" => Store.State.Favorites.Contains(entry.Id),
            _ => true
        };
    }
    internal void Refresh(bool animate = true, int direction = 1)
    {
        if (rendering)
            return;
        rendering = true;
        try
        {
            heading.Text = Model.Title;
            emblem.BorderBrush = UI.Brush(Model.Accent);
            if (emblem.Child is TextBlock glyph)
                glyph.Foreground = UI.Brush(Model.Accent);
            var selection = Store.Entries.Where(e => !Store.State.Hidden.Contains(e.Id) && Store.BoardFor(e) == Model.Id);
            star.Background = Model.FavoritesOnly ? UI.Selected : UI.T(M3Role.OnSurface, 0x0F);
            star.Content = UI.Glyph(Model.FavoritesOnly ? "\uE735" : "\uE734");
            if (owner.FavoritesOnly || Model.FavoritesOnly)
                selection = selection.Where(e => Store.State.Favorites.Contains(e.Id));
            var all = selection.ToList();
            string filter = Model.Filter;
            if (!Model.FavoritesOnly && filter == "Sélection" && Model.Id == "apps")
            {
                string[] preferred = { "google chrome", "firefox", "brave", "microsoft edge", "chatgpt", "opencode", "antigravity",
                    "hermes", "freebuff", "cursor", "visual studio code", "blender 5.2", "solidworks 2025", "obs studio", "recordly",
                    "discord", "telegram", "spotify", "docker desktop", "word", "excel", "proton mail", "7-zip file manager", "winrar" };
                selection = selection.Where(e => preferred.Contains(Store.Fold(e.Name)) || Store.State.Favorites.Contains(e.Id));
            }
            else if (!Model.FavoritesOnly && filter != "Tous" && filter != "Sélection")
                selection = selection.Where(e => e.Category == filter);
            if (!Model.FavoritesOnly)
                selection = selection.Where(MatchQuick);
            filtered = Store.Sorted(selection).ToList();
            caption.Text = Model.Id switch
            {
                "apps" => all.Count + " logiciels · tes outils au même endroit",
                "games" => all.Count + " accès · trouve ton prochain jeu",
                "projects" => all.Count + " espaces · reprends là où tu en étais",
                _ => all.Count + " dossiers · le quotidien à portée de main"
            };
            selector.Content = Model.FavoritesOnly ? "Favoris · " + Model.Title + "  ⌄" : (filter == "Tous" ? "Toutes les catégories"
                : filter) + "  ⌄";
            BuildQuick();
            RenderItems(animate, direction);
        }
        finally { rendering = false; }
    }
    void BuildQuick()
    {
        if (quickKey == quick)
            return;
        quickKey = quick;
        filters.Children.Clear();
        string[] options = Model.Id switch
        {
            "apps" => new[] { "IA", "Dev", "Création" },
            "projects" => new[] { "Cheats", "IA" },
            "documents" => new[] { "Canada", "Médias" },
            _ => Array.Empty<string>()
        };
        foreach (string text in options)
        {
            var b = UI.Button(text, () => { Model.FavoritesOnly = false; quick = quick == text ? "" : text; page = 0; Refresh(); });
            b.Padding = new Thickness(12, 7, 12, 7);
            b.FontSize = 12;
            b.Margin = new Thickness(0, 0, 7, 0);
            b.Background = quick == text ? UI.Selected : UI.T(M3Role.OnSurface, 0x09);
            filters.Children.Add(b);
        }
    }
    void OpenCategories()
    {
        var actions = new List<MenuAction> { new("Toutes les catégories", "\uE8A9", () => SetFilter("Tous"), Model.Filter == "Tous") };
        if (Model.Id == "apps")
            actions.Add(new("Sélection du quotidien", "\uE734", () => SetFilter("Sélection"), Model.Filter == "Sélection"));
        foreach (var cat in Store.Entries.Where(e => Store.BoardFor(e) == Model.Id && !Store.State.Hidden.Contains(e.Id)).Select(e
            => e.Category).Distinct().OrderBy(x => x))
        {
            string local = cat;
            actions.Add(new(cat, GlassMenu.CategoryGlyph(cat), () => SetFilter(local), Model.Filter == cat, actions.Count == 1));
        }
        GlassMenu.Open(owner, this, selector, "Catégories · " + Model.Title, actions);
    }
    void SetFilter(string filter)
    {
        Model.FavoritesOnly = false;
        Model.Filter = filter;
        quick = "";
        page = 0;
        Store.Save();
        Refresh();
    }
    void Turn(int by)
    {
        int pages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)capacity));
        int target = Math.Clamp(page + by, 0, pages - 1);
        if (target == page)
            return;
        page = target;
        RenderItems(true, by);
    }
    void RenderItems(bool animate, int direction)
    {
        double width = itemsHost.ActualWidth > 20 ? itemsHost.ActualWidth : Math.Max(400, Width - 56), height = itemsHost.ActualHeight > 20
            ? itemsHost.ActualHeight : Math.Max(180, Height - 190);
        double icon = Store.State.IconSize;
        string mode = Theme.Current.Tiles;
        if (mode == "list")
        {
            columns = 1;
            rows = Math.Max(1, (int)Math.Floor(height / 58));
        }
        else if (mode == "cards")
        {
            columns = Math.Clamp((int)Math.Floor(width / 250), 2, 5);
            rows = Math.Max(1, (int)Math.Floor(height / (icon + 104)));
        }
        else if (mode == "bento")
        {
            columns = 3;
            rows = Math.Max(1, (int)Math.Floor(height / 146));
        }
        else if (mode == "index")
        {
            columns = 1;
            rows = Math.Max(1, (int)Math.Floor(height / 50));
        }
        else if (mode == "stack")
        {
            columns = 1;
            rows = Math.Max(1, (int)Math.Floor(height / 76));
        }
        else
        {
            columns = Math.Max(2, (int)Math.Floor(width / (icon + 76)));
            rows = Math.Max(1, (int)Math.Floor(height / (icon + 61)));
        }
        capacity = columns * rows;
        int pages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)capacity));
        page = Math.Clamp(page, 0, pages - 1);
        string nextKey = Theme.Current.Id + "/" + filtered.Count + "/" + mode + "/" + columns + "/" + rows + "/" + page + "/" + icon + "/"
            + Model.Accent + "/" + string.Join("|", filtered.Skip(page * capacity).Take(capacity).Select(e => e.Id + e.Name + e.IconPath
            + Store.State.Favorites.Contains(e.Id)));
        if (renderKey == nextKey)
            return;
        renderKey = nextKey;
        var grid = new UniformGrid { Columns = columns, Rows = rows, VerticalAlignment = VerticalAlignment.Stretch };
        itemsHost.Children.Clear();
        itemsHost.Children.Add(grid);
        if (filtered.Count == 0)
        {
            var empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var g = UI.Glyph("\uE721", 29);
            g.Opacity = .6;
            empty.Children.Add(g);
            var label = UI.Text("Aucun accès dans cette vue", 15, UI.Dim);
            label.Margin = new Thickness(0, 16, 0, 0);
            empty.Children.Add(label);
            itemsHost.Children.Add(empty);
        }
        else
        {
            int ordinal = page * capacity;
            foreach (var entry in filtered.Skip(page * capacity).Take(capacity))
            {
                ordinal++;
                grid.Children.Add(CreateTile(entry, icon, ordinal));
            }
        }
        pageText.Text = (page + 1) + " / " + pages;
        previous.IsEnabled = page > 0;
        next.IsEnabled = page < pages - 1;
        if (animate)
            UI.Animate(grid, direction);
    }
}
