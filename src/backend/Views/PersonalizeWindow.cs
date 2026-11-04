// language: C# 12, file: PersonalizeWindow.cs, target: Windows 11 / .NET 8 WPF, persistent visual and content customization
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PanneauxGlass.Views;

internal sealed class PersonalizeWindow : GlassWindow
{
    readonly Coordinator coordinator;
    readonly StackPanel body = new();
    readonly WrapPanel themeGrid = new();
    bool loaded;
    readonly DispatcherTimer applyTimer = new() { Interval = TimeSpan.FromMilliseconds(32) },
        saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    internal PersonalizeWindow(Coordinator owner, string? boardId)
        : base(Strings.PanneauxGlassCustomization)
    {
        coordinator = owner;
        Width = 800;
        Height = Math.Min(920, SystemParameters.WorkArea.Height - 70);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowActivated = true;
        ContentHost.Margin = new Thickness(30, 26, 30, 24);
        ContentHost.RowDefinitions.Add(new() { Height = GridLength.Auto });
        ContentHost.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        ContentHost.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var titleRow = new DockPanel();
        var done = UI.IconButton("\uE711", Strings.CloseCustomization, Close);
        DockPanel.SetDock(done, Dock.Right);
        titleRow.Children.Add(done);
        titleRow.Children.Add(UI.Text(Strings.YourWay2, 29, null, true));
        ContentHost.Children.Add(titleRow);
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 22, 0, 20),
            Content = body,
        };
        Grid.SetRow(scroll, 1);
        ContentHost.Children.Add(scroll);
        ThemeSection();
        DailySection();
        ShortcutSection();
        Section(Strings.THEGLASS);
        Description(Strings.TheMaterialReflectionsAndDensityOf);
        AddSlider(
            Strings.GlassTint,
            .05,
            .75,
            Store.State.Tint,
            v => Store.State.Tint = v,
            v => Math.Round(v * 100) + " %"
        );
        AddSlider(
            Strings.ReflectionIntensity,
            0,
            1,
            Store.State.Reflections,
            v => Store.State.Reflections = v,
            v => Math.Round(v * 100) + " %"
        );
        AddSlider(
            Strings.IconSize,
            40,
            80,
            Store.State.IconSize,
            v => Store.State.IconSize = v,
            v => Math.Round(v) + " px"
        );
        Check(Strings.ClearGlass, Store.State.LightGlass, value => Store.State.LightGlass = value);
        Section(Strings.THEFOURPANELS);
        Description(Strings.RenameColorAndReorderTheSurfaces);
        foreach (var model in Store.State.Boards.ToArray())
            BoardRow(model, model.Id == boardId);
        AddSlider(
            Strings.LeftColumnWidth,
            .3,
            .7,
            Store.State.LeftRatio,
            v => Store.State.LeftRatio = v,
            v => Math.Round(v * 100) + " %"
        );
        AddSlider(
            Strings.FirstRowHeight,
            .3,
            .7,
            Store.State.TopRatio,
            v => Store.State.TopRatio = v,
            v => Math.Round(v * 100) + " %"
        );
        AddSlider(Strings.PanelSpacing, 12, 48, Store.State.Gap, v => Store.State.Gap = v, v => Math.Round(v) + " px");
        AddSlider(
            Strings.DesktopMargin,
            20,
            100,
            Store.State.Margin,
            v => Store.State.Margin = v,
            v => Math.Round(v) + " px"
        );
        Section(Strings.THETASKBARANDTHEEDGE);
        Check(Strings.UseThePanneauxBarInsteadOf, coordinator.TaskbarEnabled, coordinator.SetTaskbar);
        Check(
            Strings.ShowTheHandleOnTheLeft,
            Store.State.RailSide != "right",
            value => Store.State.RailSide = value ? "left" : "right"
        );
        AddSlider(
            Strings.HandleWidth,
            40,
            90,
            Store.State.RailWidth,
            v => Store.State.RailWidth = v,
            v => Math.Round(v) + " px"
        );
        AddSlider(
            Strings.HandleHeight,
            .45,
            .94,
            Store.State.RailHeight,
            v => Store.State.RailHeight = v,
            v => Math.Round(v * 100) + Strings.OfTheScreen
        );
        AddSlider(
            Strings.AnimationDuration,
            0,
            1.5,
            Store.State.Motion,
            v => Store.State.Motion = v,
            v => v < .02 ? Strings.NoAnimation : Math.Round(v * 160) + " ms"
        );
        Check(Strings.CollapseAfter18SecondsOutside, Store.State.AutoHide, value => Store.State.AutoHide = value);
        Check(
            Strings.PauseTheEdgeDuringFullScreen,
            Store.State.PauseFullscreen,
            value => Store.State.PauseFullscreen = value
        );
        Check(Strings.LaunchPanneauxAtSignIn, Native.Autostart, value => Native.SetAutostart(value));
        Section(Strings.YOURSHORTCUTS);
        var choices = new WrapPanel();
        foreach (var board in Store.State.Boards)
        {
            string local = board.Id;
            var add = UI.Button("+ " + board.Title, () => coordinator.AddAccess(local));
            add.Margin = new Thickness(0, 0, 8, 8);
            choices.Children.Add(add);
        }
        body.Children.Add(choices);
        var folder = UI.Button(Strings.AddAFolder, () => coordinator.AddFolder(boardId ?? "documents"));
        folder.HorizontalAlignment = HorizontalAlignment.Left;
        folder.Margin = new Thickness(0, 3, 0, 12);
        body.Children.Add(folder);
        if (Store.State.Hidden.Count > 0)
        {
            var unhide = UI.Button(
                Strings.ShowThe + Store.State.Hidden.Count + Strings.HiddenShortcutsAgain,
                () =>
                {
                    Store.State.Hidden.Clear();
                    Changed();
                }
            );
            unhide.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(unhide);
        }
        var transfers = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var export = UI.Button(Strings.ExportMyCustomization, Export);
        export.Margin = new Thickness(0, 0, 8, 8);
        transfers.Children.Add(export);
        var import = UI.Button("Importer…", Import);
        transfers.Children.Add(import);
        body.Children.Add(transfers);
        Section(Strings.THEDESKTOPALWAYSREACHABLE);
        Description(Strings.TheNativeRescueToolShowsAnd);
        var rescue = UI.Button(Strings.RestoreTheOrganizedDesktop, coordinator.Rescue);
        rescue.HorizontalAlignment = HorizontalAlignment.Left;
        body.Children.Add(rescue);
        var show = UI.Button(
            Strings.ShowOnlyTheDesktopIcons,
            () =>
            {
                Native.Desktop(false);
            }
        );
        show.HorizontalAlignment = HorizontalAlignment.Left;
        show.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(show);
        var hide = UI.Button(
            Strings.HideTheDesktopIcons,
            () =>
            {
                Native.Desktop(true);
            }
        );
        hide.HorizontalAlignment = HorizontalAlignment.Left;
        hide.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(hide);
        var footer = new DockPanel();
        var close = UI.Button(Strings.Done, Close);
        close.MinWidth = 120;
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        var auto = UI.Text(Strings.SavedAutomaticallyFilesStayWhereThey, 12, UI.Dim);
        auto.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(auto);
        Grid.SetRow(footer, 2);
        ContentHost.Children.Add(footer);
        applyTimer.Tick += (_, _) =>
        {
            applyTimer.Stop();
            coordinator.Arrange(false);
            ApplyGlass();
        };
        saveTimer.Tick += (_, _) =>
        {
            saveTimer.Stop();
            Store.Save();
        };
        Loaded += (_, _) =>
        {
            loaded = true;
            ApplyGlass();
        };
        Closed += (_, _) =>
        {
            applyTimer.Stop();
            saveTimer.Stop();
            Store.Save();
            coordinator.Arrange(false);
            coordinator.RefreshBoards();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                Close();
        };
    }

    void ThemeSection()
    {
        Section(Strings.THEMES);
        Description(Strings.EachThemeChangesTheMaterialThe);
        body.Children.Add(themeGrid);
        ThemeCards();
    }

    void ThemeCards()
    {
        themeGrid.Children.Clear();
        foreach (var theme in Theme.Catalogue)
        {
            bool active = Theme.Current.Id == theme.Id;
            Brush background = theme.Opaque
                ? new SolidColorBrush(theme.Surface)
                : new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(theme.Surface, 0),
                        new GradientStop(Theme.Mix(theme.Surface, theme.HaloA, .55), .58),
                        new GradientStop(Theme.Mix(theme.Surface, theme.HaloB, .45), 1),
                    },
                    new Point(0, 0),
                    new Point(1, 1)
                );
            var card = new Border
            {
                Width = 238,
                MinHeight = 142,
                CornerRadius = new CornerRadius(Math.Max(4, (int)(22 * theme.RadiusScale))),
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(15, 12, 13, 12),
                BorderThickness = new Thickness(active ? 2 : 1),
                Cursor = Cursors.Hand,
                Background = background,
                BorderBrush = active
                    ? new SolidColorBrush(theme.Accent)
                    : new SolidColorBrush(
                        Color.FromArgb(
                            48,
                            (byte)(theme.Light ? 40 : 255),
                            (byte)(theme.Light ? 40 : 255),
                            (byte)(theme.Light ? 40 : 255)
                        )
                    ),
            };
            System.Windows.Automation.AutomationProperties.SetName(card, Strings.Theme + theme.Name);
            var text = new SolidColorBrush(theme.Text);
            var dim = new SolidColorBrush(theme.Dim);
            var stack = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            head.Children.Add(UI.Text(theme.Name, 14, text, true));
            var family = UI.Text(theme.Family.ToUpperInvariant(), 9, dim, true);
            family.VerticalAlignment = VerticalAlignment.Center;
            family.HorizontalAlignment = HorizontalAlignment.Right;
            family.Margin = new Thickness(0, 0, active ? 14 : 0, 0);
            Grid.SetColumn(family, 1);
            head.Children.Add(family);
            if (active)
            {
                var check = UI.Glyph("\uE73E", 12);
                check.Foreground = new SolidColorBrush(theme.Accent);
                check.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(check, 2);
                head.Children.Add(check);
            }
            stack.Children.Add(head);
            var detail = UI.Text(theme.Description, 11, dim);
            detail.TextWrapping = TextWrapping.Wrap;
            detail.Margin = new Thickness(0, 5, 0, 0);
            stack.Children.Add(detail);
            stack.Children.Add(Preview(theme));
            card.Child = stack;
            string id = theme.Id;
            card.MouseLeftButtonUp += (_, _) =>
            {
                coordinator.SetTheme(id);
                ThemeCards();
            };
            themeGrid.Children.Add(card);
        }
    }

    // A miniature of the actual home layout, so two themes never read as a recolour of one another.
    static FrameworkElement Preview(ThemeDefinition theme)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 11, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        var accent = new SolidColorBrush(Color.FromArgb(190, theme.Accent.R, theme.Accent.G, theme.Accent.B));
        var quiet = new SolidColorBrush(Color.FromArgb(70, theme.Text.R, theme.Text.G, theme.Text.B));
        int round = Math.Max(2, (int)(6 * theme.RadiusScale));
        if (theme.Tiles == "list")
        {
            var lines = new StackPanel();
            for (int i = 0; i < 3; i++)
                lines.Children.Add(
                    new Border
                    {
                        Width = i == 0 ? 168 : 136,
                        Height = 6,
                        CornerRadius = new CornerRadius(round),
                        Background = i == 0 ? accent : quiet,
                        Margin = new Thickness(0, 0, 0, 6),
                    }
                );
            row.Children.Add(lines);
        }
        else if (theme.Tiles == "bento")
        {
            for (int i = 0; i < 3; i++)
                row.Children.Add(
                    new Border
                    {
                        Width = 52,
                        Height = 40,
                        CornerRadius = new CornerRadius(round),
                        Background = i == 0 ? accent : quiet,
                        Margin = new Thickness(0, 0, 7, 0),
                    }
                );
        }
        else if (theme.Tiles == "index")
        {
            var lines = new StackPanel();
            for (int i = 0; i < 4; i++)
            {
                var line = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
                line.Children.Add(
                    new Border
                    {
                        Width = 14,
                        Height = 6,
                        Background = accent,
                        Margin = new Thickness(0, 0, 8, 0),
                    }
                );
                line.Children.Add(
                    new Border
                    {
                        Width = i % 2 == 0 ? 96 : 72,
                        Height = 6,
                        Background = quiet,
                    }
                );
                lines.Children.Add(line);
            }
            row.Children.Add(lines);
        }
        else if (theme.Tiles == "stack")
        {
            for (int i = 0; i < 3; i++)
                row.Children.Add(
                    new Border
                    {
                        Width = 46,
                        Height = 8,
                        CornerRadius = new CornerRadius(round),
                        Background = i == 0 ? accent : quiet,
                        Margin = new Thickness(0, 0, 6, 0),
                    }
                );
        }
        else if (theme.Tiles == "cards")
        {
            for (int i = 0; i < 3; i++)
                row.Children.Add(
                    new Border
                    {
                        Width = 52,
                        Height = 34,
                        CornerRadius = new CornerRadius(round),
                        Background = quiet,
                        BorderBrush = i == 0 ? accent : null,
                        BorderThickness = new Thickness(i == 0 ? 2 : 0),
                        Margin = new Thickness(0, 0, 7, 0),
                    }
                );
        }
        else
        {
            for (int i = 0; i < 6; i++)
                row.Children.Add(
                    new Border
                    {
                        Width = 16,
                        Height = 16,
                        CornerRadius = new CornerRadius(round),
                        Background = i == 0 ? accent : quiet,
                        Margin = new Thickness(0, 0, 7, 0),
                    }
                );
        }
        return row;
    }

    // The everyday layer: how lively the motion is, what the clipboard remembers, and how a window is pinned.
    void DailySection()
    {
        Section(Strings.EVERYDAY);
        Description(Strings.WhatYouUseEveryDayA);
        var motion = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (level, name) in new[] { (0, Strings.Calm), (1, "Standard"), (2, Strings.Expressive) })
        {
            int local = level;
            var chip = UI.Button(
                name,
                () =>
                {
                    Store.State.MotionLevel = local;
                    Changed();
                    RefreshMotion(motion);
                }
            );
            chip.Padding = new Thickness(14, 7, 14, 7);
            chip.Margin = new Thickness(0, 0, 8, 0);
            chip.Tag = name;
            System.Windows.Automation.AutomationProperties.SetName(chip, Strings.MotionIntensity + name);
            motion.Children.Add(chip);
        }
        RefreshMotion(motion);
        body.Children.Add(motion);
        Check(
            Strings.KeepTheClipboardHistory,
            Store.State.ClipboardHistory,
            value =>
            {
                Store.State.ClipboardHistory = value;
                coordinator.ApplyClipboardSettings();
            }
        );
        Check(
            Strings.AlsoRememberCopiedImages,
            Store.State.ClipboardImages,
            value => Store.State.ClipboardImages = value
        );
        AddSlider(
            Strings.ItemsKept,
            8,
            80,
            Store.State.ClipboardLimit,
            v => Store.State.ClipboardLimit = (int)Math.Round(v),
            v => Math.Round(v) + Strings.Items
        );
        ShortcutEditor(
            Strings.PINTOTHEFOREGROUND,
            Store.State.PinShortcut,
            (modifiers, key) =>
            {
                bool ok = coordinator.SetPinShortcut(modifiers, key, out var error);
                return (ok, error);
            },
            string.IsNullOrEmpty(coordinator.PinStatus)
                ? Strings.Active2 + coordinator.PinShortcutLabel
                : coordinator.PinStatus
        );
        ShortcutEditor(
            Strings.CAPTURETHESCREEN,
            Store.State.CaptureShortcut,
            (modifiers, key) =>
            {
                bool ok = coordinator.SetCaptureShortcut(modifiers, key, out var error);
                return (ok, error);
            },
            string.IsNullOrEmpty(coordinator.CaptureStatus)
                ? Strings.Active2 + coordinator.CaptureShortcutLabel
                : coordinator.CaptureStatus
        );
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
        var open = UI.Button(Strings.OpenTheWorkshop, () => coordinator.OpenTools());
        tools.Children.Add(open);
        var help = UI.Button(Strings.HelpAndShortcuts, () => coordinator.OpenHelp());
        help.Margin = new Thickness(10, 0, 0, 0);
        tools.Children.Add(help);
        body.Children.Add(tools);
    }

    // One compact editor per global gesture, so every binding is changed the same way.
    void ShortcutEditor(
        string label,
        ShortcutPreferences current,
        Func<uint, uint, (bool ok, string error)> apply,
        string initialStatus
    )
    {
        var caption = UI.Text(label, 11, UI.Dim, true);
        caption.Margin = new Thickness(0, 18, 0, 8);
        body.Children.Add(caption);
        uint key = current.Key;
        var status = UI.Text("", 12, UI.Dim);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 10, 0, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        CheckBox ctrl = new CheckBox
            {
                Content = "Ctrl",
                IsChecked = (current.Modifiers & 2) != 0,
                Margin = new Thickness(0, 0, 18, 0),
            },
            alt = new CheckBox
            {
                Content = "Alt",
                IsChecked = (current.Modifiers & 1) != 0,
                Margin = new Thickness(0, 0, 18, 0),
            },
            shift = new CheckBox
            {
                Content = Strings.Shift2,
                IsChecked = (current.Modifiers & 4) != 0,
                Margin = new Thickness(0, 0, 18, 0),
            };
        foreach (var check in new[] { ctrl, alt, shift })
            row.Children.Add(check);
        Button? keyButton = null;
        keyButton = UI.Button(
            GlobalShortcut.KeyName(key) + "  ⌄",
            () =>
                GlassMenu.Open(
                    coordinator,
                    this,
                    keyButton!,
                    Strings.ShortcutKey,
                    GlobalShortcut.Keys.Select(k => new MenuAction(
                        GlobalShortcut.KeyName(k),
                        "\uE765",
                        () =>
                        {
                            key = k;
                            keyButton!.Content = GlobalShortcut.KeyName(k) + "  ⌄";
                        }
                    ))
                )
        );
        keyButton.MinWidth = 84;
        System.Windows.Automation.AutomationProperties.SetName(keyButton, Strings.ChooseTheKey + label);
        row.Children.Add(keyButton);
        body.Children.Add(row);
        uint Modifiers() =>
            (ctrl.IsChecked == true ? 2u : 0) | (alt.IsChecked == true ? 1u : 0) | (shift.IsChecked == true ? 4u : 0);
        var applyButton = UI.Button(
            Strings.Apply,
            () =>
            {
                var (ok, error) = apply(Modifiers(), key);
                status.Text = ok ? Strings.Active2 + GlobalShortcut.Label(Modifiers(), key) : error;
                status.Foreground = UI.Brush(ok ? "#A0E7CF" : "#F3CF91");
            }
        );
        applyButton.HorizontalAlignment = HorizontalAlignment.Left;
        applyButton.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(applyButton);
        body.Children.Add(status);
        status.Text = initialStatus;
    }

    static void RefreshMotion(WrapPanel chips)
    {
        foreach (var chip in chips.Children.OfType<Button>())
        {
            bool active =
                (string?)chip.Tag
                == (
                    Store.State.MotionLevel switch
                    {
                        0 => Strings.Calm,
                        2 => Strings.Expressive,
                        _ => "Standard",
                    }
                );
            chip.Background = active ? UI.Selected : UI.T(M3Role.OnSurface, 0x09);
            chip.BorderBrush = active ? UI.Selected : UI.T(M3Role.OnSurface, 0x20);
        }
    }

    void Section(string title)
    {
        var label = UI.Text(title, 11, UI.Label, true);
        label.Margin = new Thickness(0, 24, 0, 12);
        body.Children.Add(label);
    }

    void Description(string value)
    {
        var text = UI.Text(value, 13, UI.Dim);
        text.TextWrapping = TextWrapping.Wrap;
        text.Margin = new Thickness(0, 0, 0, 18);
        text.LineHeight = 20;
        body.Children.Add(text);
    }

    void AddSlider(string title, double min, double max, double value, Action<double> set, Func<double, string> format)
    {
        var row = new Grid { Margin = new Thickness(0, 7, 12, 9) };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(240) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(106) });
        var label = UI.Text(title, 13);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 15, 0),
            SmallChange = (max - min) / 50,
        };
        System.Windows.Automation.AutomationProperties.SetName(slider, title);
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        var number = UI.Text(format(value), 12, UI.Dim);
        number.VerticalAlignment = VerticalAlignment.Center;
        number.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(number, 2);
        row.Children.Add(number);
        slider.ValueChanged += (_, e) =>
        {
            set(e.NewValue);
            number.Text = format(e.NewValue);
            Changed();
        };
        body.Children.Add(row);
    }

    void Check(string title, bool current, Action<bool> set)
    {
        var check = new CheckBox { Content = title, IsChecked = current };
        check.Checked += (_, _) =>
        {
            set(true);
            Changed();
        };
        check.Unchecked += (_, _) =>
        {
            set(false);
            Changed();
        };
        body.Children.Add(check);
    }

    void Changed()
    {
        if (!loaded)
            return;
        Store.State.Normalize();
        if (!applyTimer.IsEnabled)
            applyTimer.Start();
        saveTimer.Stop();
        saveTimer.Start();
    }

    void ShortcutSection()
    {
        Section(Strings.OPENFROMANYAPPLICATION);
        Description(Strings.OnePressOpensYourSpaceIn);
        var current = Store.State.Shortcut;
        uint key = current.Key;
        string view = current.View;
        var status = UI.Text("", 12, UI.Dim);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 10, 0, 0);
        var modifiers = new StackPanel { Orientation = Orientation.Horizontal };
        CheckBox ctrl = new CheckBox
            {
                Content = "Ctrl",
                IsChecked = (current.Modifiers & 2) != 0,
                Margin = new Thickness(0, 0, 20, 0),
            },
            alt = new CheckBox
            {
                Content = "Alt",
                IsChecked = (current.Modifiers & 1) != 0,
                Margin = new Thickness(0, 0, 20, 0),
            },
            shift = new CheckBox
            {
                Content = Strings.Shift2,
                IsChecked = (current.Modifiers & 4) != 0,
                Margin = new Thickness(0, 0, 20, 0),
            };
        foreach (var check in new[] { ctrl, alt, shift })
            modifiers.Children.Add(check);
        Button? keyButton = null;
        keyButton = UI.Button(
            GlobalShortcut.KeyName(key) + "  ⌄",
            () =>
                GlassMenu.Open(
                    coordinator,
                    this,
                    keyButton!,
                    Strings.ShortcutKey,
                    GlobalShortcut.Keys.Select(k => new MenuAction(
                        GlobalShortcut.KeyName(k),
                        "\uE765",
                        () =>
                        {
                            key = k;
                            keyButton!.Content = GlobalShortcut.KeyName(k) + "  ⌄";
                            Preview();
                        },
                        key == k
                    ))
                )
        );
        keyButton.MinWidth = 94;
        System.Windows.Automation.AutomationProperties.SetName(keyButton, Strings.ChooseTheGlobalShortcutKey);
        modifiers.Children.Add(keyButton);
        body.Children.Add(modifiers);
        uint Modifiers() =>
            (ctrl.IsChecked == true ? 2u : 0) | (alt.IsChecked == true ? 1u : 0) | (shift.IsChecked == true ? 4u : 0);
        void Preview()
        {
            status.Text = Strings.ToApply + GlobalShortcut.Label(Modifiers(), key);
            status.Foreground = UI.Dim;
        }
        foreach (var check in new[] { ctrl, alt, shift })
        {
            check.Checked += (_, _) => Preview();
            check.Unchecked += (_, _) => Preview();
        }
        var views = new[]
        {
            ("controls", Strings.HomeWindowsControls),
            ("home", Strings.HomeOnly),
            ("pc", Strings.HomePCStatus),
            ("workspace", Strings.TheFourPanels),
            ("last", Strings.LastUsedView),
        };
        Button? viewButton = null;
        string ViewName() => views.First(v => v.Item1 == view).Item2;
        viewButton = UI.Button(
            ViewName() + "  ⌄",
            () =>
                GlassMenu.Open(
                    coordinator,
                    this,
                    viewButton!,
                    Strings.ViewToOpen,
                    views.Select(v => new MenuAction(
                        v.Item2,
                        "\uE8A9",
                        () =>
                        {
                            view = v.Item1;
                            viewButton!.Content = ViewName() + "  ⌄";
                            Preview();
                        },
                        view == v.Item1
                    ))
                )
        );
        viewButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        viewButton.Margin = new Thickness(0, 12, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(viewButton, Strings.ChooseTheViewTheShortcutOpens);
        body.Children.Add(viewButton);
        var apply = UI.Button(
            Strings.ApplyTheShortcut,
            () =>
            {
                bool applied = coordinator.SetShortcut(Modifiers(), key, view, out var error);
                status.Text = applied
                    ? Strings.Active2 + GlobalShortcut.Label(Modifiers(), key) + " · " + ViewName()
                    : error;
                status.Foreground = UI.Brush(applied ? "#A0E7CF" : "#F3CF91");
            }
        );
        apply.HorizontalAlignment = HorizontalAlignment.Left;
        apply.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(apply);
        body.Children.Add(status);
        status.Text = string.IsNullOrEmpty(coordinator.ShortcutStatus)
            ? Strings.Active2 + GlobalShortcut.Label(current.Modifiers, current.Key)
            : coordinator.ShortcutStatus;
        Description(Strings.CtrlAltSpaceRemainsAvailableThe);
    }

    void BoardRow(BoardState model, bool selected)
    {
        var outer = new Border
        {
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 10, 10),
            Background = UI.Brush(selected ? "#25FFFFFF" : "#0AFFFFFF"),
            BorderBrush = UI.T(M3Role.OnSurface, 0x24),
            BorderThickness = new Thickness(1),
        };
        var content = new StackPanel();
        var row = new Grid();
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var name = new TextBox { Text = model.Title, FontSize = 14 };
        name.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(name.Text))
            {
                model.Title = name.Text;
                Changed();
            }
        };
        row.Children.Add(name);
        var up = UI.IconButton(
            "\uE74A",
            Strings.MoveThisPanelBeforeThePrevious,
            () =>
            {
                int index = Store.State.Boards.IndexOf(model);
                if (index < 1)
                    return;
                Store.State.Boards.RemoveAt(index);
                Store.State.Boards.Insert(index - 1, model);
                coordinator.Boards.Sort(
                    (a, b) => Store.State.Boards.IndexOf(a.Model).CompareTo(Store.State.Boards.IndexOf(b.Model))
                );
                Changed();
            }
        );
        Grid.SetColumn(up, 1);
        up.Margin = new Thickness(10, 0, 0, 0);
        row.Children.Add(up);
        content.Children.Add(row);
        var colors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 13, 0, 0) };
        foreach (var color in new[] { "#91DFF5", "#ADA7FF", "#8BE6C5", "#F0D195", "#F4A8C5", "#EAF3FF" })
        {
            string local = color;
            var button = UI.Button(
                "",
                () =>
                {
                    model.Accent = local;
                    Changed();
                }
            );
            button.Width = 26;
            button.Height = 26;
            button.Padding = new Thickness(0);
            button.Background = UI.Brush(color);
            button.BorderBrush = UI.T(M3Role.OnSurface, 0x65);
            button.Margin = new Thickness(0, 0, 9, 0);
            button.ToolTip = color;
            colors.Children.Add(button);
        }
        var hex = new TextBox
        {
            Text = model.Accent,
            Width = 108,
            Margin = new Thickness(4, 0, 0, 0),
            FontSize = 11,
            Padding = new Thickness(8, 4, 8, 4),
        };
        hex.LostFocus += (_, _) =>
        {
            try
            {
                _ = UI.Color(hex.Text);
                model.Accent = hex.Text;
                Changed();
            }
            catch
            {
                hex.Text = model.Accent;
            }
        };
        colors.Children.Add(hex);
        content.Children.Add(colors);
        outer.Child = content;
        body.Children.Add(outer);
    }

    void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = Strings.ExportTheCustomization,
            Filter = Strings.PanneauxCustomizationJson,
            FileName = "Panneaux-dj.json",
        };
        if (dialog.ShowDialog(this) == true)
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(Store.State, Store.Json));
    }

    void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.ImportAPanneauxCustomization,
            Filter = Strings.PanneauxCustomizationJson,
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var state = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(dialog.FileName), Store.Json);
            if (state == null)
                throw new InvalidDataException("The file does not contain a customization.");
            state.Normalize();
            File.Copy(
                Path.Combine(Store.Root, "preferences-glass.json"),
                Path.Combine(Store.Root, "preferences-avant-import.json"),
                true
            );
            var previousShortcut = Store.State.Shortcut;
            Store.State = state;
            if (
                !coordinator.SetShortcut(
                    state.Shortcut.Modifiers,
                    state.Shortcut.Key,
                    state.Shortcut.View,
                    out var shortcutError
                )
            )
            {
                Store.State.Shortcut = previousShortcut;
                coordinator.Message(shortcutError);
            }
            Store.Save();
            Store.LoadCatalogue();
            foreach (var board in coordinator.Boards)
                board.Model = Store.State.Boards.First(b => b.Id == board.Model.Id);
            coordinator.Boards.Sort(
                (a, b) => Store.State.Boards.IndexOf(a.Model).CompareTo(Store.State.Boards.IndexOf(b.Model))
            );
            coordinator.Arrange();
            Close();
        }
        catch (Exception ex)
        {
            coordinator.Message(Strings.ImportFailed + ex.Message);
        }
    }
}

internal sealed class EditWindow : GlassWindow
{
    internal EditWindow(Coordinator coordinator, Entry entry, bool custom)
        : base(Strings.PanneauxGlassEditAShortcut)
    {
        Width = 620;
        Height = 590;
        ShowActivated = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ContentHost.Margin = new Thickness(30, 25, 30, 25);
        var form = new StackPanel();
        ContentHost.Children.Add(form);
        form.Children.Add(UI.Text(Strings.ThisShortcutYourWay, 25, null, true));
        void Label(string text)
        {
            var label = UI.Text(text, 12, UI.Dim);
            label.Margin = new Thickness(0, 21, 0, 8);
            form.Children.Add(label);
        }
        Label(Strings.DISPLAYEDNAME);
        var name = new TextBox { Text = entry.Name };
        form.Children.Add(name);
        Label(Strings.CATEGORY);
        var category = new TextBox { Text = entry.Category };
        form.Children.Add(category);
        Label(Strings.PANEL);
        BoardState destination = Store.State.Boards.First(b => b.Id == Store.BoardFor(entry));
        Button? destinationButton = null;
        destinationButton = UI.Button(
            destination.Title + "  ⌄",
            () =>
                GlassMenu.Open(
                    coordinator,
                    this,
                    destinationButton!,
                    Strings.ChooseAPanel,
                    Store.State.Boards.Select(b => new MenuAction(
                        b.Title,
                        GlassMenu.CategoryGlyph(b.Title),
                        () =>
                        {
                            destination = b;
                            destinationButton!.Content = b.Title + "  ⌄";
                        },
                        destination == b
                    ))
                )
        );
        destinationButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        form.Children.Add(destinationButton);
        Label(Strings.ICON);
        string selectedIcon = entry.IconPath;
        var iconRow = new DockPanel();
        var choose = UI.Button(
            Strings.ChooseAnIcon,
            () =>
            {
                var picker = new OpenFileDialog
                {
                    Title = Strings.ChooseAnIcon2,
                    Filter = Strings.IconOrApplicationPngIcoExe,
                };
                if (picker.ShowDialog(this) == true)
                    selectedIcon = picker.FileName;
            }
        );
        iconRow.Children.Add(choose);
        var native = UI.Button(Strings.OriginalIcon, () => selectedIcon = entry.Target);
        native.Margin = new Thickness(10, 0, 0, 0);
        iconRow.Children.Add(native);
        form.Children.Add(iconRow);
        var path = UI.Text(entry.Target, 11, UI.Dim);
        path.TextWrapping = TextWrapping.Wrap;
        path.MaxHeight = 43;
        path.Margin = new Thickness(0, 20, 0, 20);
        form.Children.Add(path);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        actions.Children.Add(UI.Button(Strings.Cancel, Close));
        var save = UI.Button(
            Strings.Save,
            () =>
            {
                if (string.IsNullOrWhiteSpace(name.Text) || string.IsNullOrWhiteSpace(category.Text))
                    return;
                entry.Name = name.Text.Trim();
                entry.Category = category.Text.Trim();
                entry.IconPath = selectedIcon;
                Store.State.Names[entry.Id] = entry.Name;
                Store.State.Categories[entry.Id] = entry.Category;
                Store.State.Icons[entry.Id] = selectedIcon;
                Store.State.BoardOverrides[entry.Id] = destination.Id;
                if (custom)
                {
                    Store.State.Custom.Add(entry);
                    Store.Entries.Add(entry);
                }
                Store.Save();
                coordinator.RefreshBoards();
                Close();
            }
        );
        save.Margin = new Thickness(12, 0, 0, 0);
        actions.Children.Add(save);
        form.Children.Add(actions);
        Loaded += (_, _) => ApplyGlass();
    }
}
