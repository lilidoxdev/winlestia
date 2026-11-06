// Views (List / Details / Medium icons / Large icons), resizable details columns, the visible view buttons.
// The view is saved under "view" in settings.json. Details = ListBox rows; the other three share TilePanel with a different cell size.
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PanneauxGlass;

namespace WinlestiaFiles;

internal enum ViewMode
{
    List, Details, Medium, Large
}

// Shared column widths of the details view (rows and header bind to it, so a drag resizes every realized row live).
// Name takes what is left; Emplacement is 0 wide except while a sub-folder search is showing.
public sealed class ColumnLayout : INotifyPropertyChanged
{
    public static readonly ColumnLayout Instance = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    double loc = 220, mod = 172, type = 110, size = 84;
    bool locShown;
    public GridLength NameW => new(1, GridUnitType.Star);
    public GridLength LocW => new(locShown ? loc : 0);
    public GridLength ModW => new(mod);
    public GridLength TypeW => new(type);
    public GridLength SizeW => new(size);
    internal bool LocShown => locShown;
    internal double Width(string column) => column switch { "loc" => loc, "mod" => mod, "type" => type, _ => size };

    internal void ShowLocation(bool on)
    {
        if (locShown == on)
            return;
        locShown = on;
        Raise(nameof(LocW));
    }

    // delta > 0 widens the column (the Name column gives the room back).
    internal void Resize(string column, double delta)
    {
        double Clamp(double v) => Math.Clamp(v, 56, 600);
        switch (column)
        {
            case "loc":
                loc = Clamp(loc + delta);
                Raise(nameof(LocW));
                break;
            case "mod":
                mod = Clamp(mod + delta);
                Raise(nameof(ModW));
                break;
            case "type":
                type = Clamp(type + delta);
                Raise(nameof(TypeW));
                break;
            case "size":
                size = Clamp(size + delta);
                Raise(nameof(SizeW));
                break;
        }
    }
    void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed partial class MainWindow
{
    ViewMode view = ViewMode.Details;
    readonly Button[] viewButtons = new Button[4];
    FrameworkElement? locHeader;
    bool gridMode => view != ViewMode.Details;
    internal ViewMode CurrentView => view;

    static readonly string[] ViewKeys = { "list", "details", "medium", "large" };
    static ViewMode ParseView(string? key)
    {
        int i = Array.IndexOf(ViewKeys, (key ?? "").ToLowerInvariant());
        return i < 0 ? ViewMode.Details : (ViewMode)i;
    }

    // Cell size of the tile-style views (Details: only the row height matters).
    (double W, double H) Cell => view switch { ViewMode.Large => (132, 156), ViewMode.Medium => (104, 120), ViewMode.List => (240, 32), _ => (1, Styles.RowHeight) };

    // Old two-state entry point (the large-icons scroll test).
    internal void SetView(bool grid) => SetView(grid ? ViewMode.Large : ViewMode.Details);

    internal void SetView(ViewMode mode, bool save = true)
    {
        if (view == mode)
            return;
        view = mode;
        bool details = mode == ViewMode.Details;
        headerRow.Visibility = details ? Visibility.Visible : Visibility.Collapsed;
        listHost.RowDefinitions[0].Height = details ? new GridLength(34) : new GridLength(0);
        if (details)
            list.ClearValue(ItemsControl.ItemsPanelProperty);
        else
        {
            var panel = new FrameworkElementFactory(typeof(TilePanel));
            panel.SetValue(TilePanel.CellWProperty, Cell.W);
            panel.SetValue(TilePanel.CellHProperty, Cell.H);
            list.ItemsPanel = new ItemsPanelTemplate(panel);
        }
        string template = mode switch
        {
            ViewMode.Large => "Tile",
            ViewMode.Medium => "Medium",
            ViewMode.List => "List",
            _ => "Row"
        };
        list.ItemTemplate = (DataTemplate)Resources[template + "Template"];
        list.ItemContainerStyle = (Style)Resources[mode switch
        {
            ViewMode.Large => "TileStyle",
            ViewMode.Medium => "MediumStyle",
            ViewMode.List => "ListItemStyle",
            _ => "RowStyle"
        }];
        PaintViewButtons();
        if (save)
            FilesSettings.SaveView(ViewKeys[(int)mode]);
    }

    // Four visible buttons in the status row (no focus stealing: the shortcuts keep working after a click).
    FrameworkElement ViewBar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, M3.Sp2, 0) };
        (ViewMode Mode, string Glyph, string Tip)[] defs = {
            (ViewMode.List, "☰", "Liste (Ctrl+Maj+5)"), (ViewMode.Details, "▤", "Détails (Ctrl+Maj+6)"),
            (ViewMode.Medium, "▦", "Icônes moyennes (Ctrl+Maj+3)"), (ViewMode.Large, "▣", "Grandes icônes (Ctrl+Maj+2)") };
        foreach (var d in defs)
        {
            var b = UI.Flat(d.Glyph, 26, new CornerRadius(13));
            b.Width = 30;
            b.ToolTip = d.Tip;
            b.Focusable = false;
            b.Padding = new Thickness(0);
            var mode = d.Mode;
            b.Click += (_, _) => SetView(mode);
            viewButtons[(int)mode] = b;
            bar.Children.Add(b);
        }
        PaintViewButtons();
        return bar;
    }

    void PaintViewButtons()
    {
        for (int i = 0; i < viewButtons.Length; i++)
        {
            if (viewButtons[i] is not { } b)
                continue;
            bool on = i == (int)view;
            b.Foreground = on ? UI.Accent : UI.White;
            b.Background = on ? UI.T(M3Role.PrimaryContainer, 90) : Brushes.Transparent;
        }
    }

    // ---- details header: resizable columns --------------------------------------------------------------------------

    static void Bind(ColumnDefinition column, string property) =>
        System.Windows.Data.BindingOperations.SetBinding(column, ColumnDefinition.WidthProperty, new System.Windows.Data.Binding(property) { Source = ColumnLayout.Instance });

    FrameworkElement Header()
    {
        var grid = new Grid { Margin = new Thickness(M3.Pad1, M3.Pad1, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Styles.IconWidth) });
        string[] props = { "NameW", "LocW", "ModW", "TypeW", "SizeW" };
        foreach (var p in props)
        {
            var c = new ColumnDefinition();
            Bind(c, p);
            grid.ColumnDefinitions.Add(c);
        }
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Styles.ScrollWidth) });   // matches the always-visible scrollbar lane

        FrameworkElement Cell(SortColumn? column, string text, int col, int span, bool right, Thickness pad)
        {
            var label = UI.Text(text, M3.FontSmall, UI.Label, true);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = pad };
            row.Children.Add(label);
            if (column is { } c)
            {
                var chevron = UI.Glyph("", 10);
                chevron.Foreground = UI.Label;
                chevron.Margin = new Thickness(6, 0, 0, 0);
                chevron.Visibility = Visibility.Collapsed;
                chevrons[(int)c] = chevron;
                row.Children.Add(chevron);
            }
            var cell = new Border { Child = row, Background = Brushes.Transparent, CornerRadius = new CornerRadius(8), Cursor = column == null ? Cursors.Arrow : Cursors.Hand, ClipToBounds = true };
            if (column is { } sortBy)
            {
                cell.MouseEnter += (_, _) => cell.Background = UI.Tint;
                cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
                cell.MouseLeftButtonUp += (_, _) => ApplySort(Active.Sort with { Column = sortBy, Ascending = Active.Sort.Column == sortBy ? !Active.Sort.Ascending : true });
            }
            Grid.SetColumn(cell, col);
            Grid.SetColumnSpan(cell, span);
            grid.Children.Add(cell);
            return cell;
        }
        void Grip(int col, string column)
        {
            var line = new Border { Width = 1, Margin = new Thickness(0, 6, 0, 6), Background = UI.Hairline, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
            var thumb = new Thumb { Width = 8, Background = Brushes.Transparent, Cursor = Cursors.SizeWE, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-4, 0, 0, 0), Opacity = 0 };
            thumb.DragDelta += (_, e) => ColumnLayout.Instance.Resize(column, -e.HorizontalChange);
            thumb.MouseDoubleClick += (_, _) => { };
            Grid.SetColumn(line, col);
            Grid.SetColumn(thumb, col);
            grid.Children.Add(line);
            grid.Children.Add(thumb);
            if (column == "loc")
                locGrip = new FrameworkElement[] { line, thumb };
        }
        Cell(SortColumn.Name, "Nom", 0, 2, false, new Thickness(M3.Sp2 + 4, 0, 0, 0));
        locHeader = Cell(null, "Emplacement", 2, 1, false, new Thickness(8, 0, 0, 0));
        Cell(SortColumn.Modified, "Date de modification", 3, 1, false, new Thickness(8, 0, 0, 0));
        Cell(SortColumn.Type, "Type", 4, 1, false, new Thickness(8, 0, 0, 0));
        Cell(SortColumn.Size, "Taille", 5, 1, true, new Thickness(0, 0, 12, 0));
        Grip(2, "loc");
        Grip(3, "mod");
        Grip(4, "type");
        Grip(5, "size");
        return grid;
    }
    FrameworkElement[] locGrip = Array.Empty<FrameworkElement>();

    // Emplacement column: only while the shown rows are sub-folder search results.
    void SyncLocationColumn(FolderTab tab)
    {
        bool on = tab.DeepActive;
        ColumnLayout.Instance.ShowLocation(on);
        var v = on ? Visibility.Visible : Visibility.Collapsed;
        if (locHeader != null && locHeader.Visibility != v)
            locHeader.Visibility = v;
        foreach (var g in locGrip)
            if (g.Visibility != v)
                g.Visibility = v;
    }
}
