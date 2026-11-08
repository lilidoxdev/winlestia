// C# 12, fichier ControlCenterWindow.cs, cible Windows 11 / .NET 8 WPF
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

namespace PanneauxGlass;

internal sealed partial class ControlCenterWindow : GlassWindow
{
    // Regroupe les rafales d'événements média (changement de piste) en une seule relecture.
    const int MediaNoticeMs = 250;
    readonly Coordinator owner;
    readonly SystemAudio audio = new();
    readonly WindowsConnections connections = new();
    readonly MediaController media = new();
    readonly DispatcherTimer mediaNotice = new() { Interval = TimeSpan.FromMilliseconds(MediaNoticeMs) };
    int mediaEvents, mediaReads, audioReads;
    long activeSince;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) }, brightnessTimer = new() {
        Interval = TimeSpan.FromMilliseconds(90) };
    readonly Grid body = new();
    readonly TextBlock subtitle, note;
    readonly GlassLevel volume = new("Volume système"), brightness = new("Luminosité de l’écran") { Glyph = "\uE706" };
    readonly TextBlock mediaTitle = UI.Text("Aucune lecture", 14, null, true),
        artist = UI.Text("Lance Spotify pour commencer", 11, UI.Dim),
        appName = UI.Text("MUSIQUE", 10, UI.Dim),
        elapsed = UI.Text("0:00", 10, UI.Dim),
        duration = UI.Text("0:00", 10, UI.Dim),
        device = UI.Text("Sortie Windows", 12),
        muteLabel = UI.Text("Son activé", 12);
    readonly Image cover = new() { Width = 48, Height = 48, Stretch = Stretch.UniformToFill };
    readonly Button play, previous, next, mute, sessionPicker;
    readonly Slider progress = new() { Minimum = 0, Maximum = 1, Height = 21, IsMoveToPointEnabled = true, SmallChange = 5,
        LargeChange = 15
        };
    readonly Dictionary<string, (Button button, TextBlock status)> radios = new();
    AudioSnapshot audioState = new(false, 0, false, "Sortie Windows");
    ConnectionSnapshot? connectionState;
    MediaSnapshot? mediaState;
    bool syncing, seeking, refreshingMedia, refreshingConnections, writingBrightness;
    int ticks, revision;
    double? pendingBrightness;
    string layoutKey = "";
    internal ControlCenterWindow(Coordinator coordinator) : base("Panneaux Glass — centre de contrôle")
    {
        owner = coordinator;
        Radius = 38;
        ContentHost.CacheMode = null;
        ContentHost.Margin = new Thickness(24, 25, 24, 22);
        ContentHost.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        ContentHost.RowDefinitions.Add(new()
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        ContentHost.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        var header = new DockPanel { Margin = new Thickness(1, 0, 1, 20) };
        var close = UI.IconButton("\uE76B", "Replier les commandes Windows", owner.ToggleControls);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var title = new StackPanel();
        title.Children.Add(UI.Text("Centre de contrôle", 23, null, true));
        subtitle = UI.Text("Windows · Tes commandes essentielles", 11, UI.Dim);
        subtitle.Margin = new Thickness(0, 6, 0, 0);
        title.Children.Add(subtitle);
        header.Children.Add(title);
        ContentHost.Children.Add(header);
        var scroll =
            new ScrollViewer
            {
                Content = body,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        Grid.SetRow(scroll, 1);
        ContentHost.Children.Add(scroll);
        var footer = new DockPanel { Margin = new Thickness(0, 15, 0, 0) };
        var edit = UI.Button("\uE70F  Modifier", () => owner.EditControls(this));
        edit.Content = IconLabel("\uE70F", "Modifier", 12);
        edit.Padding = new Thickness(12, 8, 12, 8);
        System.Windows.Automation.AutomationProperties.SetName(edit, "Personnaliser les commandes Windows");
        DockPanel.SetDock(edit, Dock.Right);
        footer.Children.Add(edit);
        note = UI.Text("À ta façon", 11, UI.Dim);
        note.VerticalAlignment = VerticalAlignment.Center;
        note.TextTrimming = TextTrimming.CharacterEllipsis;
        note.Margin = new Thickness(2, 0, 10, 0);
        footer.Children.Add(note);
        Grid.SetRow(footer, 2);
        ContentHost.Children.Add(footer);
        previous = MediaButton("\uE892", "Morceau précédent", () => _ = MediaCommand("previous"));
        play = MediaButton("\uE768", "Lecture ou pause", () => _ = MediaCommand("toggle"));
        next = MediaButton("\uE893", "Morceau suivant", () => _ = MediaCommand("next"));
        play.Width = 42;
        play.Height = 42;
        play.Background = UI.Brush("#EAEEF8FF");
        if (play.Content is TextBlock pg)
            pg.Foreground = UI.Brush("#254D74");
        sessionPicker = UI.IconButton("\uE712", "Choisir l’application musicale", OpenSessions);
        sessionPicker.Width = 25;
        sessionPicker.Height = 25;
        sessionPicker.Background = Brushes.Transparent;
        sessionPicker.BorderBrush = Brushes.Transparent;
        mute = UI.Button("", () =>
        {
            try
            {
                audio.SetMute(!audioState.Muted);
                RefreshAudio();
            }
            catch (Exception ex) { Feedback(ex.Message); }
        });
        mute.HorizontalContentAlignment = HorizontalAlignment.Left;
        mute.Padding = new Thickness(13, 11, 13, 11);
        mute.Content = IconLabel("\uE767", "Son activé", 12);
        System.Windows.Automation.AutomationProperties.SetName(mute, "Activer ou couper le son");
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
            catch (Exception ex) { Feedback(ex.Message); }
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
            level.DraggingChanged += dragging => { if (dragging) owner.BeginLayoutInteraction(); else owner.EndLayoutInteraction(); };
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
        IsVisibleChanged += (_, _) => { if (IsVisible) Start(); else Stop(); };
        SizeChanged += (_, _) => Rebuild();
        Closed += (_, _) =>
        {
            Stop();
            media.Dispose();
            audio.Dispose();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) owner.Collapse(); };
        Rebuild();
    }
    internal static string ModuleName(string id)
        => id switch
        {
            "connections" => "Connexions",
            "media" => "Musique",
            "levels" => "Volume et luminosité",
            "actions" => "Actions rapides",
            _ => "Raccourcis Windows"
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
            roundTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Grid><Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
            BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="100"/><Border x:Name="shine" Background="White" Opacity="0"
            CornerRadius="100"/><ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center"
            VerticalAlignment="Center"/></Grid>
          <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="shine" Property="Opacity"
            Value="0.12"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="shine" Property="Opacity" Value="0.24"/>
            </Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="shine" Property="Opacity" Value="0.16"/>
            </Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.3"/></Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
            roundTemplate.Seal();
        }
        button.Template = roundTemplate;
        return button;
    }
    Border Card(UIElement content, int radius = 28)
        => new()
        {
            CornerRadius = new CornerRadius(radius),
            Background = new LinearGradientBrush(UI.Color("#25D6ECFF"), UI.Color("#10FFFFFF"), 70),
            BorderBrush = new LinearGradientBrush(UI.Color("#65F1FAFF"), UI.Color("#16FFFFFF"), 70),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(13),
            Child = content
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
        body.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        body.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(14)
        });
        body.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        // Les contrôles persistants ne sont rattachés à un nouveau parent que lorsque la disposition des modules change.
        foreach (var element in new FrameworkElement[]
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
            mute
        })
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
            } while (body.RowDefinitions.Count <= row)
                body.RowDefinitions.Add(new()
                {
                    Height = GridLength.Auto
                });
            FrameworkElement content = module switch
            {
                "connections" => ConnectionsCard(),
                "media" => MediaCard(),
                "levels" => Levels(),
                "actions" => ActionsCard(),
                _ => Shortcuts()
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
            var empty = UI.Text("Ajoute des commandes avec Modifier.", 14, UI.Dim);
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
            Store.Log("Activité commandes · ms="
                + (Environment.TickCount64 - activeSince)
                + " · audio="
                + audioReads
                + " · médias="
                + mediaReads
                + " · notifications="
                + mediaEvents);
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
}
