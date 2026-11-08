// Étape 5 (suite) : champ de filtre de la liste, scindé de MainWindow.Watch.cs. Antirebond de 120 ms après une frappe.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PanneauxGlass;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    // ---- filter field -----------------------------------------------------------------------------------------------

    Border filterHost = null!; TextBox filterBox = null!; DispatcherTimer? filterTimer;
    bool settingFilter;

    FrameworkElement BuildFilter()
    {
        var icon = UI.Glyph("", 16);
        icon.Foreground = UI.Label;
        icon.Margin = new Thickness(M3.Sp2, 0, M3.Pad1, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        filterBox = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = UI.White,
            CaretBrush = UI.White,
            FontSize = M3.FontSmall,
            FontFamily = M3.Body,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Template = AddressTemplate(),
            ToolTip = "Rechercher (Ctrl+F ou Ctrl+E, Échap pour effacer)"
        };
        searchHint = UI.Text("Rechercher", M3.FontSmall, UI.Label);
        searchHint.IsHitTestVisible = false;
        searchHint.VerticalAlignment = VerticalAlignment.Center;
        searchHint.TextTrimming = TextTrimming.CharacterEllipsis;
        searchHint.Margin = new Thickness(2, 0, 0, 0);
        var field = new Grid();
        field.Children.Add(filterBox);
        field.Children.Add(searchHint);
        filterBox.TextChanged += (_, _) =>
        {
            UpdateFilterChrome();
            if (settingFilter)
                return;
            filterTimer ??= new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(FilterDebounceMs) };
            filterTimer.Tick -= ApplyFilter;
            filterTimer.Tick += ApplyFilter;
            filterTimer.Stop();
            filterTimer.Start();                        // une seule fois : ApplyFilter l'arrête
        };
        filterBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (!Active.CancelDeep())
                    ClearFilter(true);
                UpdateStatus();
                e.Handled = true;
            }
            else if (e.Key is Key.Down or Key.Enter)
            {
                list.Focus();
                if (list.SelectedItem == null && list.Items.Count > 0)
                    list.SelectedIndex = 0;
                e.Handled = true;
            }
        };
        filterBox.GotKeyboardFocus += (_, _) => filterHost.BorderBrush = UI.T(M3Role.Primary);
        filterBox.LostKeyboardFocus += (_, _) => filterHost.BorderBrush = UI.Hairline;
        filterClose = UI.Flat(UI.Glyph("", 10), 24, new CornerRadius(12));
        filterClose.Width = 24;
        filterClose.Margin = new Thickness(0, 0, M3.Pad1, 0);
        filterClose.ToolTip = "Effacer la recherche (Échap)";
        filterClose.Click += (_, _) => ClearFilter(true);
        deepToggle = UI.Flat("Sous-dossiers", 24, new CornerRadius(12));
        deepToggle.Margin = new Thickness(0, 0, M3.Pad1, 0);
        deepToggle.Padding = new Thickness(M3.Pad3, 0, M3.Pad3, 0);
        deepToggle.Focusable = false;
        deepToggle.FontSize = 11;
        deepToggle.ToolTip = "Rechercher dans les sous-dossiers";
        deepToggle.Click += (_, _) => { Active.SetDeepOption(!Active.DeepOption); PaintDeep(Active); };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(field, 1);
        Grid.SetColumn(deepToggle, 2);
        Grid.SetColumn(filterClose, 3);
        row.Children.Add(icon);
        row.Children.Add(field);
        row.Children.Add(deepToggle);
        row.Children.Add(filterClose);
        // Toujours visible, à droite de la zone d'adresse ; même fond surfaceContainer, bordure d'accent quand il a le focus.
        filterHost = new Border { Height = 36, Width = 250, CornerRadius = new CornerRadius(18),
            Background = UI.T(M3Role.SurfaceContainer),
            BorderBrush = UI.Hairline, BorderThickness = new Thickness(1), Child = row, SnapsToDevicePixels = true };
        UpdateFilterChrome();
        return filterHost;
    }

    Button filterClose = null!;
    // Texte indicatif tant qu'il est vide ; les boutons d'effacement et de sous-dossiers n'apparaissent qu'une fois qu'il y a quelque
    // chose à effacer.
    void UpdateFilterChrome()
    {
        if (filterBox == null || searchHint == null)
            return;
        bool has = filterBox.Text.Length > 0;
        searchHint.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        filterClose.Visibility = deepToggle.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
    }

    void ApplyFilter(object? sender, EventArgs e)
    {
        filterTimer?.Stop();
        if (closed || activeView == null)
            return;
        var t = Perf.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        Active.SetFilter(filterBox.Text);
        if (Perf.Enabled)
            Perf.Log($"filter '{filterBox.Text}': {Perf.Ms(t):F1} ms ({Active.Items.Count}/{Active.Total})");
    }

    Button deepToggle = null!;
    void PaintDeep(FolderTab tab)
    {
        if (deepToggle == null)
            return;
        bool on = tab.DeepOption;
        deepToggle.Foreground = on ? UI.T(M3Role.OnPrimaryContainer) : UI.White;
        deepToggle.Background = on ? UI.T(M3Role.PrimaryContainer) : Brushes.Transparent;
    }

    void ShowFilter(string? seed)
    {
        if (seed != null)
        {
            filterBox.Text = seed;
        }
        filterBox.Focus();
        filterBox.CaretIndex = filterBox.Text.Length;
    }

    void ClearFilter(bool focusList)
    {
        filterTimer?.Stop();
        settingFilter = true;
        filterBox.Text = "";
        settingFilter = false;
        UpdateFilterChrome();
        if (activeView != null)
            Active.SetFilter("");
        if (focusList)
            list.Focus();
    }

    // Appelé depuis Refresh : le champ reflète le filtre de l'onglet actif (effacé par la navigation, conservé par onglet au
    // changement).
    void SyncFilterUi(FolderTab tab)
    {
        if (filterBox == null)
            return;
        PaintDeep(tab);
        if (filterTimer is { IsEnabled: true })
            return;                     // l'utilisateur tape : l'anti-rebond l'appliquera
        if (filterBox.Text.Trim() == tab.Filter)
            return;
        settingFilter = true;
        filterBox.Text = tab.Filter;
        settingFilter = false;
        UpdateFilterChrome();
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        base.OnPreviewTextInput(e);
        if (e.Handled || activeView == null || e.Text.Length == 0 || char.IsControl(e.Text[0]) || char.IsWhiteSpace(e.Text[0]))
            return;
        if (!list.IsKeyboardFocusWithin || renameBox.IsKeyboardFocusWithin || addressBox.IsKeyboardFocusWithin)
            return;
        ShowFilter(e.Text);
        e.Handled = true;
    }

    FrameworkElement NoResult(FolderTab tab) =>
        UI.EmptyState("", "", "Aucun résultat", "Aucun élément ne correspond à « " + tab.Filter + " ».");
}
