// ControlCenterWindow : cartes du centre de contrôle (connexions, actions, média, niveaux, raccourcis).

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

internal sealed partial class ControlCenterWindow
{
    FrameworkElement ConnectionsCard()
    {
        radios.Clear();
        var grid = new UniformGrid { Columns = 2, Rows = 2 };
        foreach (var item in new[]
        {
            ("wifi", "\uE701", "Wi-Fi"),
            ("bluetooth", "\uE702", "Bluetooth"),
            ("network", "\uE839", "Réseau"),
            ("settings", "\uE713", "Réglages")
        })
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(1, 1, 1, 3) };
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
                OpenSetting(item.Item1 == "wifi"
                ? "network-wifi"
                : item.Item1 == "bluetooth" ? "bluetooth" : item.Item1 == "network" ? "network-status" : "");
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
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(1, 1, 1, 3) };
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
        Tile("\uE722", "Capture", "Zone d’écran", () => SystemActions.Screenshot(owner));
        Tile("\uE74D", "Corbeille", "Vider · confirmation", () => SystemActions.EmptyBin(owner));
        var power = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(1, 1, 1, 3) };
        Button? powerButton = null;
        powerButton = Circle(UI.IconButton("\uE7E8",
                                           "Alimentation",
                                           () =>
                                               GlassMenu.Open(owner,
                                                              this,
                                                              powerButton!,
                                                              "Alimentation",
                                                              new[]
                                                              {
                                                                  new MenuAction("Redémarrer", "\uE72C", () => SystemActions.Power(owner,
                                                                      true)),
                                                                  new MenuAction("Éteindre", "\uE7E8", () => SystemActions.Power(owner,
                                                                      false)),
                                                                  new MenuAction("Mettre en veille", "\uE708", ()
                                                                      => SystemActions.Sleep(owner))
                                                              })));
        powerButton.Width = 43;
        powerButton.Height = 43;
        powerButton.Margin = new Thickness(0, 0, 0, 4);
        power.Children.Add(powerButton);
        var powerLabel = UI.Text("Alimentation", 11, null, true);
        powerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        power.Children.Add(powerLabel);
        var powerHint = UI.Text("Éteindre · veille", 9, UI.Dim);
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
        header.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        header.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        var artContent = new Grid();
        var placeholder = UI.Glyph("\uE8D6", 25);
        placeholder.Opacity = .7;
        artContent.Children.Add(placeholder);
        artContent.Children.Add(cover);
        var art =
            new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(13),
                Clip = new RectangleGeometry(new Rect(0, 0, 48, 48), 13, 13),
                Background = UI.Brush("#20FFFFFF"),
                Child = artContent,
                Margin = new Thickness(0, 0, 10, 0)
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
        var transport =
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 9, 0, 0)
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
        grid.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        grid.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(12)
        });
        int col = 2;
        var prefs = Store.State.Controls;
        foreach (var level in new[] { volume, brightness })
        {
            if (level == volume && !prefs.ShowVolume || level == brightness && !prefs.ShowBrightness)
                continue;
            grid.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(88)
            });
            grid.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(12)
            });
            var stack = new StackPanel();
            stack.Children.Add(level);
            var caption = UI.Text(level == volume ? "Volume" : "Luminosité", 11, UI.Dim);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.Margin = new Thickness(0, 8, 0, 0);
            if (level == brightness)
            {
                var link = UI.Button("Luminosité  ›", () => OpenSetting("display"));
                link.FontSize = 11;
                link.Foreground = UI.Dim;
                link.Background = Brushes.Transparent;
                link.BorderBrush = Brushes.Transparent;
                link.Padding = new Thickness(0);
                link.Margin = new Thickness(0, 8, 0, 0);
                link.ToolTip = "Réglages écran · ouvre les options Windows";
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
        var outputTitle = IconLabel("\uE7F5", "Sortie audio", 12);
        output.Children.Add(outputTitle);
        device.TextTrimming = TextTrimming.CharacterEllipsis;
        device.Margin = new Thickness(0, 9, 0, 0);
        output.Children.Add(device);
        var change = UI.Text("Choisir la sortie   ›", 10, UI.Dim);
        change.Margin = new Thickness(0, 5, 0, 0);
        output.Children.Add(change);
        var outputButton = UI.Button("", () => OpenSetting("sound"));
        outputButton.Content = output;
        outputButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        outputButton.Padding = new Thickness(13, 13, 13, 13);
        System.Windows.Automation.AutomationProperties.SetName(outputButton, "Choisir la sortie audio dans Windows");
        options.Children.Add(outputButton);
        mute.Margin = new Thickness(0, 10, 0, 10);
        options.Children.Add(mute);
        var screen = UI.Button("", () => OpenSetting("quiethours"));
        screen.Content = IconLabel("\uE708", "Concentration", 12);
        screen.ToolTip = "Ouvrir les paramètres de concentration Windows";
        screen.HorizontalContentAlignment = HorizontalAlignment.Left;
        screen.Padding = new Thickness(13, 11, 13, 11);
        options.Children.Add(screen);
        grid.Children.Add(options);
        return grid;
    }
    FrameworkElement Shortcuts()
    {
        var stack = new StackPanel();
        var title = UI.Text("Raccourcis Windows", 13, null, true);
        title.Margin = new Thickness(2, 0, 0, 12);
        stack.Children.Add(title);
        var grid = new UniformGrid { Columns = 4 };
        foreach (var shortcut in Store.State.Controls.Shortcuts)
        {
            var tile = new StackPanel { Margin = new Thickness(0, 0, 0, 11) };
            var button = Circle(UI.IconButton(shortcut.Glyph, shortcut.Name, () => owner.LaunchWindowsShortcut(shortcut)));
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
        var plus = Circle(UI.IconButton("\uE710", "Ajouter un raccourci Windows", () => owner.EditControls(this, true)));
        plus.Width = 48;
        plus.Height = 48;
        add.Children.Add(plus);
        var text = UI.Text("Ajouter", 10, UI.Dim);
        text.TextAlignment = TextAlignment.Center;
        text.Margin = new Thickness(0, 6, 0, 0);
        add.Children.Add(text);
        grid.Children.Add(add);
        stack.Children.Add(grid);
        return stack;
    }
}
