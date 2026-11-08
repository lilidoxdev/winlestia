// MainWindow : construction de la mise en page (onglets, navigation, carte principale, chrome DWM).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using PanneauxGlass;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    // ---- layout ----------------------------------------------------------------------------------------------

    FrameworkElement TabRow()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        plus = UI.Flat(UI.Glyph("", 14), 32, new CornerRadius(16));
        plus.Width = 32;
        plus.ToolTip = "Nouvel onglet (Ctrl+T)";
        plus.Click += (_, _) => AddTab(Active.Location);
        tabStrip.Children.Add(plus);
        var scroll =
            new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalAlignment = HorizontalAlignment.Left,
                Content = tabStrip,
                VerticalAlignment = VerticalAlignment.Center
            };
        WindowChrome.SetIsHitTestVisibleInChrome(scroll, true);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(Caption("", () => WindowState = WindowState.Minimized));
        controls.Children.Add(Caption("", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal
            : WindowState.Maximized));
        controls.Children.Add(Caption("", Close));
        Grid.SetColumn(controls, 1);
        grid.Children.Add(scroll);
        grid.Children.Add(controls);
        return grid;
    }

    FrameworkElement NavRow()
    {
        var grid = new Grid { Margin = new Thickness(0, M3.Pad1, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sortButton = null!;                         // the sort command lives in the command bar (MainWindow.Cmd.cs)
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(BuildFilter());
        Grid.SetColumn(right, 2);
        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        back = NavButton("", "Précédent (Alt+Gauche)", () => Active.Back());
        forward = NavButton("", "Suivant (Alt+Droite)", () => Active.Forward());
        up = NavButton("", "Dossier parent (Alt+Haut)", () => Active.Up());
        nav.Children.Add(back);
        nav.Children.Add(forward);
        nav.Children.Add(up);

        crumbs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(M3.Pad1, 0, M3.Pad1, 0)
        };
        crumbScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = crumbs,
            VerticalAlignment = VerticalAlignment.Center
        };
        crumbs.SizeChanged += (_, _) => crumbScroll.ScrollToRightEnd();
        addressBox = new TextBox
        {
            Height = 36,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(M3.Sp3, 0, M3.Sp3, 0),
            Background = Brushes.Transparent,
            Foreground = UI.White,
            BorderThickness = new Thickness(0),
            FontSize = M3.FontSmall,
            FontFamily = M3.Body,
            CaretBrush = UI.White,
            Visibility = Visibility.Collapsed,
            Template = AddressTemplate()
        };
        addressBox.LostKeyboardFocus += (_, _) => EndEdit();
        addressBox.GotKeyboardFocus += (_, _) => addressPill.BorderBrush = UI.T(M3Role.Primary);
        addressBox.LostKeyboardFocus += (_, _) => addressPill.BorderBrush = UI.Hairline;
        addressBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                SubmitAddress(addressBox.Text);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                EndEdit();
                list.Focus();
                e.Handled = true;
            }
        };
        var pill =
            addressPill = new Border
            {
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Background = UI.T(M3Role.SurfaceContainer),
                BorderBrush = UI.Hairline,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(M3.Sp2, 0, M3.Sp2, 0),
                Cursor = Cursors.IBeam,
                ClipToBounds = true,
                SnapsToDevicePixels = true
            };
        var inner = new Grid();
        inner.Children.Add(crumbScroll);
        inner.Children.Add(addressBox);
        pill.Child = inner;
        pill.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                BeginEdit();
                e.Handled = true;
            }
        };   // double click on the free area right of the segments (buttons mark their own clicks handled)
        Grid.SetColumn(pill, 1);
        grid.Children.Add(nav);
        grid.Children.Add(pill);
        grid.Children.Add(right);
        return grid;
    }

    Button NavButton(string glyph, string tip, Action action)
    {
        var b = UI.Flat(UI.Glyph(glyph, 16), 36, new CornerRadius(18));
        b.ToolTip = tip;
        b.IsEnabled = false;
        b.Opacity = .38;
        if (b.Content is TextBlock g)
            g.Foreground = UI.Accent;
        b.Click += (_, _) => action();
        return b;
    }

    FrameworkElement MainCard()
    {
        list.Style = (Style)Resources["ListStyle"];
        list.ItemTemplate = (DataTemplate)Resources["RowTemplate"];
        list.ItemContainerStyle = (Style)Resources["RowStyle"];
        list.SelectionMode = SelectionMode.Extended;
        list.Background = Brushes.Transparent;
        list.BorderThickness = new Thickness(0);
        list.Foreground = UI.White;
        list.FontSize = M3.FontSmall;
        list.Padding = new Thickness(M3.Pad1, 0, 0, M3.Pad1);
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Item);
        ScrollViewer.SetCanContentScroll(list, true);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Visible);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.SelectionChanged += (_, _) => UpdateStatus();
        if (Perf.Enabled)
            list.ItemContainerGenerator.StatusChanged += (_,
                                                          _) =>
            {
                if (list.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated
                && !containersLogged)
                {
                    containersLogged = true;
                    Perf.Mark("first containers generated");
                }
            };
        list.MouseDoubleClick += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left
                && e.OriginalSource is DependencyObject source
                && ItemsControl.ContainerFromElement(list, source) is ListBoxItem { DataContext: FileItem item })
            {
                clickArm++;
                Open(item);
            }
        };
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter
                && !renameBox.IsKeyboardFocusWithin
                && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Control
                && list.SelectedItem != null)
            {
                OpenSelected(Keyboard.Modifiers == ModifierKeys.Control);
                e.Handled = true;
            }
        };

        listHost = new Grid();
        listHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        listHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = headerRow = Header();
        list.SetValue(Grid.RowProperty, 1);
        listHost.Children.Add(header);
        listHost.Children.Add(list);

        var card = new Grid();
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var cmdBar = CommandBar();
        var statusBar = StatusBar();
        Grid.SetRow(content, 1);
        Grid.SetRow(statusBar, 2);
        card.Children.Add(cmdBar);
        card.Children.Add(content);
        card.Children.Add(statusBar);
        SetView(ParseView(FilesSettings.InitialView()), false);
        toastText = UI.Text("", M3.FontSmall, UI.T(M3Role.OnSecondaryContainer));
        toast = new Border
        {
            Child = toastText,
            Background = UI.T(M3Role.SecondaryContainer),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(M3.Sp3, 6, M3.Sp3, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, M3.Sp2),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        Grid.SetRowSpan(toast, 3);
        card.Children.Add(toast);
        return new Border
        {
            Background = UI.Panel,
            CornerRadius = new CornerRadius(M3.Normal),
            Child = card,
            BorderBrush = UI.Hairline,
            BorderThickness = new Thickness(1),
            ClipToBounds = true
        };
    }

    static ControlTemplate AddressTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        host.SetValue(Control.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(host);
        return new ControlTemplate(typeof(TextBox)) { VisualTree = border };
    }

    static Button Caption(string glyph, Action action)
    {
        var b = UI.Flat(UI.Glyph(glyph, 12), 32, new CornerRadius(16));
        b.Width = 40;
        b.Click += (_, _) => action();
        WindowChrome.SetIsHitTestVisibleInChrome(b, true);
        return b;
    }

    // DWM : mode sombre immersif selon la palette (20), coins arrondis (33=2), fond Mica (38=2) sauf effets réduits.
    internal void RepaintChrome() => ApplyDwm();     // after a live palette swap (dark/light title bar attribute)

    void ApplyDwm()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget != null)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
        int dark = Theme.Current.IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
        int corner = 2;
        DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
        int backdrop = reduced ? 1 : 2;
        DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Thumbs.Dpi = newDpi.DpiScaleX;
    }
}
