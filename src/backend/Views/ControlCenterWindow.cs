// language: C# 12, file: ControlCenterWindow.cs, target: Windows 11 / .NET 8 WPF
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Devices.Radios;

namespace PanneauxGlass.Views;

internal sealed class ControlCenterWindow : GlassWindow
{
    readonly Coordinator owner;
    readonly SystemAudio audio = new();
    readonly WindowsConnections connections = new();
    readonly MediaController media = new();
    readonly DispatcherTimer mediaNotice = new() { Interval = TimeSpan.FromMilliseconds(250) };
    int mediaEvents,
        mediaReads,
        audioReads;
    long activeSince;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) },
        brightnessTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    readonly Grid body = new();
    readonly TextBlock subtitle,
        note;
    readonly GlassLevel volume = new(Strings.SystemVolume),
        brightness = new(Strings.DisplayBrightness) { Glyph = "\uE706" };
    readonly TextBlock mediaTitle = UI.Text(Strings.NothingPlaying, 14, null, true),
        artist = UI.Text(Strings.StartSpotifyToBegin, 11, UI.Dim),
        appName = UI.Text(Strings.MUSIC, 10, UI.Dim),
        elapsed = UI.Text("0:00", 10, UI.Dim),
        duration = UI.Text("0:00", 10, UI.Dim),
        device = UI.Text(Strings.WindowsOutput, 12),
        muteLabel = UI.Text(Strings.SoundOn, 12);
    readonly Image cover = new()
    {
        Width = 48,
        Height = 48,
        Stretch = Stretch.UniformToFill,
    };
    readonly Button play,
        previous,
        next,
        mute,
        sessionPicker;
    readonly Slider progress = new()
    {
        Minimum = 0,
        Maximum = 1,
        Height = 21,
        IsMoveToPointEnabled = true,
        SmallChange = 5,
        LargeChange = 15,
    };
    readonly Dictionary<string, (Button button, TextBlock status)> radios = new();
    AudioSnapshot audioState = new(false, 0, false, Strings.WindowsOutput);
    ConnectionSnapshot? connectionState;
    MediaSnapshot? mediaState;
    bool syncing,
        seeking,
        refreshingMedia,
        refreshingConnections,
        writingBrightness;
    int ticks,
        revision;
    double? pendingBrightness;
    string layoutKey = "";

    internal ControlCenterWindow(Coordinator coordinator)
        : base(Strings.PanneauxGlassControlCenter)
    {
        owner = coordinator;
        Radius = 38;
        ContentHost.CacheMode = null;
        ContentHost.Margin = new Thickness(24, 25, 24, 22);
        ContentHost.RowDefinitions.Add(new() { Height = GridLength.Auto });
        ContentHost.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        ContentHost.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(1, 0, 1, 20) };
        var close = UI.IconButton("\uE76B", Strings.CollapseWindowsControls, owner.ToggleControls);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var title = new StackPanel();
        title.Children.Add(UI.Text(Strings.ControlCenter, 23, null, true));
        subtitle = UI.Text(Strings.WindowsYourEssentialControls, 11, UI.Dim);
        subtitle.Margin = new Thickness(0, 6, 0, 0);
        title.Children.Add(subtitle);
        header.Children.Add(title);
        ContentHost.Children.Add(header);
        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        Grid.SetRow(scroll, 1);
        ContentHost.Children.Add(scroll);
        var footer = new DockPanel { Margin = new Thickness(0, 15, 0, 0) };
        var edit = UI.Button(Strings.Edit2, () => owner.EditControls(this));
        edit.Content = IconLabel("\uE70F", Strings.Edit, 12);
        edit.Padding = new Thickness(12, 8, 12, 8);
        System.Windows.Automation.AutomationProperties.SetName(edit, Strings.CustomizeWindowsControls);
        DockPanel.SetDock(edit, Dock.Right);
        footer.Children.Add(edit);
        note = UI.Text(Strings.YourWay, 11, UI.Dim);
        note.VerticalAlignment = VerticalAlignment.Center;
        note.TextTrimming = TextTrimming.CharacterEllipsis;
        note.Margin = new Thickness(2, 0, 10, 0);
        footer.Children.Add(note);
        Grid.SetRow(footer, 2);
        ContentHost.Children.Add(footer);
        previous = MediaButton("\uE892", Strings.PreviousTrack, () => _ = MediaCommand("previous"));
        play = MediaButton("\uE768", Strings.PlayOrPause, () => _ = MediaCommand("toggle"));
        next = MediaButton("\uE893", Strings.NextTrack, () => _ = MediaCommand("next"));
        play.Width = 42;
        play.Height = 42;
        play.Background = UI.Brush("#EAEEF8FF");
        if (play.Content is TextBlock pg)
            pg.Foreground = UI.Brush("#254D74");
        sessionPicker = UI.IconButton("\uE712", Strings.ChooseTheMusicApp, OpenSessions);
        sessionPicker.Width = 25;
        sessionPicker.Height = 25;
        sessionPicker.Background = Brushes.Transparent;
        sessionPicker.BorderBrush = Brushes.Transparent;
        mute = UI.Button(
            "",
            () =>
            {
                try
                {
                    audio.SetMute(!audioState.Muted);
                    RefreshAudio();
                }
                catch (Exception ex)
                {
                    Feedback(ex.Message);
                }
            }
        );
        mute.HorizontalContentAlignment = HorizontalAlignment.Left;
        mute.Padding = new Thickness(13, 11, 13, 11);
        mute.Content = IconLabel("\uE767", Strings.SoundOn, 12);
        System.Windows.Automation.AutomationProperties.SetName(mute, Strings.TurnSoundOnOrOff);
        volume.ValueChanged += (_, _) =>
        {
            if (syncing)
                return;
            try
            {
                audio.SetVolume(volume.Value);
                if (audioState.Muted && volume.Value > 0)
                    audio.SetMute(false);
            }
            catch (Exception ex)
            {
                Feedback(ex.Message);
            }
        };
        brightness.ValueChanged += (_, _) =>
        {
            if (syncing || !brightness.IsEnabled)
                return;
            pendingBrightness = brightness.Value;
            if (!brightnessTimer.IsEnabled && !writingBrightness)
                brightnessTimer.Start();
        };
        brightnessTimer.Tick += async (_, _) =>
        {
            brightnessTimer.Stop();
            await WriteBrightness();
        };
        foreach (var level in new[] { volume, brightness })
            level.DraggingChanged += dragging =>
            {
                if (dragging)
                    owner.BeginLayoutInteraction();
                else
                    owner.EndLayoutInteraction();
            };
        progress.PreviewMouseLeftButtonDown += (_, _) =>
        {
            seeking = true;
            owner.BeginLayoutInteraction();
        };
        progress.AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler((_, _) => EndSeek()), true);
        progress.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => EndSeek()), true);
        progress.KeyUp += (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageDown or Key.PageUp)
                _ = MediaCommand("seek", progress.Value);
        };
        timer.Tick += (_, _) =>
        {
            if (!IsVisible)
                return;
            if (Enabled("levels"))
                RefreshAudio();
            if (Enabled("media"))
                _ = RefreshMedia();
            if (++ticks % 5 == 0 && Enabled("connections"))
                _ = RefreshConnections();
        };
        media.Changed += () =>
        {
            if (IsVisible && Enabled("media"))
            {
                mediaEvents++;
                if (!mediaNotice.IsEnabled)
                    mediaNotice.Start();
            }
        };
        mediaNotice.Tick += (_, _) =>
        {
            mediaNotice.Stop();
            if (IsVisible)
                _ = RefreshMedia();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                Start();
            else
                Stop();
        };
        SizeChanged += (_, _) => Rebuild();
        Closed += (_, _) =>
        {
            Stop();
            media.Dispose();
            audio.Dispose();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                owner.Collapse();
        };
        Rebuild();
    }

    internal static string ModuleName(string id) =>
        id switch
        {
            "connections" => Strings.Connections,
            "media" => Strings.KnownFolder("Music"),
            "levels" => Strings.VolumeAndBrightness,
            "actions" => Strings.QuickActions,
            _ => Strings.WindowsShortcuts,
        };

    internal static StackPanel IconLabel(string glyph, string label, int size = 14)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = UI.Glyph(glyph, 19);
        icon.Margin = new Thickness(0, 0, 11, 0);
        stack.Children.Add(icon);
        var text = UI.Text(label, size);
        text.VerticalAlignment = VerticalAlignment.Center;
        stack.Children.Add(text);
        return stack;
    }

    Button MediaButton(string glyph, string label, Action action)
    {
        var button = Circle(UI.IconButton(glyph, label, action));
        button.Width = 32;
        button.Height = 32;
        button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        return button;
    }

    static ControlTemplate? roundTemplate;

    internal static Button Circle(Button button)
    {
        if (roundTemplate == null)
        {
            roundTemplate = (ControlTemplate)
                System.Windows.Markup.XamlReader.Parse(
                    "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"Button\">\r\n"
                        + "  <Grid><Border Background=\"{TemplateBinding Background}\" BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"{TemplateBinding BorderThickness}\" "
                        + "CornerRadius=\"100\"/><Border x:Name=\"shine\" Background=\"White\" Opacity=\"0\" CornerRadius=\"100\"/>"
                        + "<ContentPresenter Margin=\"{TemplateBinding Padding}\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\"/></Grid>\r\n"
                        + "  <ControlTemplate.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter TargetName=\"shine\" Property=\"Opacity\" Value=\"0.12\"/></Trigger>"
                        + "<Trigger Property=\"IsPressed\" Value=\"True\"><Setter TargetName=\"shine\" Property=\"Opacity\" Value=\"0.24\"/></Trigger>"
                        + "<Trigger Property=\"IsKeyboardFocused\" Value=\"True\"><Setter TargetName=\"shine\" Property=\"Opacity\" Value=\"0.16\"/></Trigger>"
                        + "<Trigger Property=\"IsEnabled\" Value=\"False\"><Setter Property=\"Opacity\" Value=\"0.3\"/></Trigger></ControlTemplate.Triggers>\r\n"
                        + "</ControlTemplate>"
                );
            roundTemplate.Seal();
        }
        button.Template = roundTemplate;
        return button;
    }

    Border Card(UIElement content, int radius = 28) =>
        new()
        {
            CornerRadius = new CornerRadius(radius),
            Background = new LinearGradientBrush(UI.Color("#25D6ECFF"), UI.Color("#10FFFFFF"), 70),
            BorderBrush = new LinearGradientBrush(UI.Color("#65F1FAFF"), UI.Color("#16FFFFFF"), 70),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(13),
            Child = content,
        };

    internal void Rebuild()
    {
        var prefs = Store.State.Controls;
        string key =
            ActualWidth
            + "|"
            + string.Join(",", prefs.Modules)
            + "|"
            + string.Join(",", prefs.HiddenModules)
            + prefs.ShowVolume
            + prefs.ShowBrightness
            + string.Join("|", prefs.Shortcuts.Select(s => s.Id + s.Name + s.Glyph + s.Target));
        if (layoutKey == key)
            return;
        layoutKey = key;
        body.Children.Clear();
        body.RowDefinitions.Clear();
        body.ColumnDefinitions.Clear();
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(14) });
        body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        // Persistent controls are reparented only when the module layout changes.
        foreach (
            var element in new FrameworkElement[]
            {
                volume,
                brightness,
                cover,
                play,
                previous,
                next,
                sessionPicker,
                progress,
                mediaTitle,
                artist,
                appName,
                elapsed,
                duration,
                device,
                mute,
            }
        )
            Detach(element);
        int row = 0;
        bool half = false;
        foreach (string module in prefs.Modules.Where(m => !prefs.HiddenModules.Contains(m)))
        {
            bool wide = module is "levels" or "shortcuts";
            if (wide && half)
            {
                row++;
                half = false;
            }
            while (body.RowDefinitions.Count <= row)
                body.RowDefinitions.Add(new() { Height = GridLength.Auto });
            FrameworkElement content = module switch
            {
                "connections" => ConnectionsCard(),
                "media" => MediaCard(),
                "levels" => Levels(),
                "actions" => ActionsCard(),
                _ => Shortcuts(),
            };
            content.Margin = new Thickness(0, 0, 0, 12);
            Grid.SetRow(content, row);
            Grid.SetColumn(content, half ? 2 : 0);
            if (wide)
            {
                Grid.SetColumnSpan(content, 3);
                row++;
            }
            else if (half)
            {
                row++;
                half = false;
            }
            else
                half = true;
            body.Children.Add(content);
        }
        if (body.Children.Count == 0)
        {
            var empty = UI.Text(Strings.AddControlsWithEdit, 14, UI.Dim);
            empty.Margin = new Thickness(5, 25, 5, 25);
            body.Children.Add(empty);
        }
        UpdateConnections();
        ApplyMedia();
        if (IsVisible)
            RefreshServices();
    }

    static void Detach(FrameworkElement element)
    {
        if (element.Parent is Panel panel)
            panel.Children.Remove(element);
        else if (element.Parent is Decorator decorator)
            decorator.Child = null;
        else if (element.Parent is ContentControl control)
            control.Content = null;
    }

    FrameworkElement ConnectionsCard()
    {
        radios.Clear();
        var grid = new UniformGrid { Columns = 2, Rows = 2 };
        foreach (
            var item in new[]
            {
                ("wifi", "\uE701", "Wi-Fi"),
                ("bluetooth", "\uE702", "Bluetooth"),
                ("network", "\uE839", Strings.Network),
                ("settings", "\uE713", Strings.Settings),
            }
        )
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(1, 1, 1, 3),
            };
            var button = Circle(UI.IconButton(item.Item2, item.Item3, () => ConnectionAction(item.Item1)));
            button.Width = 43;
            button.Height = 43;
            button.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(button);
            var label = UI.Text(item.Item3, 11, null, true);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(label);
            var status = UI.Text(item.Item1 == "settings" ? "Windows" : "…", 9, UI.Dim);
            status.HorizontalAlignment = HorizontalAlignment.Center;
            status.TextTrimming = TextTrimming.CharacterEllipsis;
            status.MaxWidth = 80;
            stack.Children.Add(status);
            radios[item.Item1] = (button, status);
            button.MouseRightButtonUp += (_, e) =>
            {
                OpenSetting(
                    item.Item1 == "wifi" ? "network-wifi"
                    : item.Item1 == "bluetooth" ? "bluetooth"
                    : item.Item1 == "network" ? "network-status"
                    : ""
                );
                e.Handled = true;
            };
            grid.Children.Add(stack);
        }
        var card = Card(grid);
        card.Height = 184;
        return card;
    }

    FrameworkElement ActionsCard()
    {
        var grid = new UniformGrid { Columns = 2, Rows = 2 };
        void Tile(string glyph, string label, string hint, Action run)
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(1, 1, 1, 3),
            };
            var button = Circle(UI.IconButton(glyph, label, run));
            button.Width = 43;
            button.Height = 43;
            button.Margin = new Thickness(0, 0, 0, 4);
            stack.Children.Add(button);
            var text = UI.Text(label, 11, null, true);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(text);
            var note = UI.Text(hint, 9, UI.Dim);
            note.HorizontalAlignment = HorizontalAlignment.Center;
            note.TextTrimming = TextTrimming.CharacterEllipsis;
            note.MaxWidth = 96;
            stack.Children.Add(note);
            grid.Children.Add(stack);
        }
        Tile("\uE72E", "Verrouiller", "Session Windows", () => SystemActions.Lock(owner));
        Tile("\uE722", "Capture", Strings.ScreenArea, () => SystemActions.Screenshot(owner));
        Tile("\uE74D", Strings.RecycleBin, Strings.EmptyConfirmation, () => SystemActions.EmptyBin(owner));
        var power = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(1, 1, 1, 3),
        };
        Button? powerButton = null;
        powerButton = Circle(
            UI.IconButton(
                "\uE7E8",
                "Alimentation",
                () =>
                    GlassMenu.Open(
                        owner,
                        this,
                        powerButton!,
                        "Alimentation",
                        new[]
                        {
                            new MenuAction(Strings.Restart, "\uE72C", () => SystemActions.Power(owner, true)),
                            new MenuAction(Strings.ShutDown, "\uE7E8", () => SystemActions.Power(owner, false)),
                            new MenuAction(Strings.Sleep, "\uE708", () => SystemActions.Sleep(owner)),
                        }
                    )
            )
        );
        powerButton.Width = 43;
        powerButton.Height = 43;
        powerButton.Margin = new Thickness(0, 0, 0, 4);
        power.Children.Add(powerButton);
        var powerLabel = UI.Text("Alimentation", 11, null, true);
        powerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        power.Children.Add(powerLabel);
        var powerHint = UI.Text(Strings.ShutDownSleep, 9, UI.Dim);
        powerHint.HorizontalAlignment = HorizontalAlignment.Center;
        power.Children.Add(powerHint);
        grid.Children.Add(power);
        var card = Card(grid);
        card.Height = 184;
        return card;
    }

    FrameworkElement MediaCard()
    {
        var stack = new StackPanel();
        var header = new Grid();
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var artContent = new Grid();
        var placeholder = UI.Glyph("\uE8D6", 25);
        placeholder.Opacity = .7;
        artContent.Children.Add(placeholder);
        artContent.Children.Add(cover);
        var art = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(13),
            Clip = new RectangleGeometry(new Rect(0, 0, 48, 48), 13, 13),
            Background = UI.Brush("#20FFFFFF"),
            Child = artContent,
            Margin = new Thickness(0, 0, 10, 0),
        };
        header.Children.Add(art);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        appName.FontSize = 9;
        appName.Margin = new Thickness(0, 0, 0, 4);
        texts.Children.Add(appName);
        foreach (var text in new[] { mediaTitle, artist })
        {
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            texts.Children.Add(text);
        }
        Grid.SetColumn(texts, 1);
        header.Children.Add(texts);
        stack.Children.Add(header);
        var transport = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 9, 0, 0),
        };
        previous.Margin = new Thickness(0, 0, 10, 0);
        next.Margin = new Thickness(10, 0, 0, 0);
        transport.Children.Add(previous);
        transport.Children.Add(play);
        transport.Children.Add(next);
        stack.Children.Add(transport);
        stack.Children.Add(progress);
        var times = new DockPanel();
        DockPanel.SetDock(duration, Dock.Right);
        times.Children.Add(duration);
        times.Children.Add(elapsed);
        stack.Children.Add(times);
        var layout = new Grid();
        layout.Children.Add(stack);
        sessionPicker.HorizontalAlignment = HorizontalAlignment.Right;
        sessionPicker.VerticalAlignment = VerticalAlignment.Top;
        sessionPicker.Margin = new Thickness(0, -7, -6, 0);
        layout.Children.Add(sessionPicker);
        appName.Margin = new Thickness(0, 0, 15, 4);
        var card = Card(layout);
        card.Height = 184;
        return card;
    }

    FrameworkElement Levels()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(12) });
        int col = 2;
        var prefs = Store.State.Controls;
        foreach (var level in new[] { volume, brightness })
        {
            if (level == volume && !prefs.ShowVolume || level == brightness && !prefs.ShowBrightness)
                continue;
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(88) });
            grid.ColumnDefinitions.Add(new() { Width = new GridLength(12) });
            var stack = new StackPanel();
            stack.Children.Add(level);
            var caption = UI.Text(level == volume ? "Volume" : Strings.Brightness, 11, UI.Dim);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.Margin = new Thickness(0, 8, 0, 0);
            if (level == brightness)
            {
                var link = UI.Button(Strings.Brightness2, () => OpenSetting("display"));
                link.FontSize = 11;
                link.Foreground = UI.Dim;
                link.Background = Brushes.Transparent;
                link.BorderBrush = Brushes.Transparent;
                link.Padding = new Thickness(0);
                link.Margin = new Thickness(0, 8, 0, 0);
                link.ToolTip = Strings.DisplaySettingsOpensTheWindowsOptions;
                stack.Children.Add(link);
            }
            else
                stack.Children.Add(caption);
            Grid.SetColumn(stack, col);
            grid.Children.Add(stack);
            col += 2;
        }
        var options = new StackPanel();
        var output = new StackPanel();
        var outputTitle = IconLabel("\uE7F5", Strings.AudioOutput, 12);
        output.Children.Add(outputTitle);
        device.TextTrimming = TextTrimming.CharacterEllipsis;
        device.Margin = new Thickness(0, 9, 0, 0);
        output.Children.Add(device);
        var change = UI.Text(Strings.ChooseTheOutput, 10, UI.Dim);
        change.Margin = new Thickness(0, 5, 0, 0);
        output.Children.Add(change);
        var outputButton = UI.Button("", () => OpenSetting("sound"));
        outputButton.Content = output;
        outputButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        outputButton.Padding = new Thickness(13, 13, 13, 13);
        System.Windows.Automation.AutomationProperties.SetName(outputButton, Strings.ChooseTheAudioOutputInWindows);
        options.Children.Add(outputButton);
        mute.Margin = new Thickness(0, 10, 0, 10);
        options.Children.Add(mute);
        var screen = UI.Button("", () => OpenSetting("quiethours"));
        screen.Content = IconLabel("\uE708", Strings.Focus, 12);
        screen.ToolTip = Strings.OpenTheWindowsFocusSettings;
        screen.HorizontalContentAlignment = HorizontalAlignment.Left;
        screen.Padding = new Thickness(13, 11, 13, 11);
        options.Children.Add(screen);
        grid.Children.Add(options);
        return grid;
    }

    FrameworkElement Shortcuts()
    {
        var stack = new StackPanel();
        var title = UI.Text(Strings.WindowsShortcuts, 13, null, true);
        title.Margin = new Thickness(2, 0, 0, 12);
        stack.Children.Add(title);
        var grid = new UniformGrid { Columns = 4 };
        foreach (var shortcut in Store.State.Controls.Shortcuts)
        {
            var tile = new StackPanel { Margin = new Thickness(0, 0, 0, 11) };
            var button = Circle(
                UI.IconButton(shortcut.Glyph, shortcut.Name, () => owner.LaunchWindowsShortcut(shortcut))
            );
            button.Width = 48;
            button.Height = 48;
            button.Background = new LinearGradientBrush(UI.Color("#30FFFFFF"), UI.Color("#13FFFFFF"), 80);
            tile.Children.Add(button);
            var label = UI.Text(shortcut.Name, 10, UI.Dim);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.TextAlignment = TextAlignment.Center;
            label.Margin = new Thickness(1, 6, 1, 0);
            label.ToolTip = shortcut.Name;
            tile.Children.Add(label);
            grid.Children.Add(tile);
        }
        var add = new StackPanel { Margin = new Thickness(0, 0, 0, 11) };
        var plus = Circle(UI.IconButton("\uE710", Strings.AddAWindowsShortcut, () => owner.EditControls(this, true)));
        plus.Width = 48;
        plus.Height = 48;
        add.Children.Add(plus);
        var text = UI.Text(Strings.Add, 10, UI.Dim);
        text.TextAlignment = TextAlignment.Center;
        text.Margin = new Thickness(0, 6, 0, 0);
        add.Children.Add(text);
        grid.Children.Add(add);
        stack.Children.Add(grid);
        return stack;
    }

    bool Enabled(string module) => !Store.State.Controls.HiddenModules.Contains(module);

    void RefreshServices()
    {
        if (Enabled("levels"))
            RefreshAudio();
        else
            audio.Dispose();
        if (Enabled("media"))
            _ = StartMedia();
        else
            media.Stop();
        if (Enabled("connections"))
            _ = RefreshConnections();
        if (Enabled("levels") && Store.State.Controls.ShowBrightness)
            _ = ReadBrightness();
    }

    void Start()
    {
        revision++;
        ticks = 0;
        mediaEvents = mediaReads = audioReads = 0;
        activeSince = Environment.TickCount64;
        RefreshServices();
        timer.Start();
    }

    void Stop()
    {
        if (activeSince != 0)
        {
            Store.Log(
                "Controls activity · ms="
                    + (Environment.TickCount64 - activeSince)
                    + " · audio="
                    + audioReads
                    + " · media="
                    + mediaReads
                    + " · notifications="
                    + mediaEvents
            );
            activeSince = 0;
        }
        revision++;
        mediaNotice.Stop();
        timer.Stop();
        brightnessTimer.Stop();
        if (volume.IsMouseCaptured)
            volume.ReleaseMouseCapture();
        if (brightness.IsMouseCaptured)
            brightness.ReleaseMouseCapture();
        if (seeking)
        {
            seeking = false;
            owner.EndLayoutInteraction();
        }
        if (pendingBrightness.HasValue)
            _ = WriteBrightness();
        media.Stop();
        audio.Dispose();
    }

    async Task StartMedia()
    {
        try
        {
            await media.StartAsync();
            await RefreshMedia();
        }
        catch (Exception ex)
        {
            mediaTitle.Text = Strings.PlaybackUnavailable;
            artist.Text = Strings.OpenTheWindowsAudioControls;
            Store.Log("SMTC: " + ex.Message);
        }
    }

    void RefreshAudio()
    {
        if (!IsVisible)
            return;
        audioReads++;
        var updated = audio.Read();
        if (updated == audioState)
            return;
        audioState = updated;
        syncing = true;
        try
        {
            volume.IsEnabled = audioState.Available;
            if (!volume.Dragging)
                volume.Value = audioState.Volume;
            volume.Glyph = audioState.Muted || audioState.Volume < 1 ? "\uE74F" : "\uE767";
            volume.InvalidateVisual();
            volume.ToolTip = audioState.Available ? Strings.SystemVolumeDragOrUseThe : Strings.NoAudioOutputAvailable;
            device.Text = audioState.Device;
            device.ToolTip = audioState.Device;
            mute.IsEnabled = audioState.Available;
            mute.Content = IconLabel(
                audioState.Muted ? "\uE74F" : "\uE767",
                audioState.Muted ? Strings.SoundMuted : Strings.SoundOn,
                12
            );
        }
        finally
        {
            syncing = false;
        }
    }

    async Task RefreshMedia()
    {
        if (!IsVisible || !Enabled("media") || refreshingMedia)
            return;
        refreshingMedia = true;
        mediaReads++;
        int expected = revision;
        try
        {
            var state = await media.ReadAsync();
            if (state != null && expected == revision && IsVisible)
            {
                var before = mediaState;
                mediaState = state;
                ApplyMedia(before);
            }
        }
        catch (Exception ex)
        {
            Feedback(Strings.PlaybackUnavailable);
            Store.Log("SMTC playback: " + ex.Message);
        }
        finally
        {
            refreshingMedia = false;
        }
    }

    void ApplyMedia(MediaSnapshot? before = null)
    {
        var s = mediaState;
        if (s == null)
            return;
        mediaTitle.Text = s.Title;
        mediaTitle.ToolTip = s.Title;
        artist.Text = s.Artist;
        artist.ToolTip = s.Artist;
        appName.Text = s.Available ? s.App.ToUpperInvariant() : Strings.MUSIC;
        cover.Source = s.Artwork;
        cover.Visibility = s.Artwork == null ? Visibility.Hidden : Visibility.Visible;
        play.IsEnabled = s.Available && s.CanToggle;
        previous.IsEnabled = s.Available && s.CanPrevious;
        next.IsEnabled = s.Available && s.CanNext;
        if (before?.Playing != s.Playing)
            play.Content = UI.Glyph(s.Playing ? "\uE769" : "\uE768", 20);
        if (play.Content is TextBlock glyph)
            glyph.Foreground = UI.Brush("#254D74");
        if (!seeking)
        {
            progress.Maximum = Math.Max(1, s.Duration);
            progress.Value = s.Position;
        }
        progress.IsEnabled = s.Available && s.CanSeek && s.Duration > 0;
        elapsed.Text = Time(s.Position);
        duration.Text = Time(s.Duration);
    }

    internal static string Time(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(seconds) ? seconds : 0));
        return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");
    }

    void EndSeek()
    {
        if (!seeking)
            return;
        seeking = false;
        owner.EndLayoutInteraction();
        _ = MediaCommand("seek", progress.Value);
    }

    async Task MediaCommand(string action, double position = 0)
    {
        try
        {
            if (!await media.CommandAsync(action, position))
                Feedback(Strings.ThisCommandIsNotAvailableFor);
            await RefreshMedia();
        }
        catch (Exception ex)
        {
            Feedback(ex.Message);
        }
    }

    void OpenSessions()
    {
        var sessions = media.Sessions();
        var actions = new List<MenuAction>
        {
            new(
                Strings.AutomaticSpotifyFirst,
                "\uE8D6",
                () =>
                {
                    Store.State.Controls.MediaApp = "";
                    Store.Save();
                    _ = RefreshMedia();
                },
                string.IsNullOrEmpty(Store.State.Controls.MediaApp)
            ),
        };
        foreach (var s in sessions)
            actions.Add(
                new(
                    s.name,
                    "\uE8D6",
                    () =>
                    {
                        Store.State.Controls.MediaApp = s.id;
                        Store.Save();
                        _ = RefreshMedia();
                    },
                    Store.State.Controls.MediaApp == s.id
                )
            );
        GlassMenu.Open(owner, this, sessionPicker, "Source musicale", actions);
    }

    async Task RefreshConnections()
    {
        if (!IsVisible || !Enabled("connections") || refreshingConnections)
            return;
        refreshingConnections = true;
        int expected = revision;
        try
        {
            var result = await connections.ReadAsync();
            if (IsVisible && expected == revision)
            {
                connectionState = result;
                UpdateConnections();
            }
        }
        catch (Exception ex)
        {
            Feedback(Strings.ConnectionsUnavailable);
            Store.Log("Radios: " + ex.Message);
        }
        finally
        {
            refreshingConnections = false;
        }
    }

    void UpdateConnections()
    {
        if (connectionState is not { } s)
            return;
        subtitle.Text = s.Internet ? s.Network + Strings.ConnectedToTheInternet : Strings.WindowsOffline;
        foreach (string id in new[] { "wifi", "bluetooth", "network" })
            if (radios.TryGetValue(id, out var tile))
            {
                bool on =
                    id == "wifi" ? s.Wifi.On
                    : id == "bluetooth" ? s.Bluetooth.On
                    : s.Internet;
                bool present =
                    id == "wifi" ? s.Wifi.Present
                    : id == "bluetooth" ? s.Bluetooth.Present
                    : true;
                tile.button.Background = UI.Brush(
                    on
                        ? id == "network"
                            ? "#D93BB283"
                            : "#DE247BF2"
                        : "#20FFFFFF"
                );
                tile.status.Text =
                    !present ? Strings.Absent
                    : id == "network"
                        ? s.Ethernet ? "Ethernet"
                            : s.Internet ? Strings.Connected
                            : Strings.Offline
                    : on ? Strings.On
                    : Strings.Off;
                tile.button.ToolTip =
                    id == "wifi"
                        ? s.Wifi.Present
                            ? Strings.ToggleWiFiRightClickFor
                            : Strings.WiFiMissingOpenSettings
                        : id == "bluetooth"
                            ? s.Bluetooth.Present
                                ? Strings.ToggleBluetoothRightClickForDevices
                                : Strings.BluetoothMissingOpenSettings
                            : Strings.OpenTheWindowsConnections;
            }
    }

    async void ConnectionAction(string id)
    {
        try
        {
            if (id == "wifi" || id == "bluetooth")
            {
                bool present =
                    id == "wifi" ? connectionState?.Wifi.Present == true : connectionState?.Bluetooth.Present == true;
                if (!present)
                {
                    OpenSetting(id == "wifi" ? "network-wifi" : "bluetooth");
                    return;
                }
                if (radios.TryGetValue(id, out var tile))
                    tile.button.IsEnabled = false;
                try
                {
                    await owner.RunControlTask(async () =>
                    {
                        await connections.ToggleAsync(id == "wifi" ? RadioKind.WiFi : RadioKind.Bluetooth);
                        await RefreshConnections();
                    });
                }
                finally
                {
                    if (radios.TryGetValue(id, out tile))
                        tile.button.IsEnabled = true;
                }
            }
            else
                OpenSetting(id == "network" ? "network-status" : "");
        }
        catch (Exception ex)
        {
            Feedback(ex.Message);
        }
    }

    Native.Point ScreenPoint()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Native.Point
        {
            X = (int)((Left + Width / 2) * dpi.DpiScaleX),
            Y = (int)((Top + Height / 2) * dpi.DpiScaleY),
        };
    }

    async Task ReadBrightness()
    {
        int expected = revision;
        brightness.IsEnabled = false;
        brightness.ToolTip = Strings.ReadingTheDisplaySetting;
        try
        {
            var state = await MonitorBrightness.ReadAsync(ScreenPoint());
            if (!IsVisible || expected != revision)
                return;
            syncing = true;
            try
            {
                brightness.IsEnabled = state.Available;
                brightness.Value = state.Value;
                brightness.ToolTip = state.Available
                    ? state.Device + Strings.DragToAdjust
                    : state.Error + Strings.ClickBrightnessToOpenTheDisplay;
            }
            finally
            {
                syncing = false;
            }
        }
        catch (Exception ex)
        {
            brightness.ToolTip = Strings.SettingUnavailableClickBrightnessForThe;
            Store.Log("Brightness: " + ex.Message);
        }
    }

    async Task WriteBrightness()
    {
        if (writingBrightness || !pendingBrightness.HasValue)
            return;
        writingBrightness = true;
        double value = pendingBrightness.Value;
        pendingBrightness = null;
        var point = ScreenPoint();
        try
        {
            await MonitorBrightness.SetAsync(point, value);
        }
        catch (Exception ex)
        {
            Feedback(ex.Message);
        }
        finally
        {
            writingBrightness = false;
            if (pendingBrightness.HasValue && !brightnessTimer.IsEnabled)
                brightnessTimer.Start();
        }
    }

    void Feedback(string message)
    {
        note.Text = message;
        note.ToolTip = message;
    }

    void OpenSetting(string page) =>
        owner.LaunchWindowsShortcut(new() { Name = Strings.WindowsSettings, Target = "ms-settings:" + page });
}
