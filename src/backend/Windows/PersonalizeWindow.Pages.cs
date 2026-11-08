// C# 12, fichier PersonalizeWindow.cs, cible Windows 11 / .NET 8 WPF, personnalisation visuelle et de contenu persistante
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

namespace PanneauxGlass;

internal sealed partial class PersonalizeWindow : GlassWindow
{
    // Une miniature de la vraie disposition de l'accueil, pour que deux thèmes ne se lisent jamais comme une simple recoloration l'un de
    // l'autre.
    static FrameworkElement Preview(ThemeDefinition theme)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 11, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom };
        var accent = new SolidColorBrush(Color.FromArgb(190, theme.Accent.R, theme.Accent.G, theme.Accent.B));
        var quiet = new SolidColorBrush(Color.FromArgb(70, theme.Text.R, theme.Text.G, theme.Text.B));
        int round = Math.Max(2, (int)(6 * theme.RadiusScale));
        if (theme.Tiles == "list")
        {
            var lines = new StackPanel();
            for (int i = 0; i < 3; i++)
                lines.Children.Add(new Border
                {
                    Width = i == 0 ? 168 : 136,
                    Height = 6,
                    CornerRadius = new CornerRadius(round),
                    Background = i == 0 ? accent : quiet,
                    Margin = new Thickness(0, 0, 0, 6)
                });
            row.Children.Add(lines);
        }
        else if (theme.Tiles == "bento")
        {
            for (int i = 0; i < 3; i++)
                row.Children.Add(new Border
                {
                    Width = 52,
                    Height = 40,
                    CornerRadius = new CornerRadius(round),
                    Background = i == 0 ? accent : quiet,
                    Margin = new Thickness(0, 0, 7, 0)
                });
        }
        else if (theme.Tiles == "index")
        {
            var lines = new StackPanel();
            for (int i = 0; i < 4; i++)
            {
                var line = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
                line.Children.Add(new Border { Width = 14, Height = 6, Background = accent, Margin = new Thickness(0, 0, 8, 0) });
                line.Children.Add(new Border { Width = i % 2 == 0 ? 96 : 72, Height = 6, Background = quiet });
                lines.Children.Add(line);
            }
            row.Children.Add(lines);
        }
        else if (theme.Tiles == "stack")
        {
            for (int i = 0; i < 3; i++)
                row.Children.Add(new Border
                {
                    Width = 46,
                    Height = 8,
                    CornerRadius = new CornerRadius(round),
                    Background = i == 0 ? accent : quiet,
                    Margin = new Thickness(0, 0, 6, 0)
                });
        }
        else if (theme.Tiles == "cards")
        {
            for (int i = 0; i < 3; i++)
                row.Children.Add(new Border
                {
                    Width = 52,
                    Height = 34,
                    CornerRadius = new CornerRadius(round),
                    Background = quiet,
                    BorderBrush = i == 0 ? accent : null,
                    BorderThickness = new Thickness(i == 0 ? 2 : 0),
                    Margin = new Thickness(0, 0, 7, 0)
                });
        }
        else
        {
            for (int i = 0; i < 6; i++)
                row.Children.Add(new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(round),
                    Background = i == 0 ? accent : quiet,
                    Margin = new Thickness(0, 0, 7, 0)
                });
        }
        return row;
    }
    // La couche du quotidien : la vivacité du mouvement, ce dont le presse-papiers se souvient et la façon d'épingler une fenêtre.
    void DailySection()
    {
        Section("LE QUOTIDIEN");
        Description("Ce qui sert tous les jours : un presse-papiers qui se souvient, une capture qui reste à l’écran, une fenêtre " +
            "épinglée au premier plan. Chaque geste se coupe ici s’il ne te sert pas.");
        var motion = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (level, name) in new[] { (0, "Calme"), (1, "Standard"), (2, "Expressif") })
        {
            int local = level;
            var chip = UI.Button(name, () =>
            {
                Store.State.MotionLevel = local;
                Changed();
                RefreshMotion(motion);
            });
            chip.Padding = new Thickness(14, 7, 14, 7);
            chip.Margin = new Thickness(0, 0, 8, 0);
            chip.Tag = name;
            System.Windows.Automation.AutomationProperties.SetName(chip, "Intensité du mouvement : " + name);
            motion.Children.Add(chip);
        }
        RefreshMotion(motion);
        body.Children.Add(motion);
        Check("Garder l’historique du presse-papiers", Store.State.ClipboardHistory, value =>
        {
            Store.State.ClipboardHistory = value;
            coordinator.ApplyClipboardSettings();
        });
        Check("Mémoriser aussi les images copiées", Store.State.ClipboardImages, value => Store.State.ClipboardImages = value);
        AddSlider("Éléments conservés",
                  8,
                  80,
                  Store.State.ClipboardLimit,
                  v => Store.State.ClipboardLimit = (int)Math.Round(v),
                  v => Math.Round(v) + " éléments");
        ShortcutEditor("ÉPINGLER AU PREMIER PLAN", Store.State.PinShortcut, (modifiers, key) =>
        {
            bool ok =
            coordinator.Keys.SetPinShortcut(modifiers, key, out var error);
            return (ok, error);
        }, string.IsNullOrEmpty(coordinator.Keys.PinStatus) ? "Actif : " + coordinator.Keys.PinShortcutLabel : coordinator.Keys.PinStatus);
        ShortcutEditor("CAPTURER L’ÉCRAN", Store.State.CaptureShortcut, (modifiers, key) =>
        {
            bool ok =
            coordinator.Keys.SetCaptureShortcut(modifiers, key, out var error);
            return (ok, error);
        }, string.IsNullOrEmpty(coordinator.Keys.CaptureStatus) ? "Actif : " + coordinator.Keys.CaptureShortcutLabel
            : coordinator.Keys.CaptureStatus);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
        var open = UI.Button("Ouvrir l’atelier", () => coordinator.OpenTools());
        tools.Children.Add(open);
        var help = UI.Button("Aide et raccourcis", () => coordinator.OpenHelp());
        help.Margin = new Thickness(10, 0, 0, 0);
        tools.Children.Add(help);
        body.Children.Add(tools);
    }
    // Un éditeur compact par geste global, pour que chaque liaison se change de la même façon.
    void ShortcutEditor(string label, ShortcutPreferences current, Func<uint, uint, (bool ok, string error)> apply, string initialStatus)
    {
        var caption = UI.Text(label, 11, UI.Dim, true);
        caption.Margin = new Thickness(0, 18, 0, 8);
        body.Children.Add(caption);
        uint key = current.Key;
        var status = UI.Text("", 12, UI.Dim);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 10, 0, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        CheckBox ctrl = new CheckBox { Content = "Ctrl", IsChecked = (current.Modifiers & 2) != 0, Margin = new Thickness(0, 0, 18, 0) },
            alt = new CheckBox { Content = "Alt", IsChecked = (current.Modifiers & 1) != 0, Margin = new Thickness(0, 0, 18, 0) },
            shift = new CheckBox { Content = "Maj", IsChecked = (current.Modifiers & 4) != 0, Margin = new Thickness(0, 0, 18, 0) };
        foreach (var check in new[] { ctrl, alt, shift })
            row.Children.Add(check);
        Button? keyButton = null;
        keyButton = UI.Button(GlobalShortcut.KeyName(key)
            + "  ⌄", () => GlassMenu.Open(coordinator, this, keyButton!, "Touche du raccourci", GlobalShortcut.Keys.Select(k
                => new MenuAction(GlobalShortcut.KeyName(k), "\uE765", () =>
            {
                key = k;
                keyButton!.Content = GlobalShortcut.KeyName(k) + "  ⌄";
            }))));
        keyButton.MinWidth = 84;
        System.Windows.Automation.AutomationProperties.SetName(keyButton, "Choisir la touche : " + label);
        row.Children.Add(keyButton);
        body.Children.Add(row);
        uint Modifiers() => (ctrl.IsChecked == true ? 2u : 0) | (alt.IsChecked == true ? 1u : 0) | (shift.IsChecked == true ? 4u : 0);
        var applyButton = UI.Button("Appliquer", () =>
        {
            var (ok, error) = apply(Modifiers(), key);
            status.Text = ok ? "Actif : " + GlobalShortcut.Label(Modifiers(), key) : error;
            status.Foreground = UI.Brush(ok ? "#A0E7CF" : "#F3CF91");
        });
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
            bool active = (string?)chip.Tag == (Store.State.MotionLevel switch
            {
                0 => "Calme",
                2 => "Expressif",
                _ => "Standard"
            });
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
        row.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(240)
        });
        row.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        row.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(106)
        });
        var label = UI.Text(title, 13);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        var slider =
            new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 15, 0),
                SmallChange = (max - min) / 50
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
        Section("OUVRIR DEPUIS N’IMPORTE QUELLE APPLICATION");
        Description("Un appui ouvre ton espace au premier plan. Un second le replie. Choisis aussi la vue à retrouver pour accéder au " +
            "son et à ta musique immédiatement.");
        var current = Store.State.Shortcut;
        uint key = current.Key;
        string view = current.View;
        var status = UI.Text("", 12, UI.Dim);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 10, 0, 0);
        var modifiers = new StackPanel { Orientation = Orientation.Horizontal };
        CheckBox ctrl = new CheckBox { Content = "Ctrl", IsChecked = (current.Modifiers & 2) != 0, Margin = new Thickness(0, 0, 20, 0) },
            alt = new CheckBox { Content = "Alt", IsChecked = (current.Modifiers & 1) != 0, Margin = new Thickness(0, 0, 20, 0) },
            shift = new CheckBox { Content = "Maj", IsChecked = (current.Modifiers & 4) != 0, Margin = new Thickness(0, 0, 20, 0) };
        foreach (var check in new[] { ctrl, alt, shift })
            modifiers.Children.Add(check);
        Button? keyButton = null;
        keyButton = UI.Button(GlobalShortcut.KeyName(key)
            + "  ⌄", () => GlassMenu.Open(coordinator, this, keyButton!, "Touche du raccourci", GlobalShortcut.Keys.Select(k
                => new MenuAction(GlobalShortcut.KeyName(k), "\uE765", () =>
            {
                key = k;
                keyButton!.Content = GlobalShortcut.KeyName(k) + "  ⌄";
                Preview();
            }, key == k))));
        keyButton.MinWidth = 94;
        System.Windows.Automation.AutomationProperties.SetName(keyButton, "Choisir la touche du raccourci global");
        modifiers.Children.Add(keyButton);
        body.Children.Add(modifiers);
        uint Modifiers() => (ctrl.IsChecked == true ? 2u : 0) | (alt.IsChecked == true ? 1u : 0) | (shift.IsChecked == true ? 4u : 0);
        void Preview()
        {
            status.Text = "À appliquer : " + GlobalShortcut.Label(Modifiers(), key);
            status.Foreground = UI.Dim;
        }
        foreach (var check in new[] { ctrl, alt, shift })
        {
            check.Checked += (_, _) => Preview();
            check.Unchecked += (_, _) => Preview();
        }
        var views =
            new[]
            {
                ("controls", "Accueil + commandes Windows"),
                ("home", "Accueil seul"),
                ("pc", "Accueil + état du PC"),
                ("workspace", "Les quatre panneaux"),
                ("last", "Dernière vue utilisée")
            };
        Button? viewButton = null;
        string ViewName() => views.First(v => v.Item1 == view).Item2;
        viewButton = UI.Button(ViewName()
            + "  ⌄", () => GlassMenu.Open(coordinator, this, viewButton!, "Vue à ouvrir", views.Select(v => new MenuAction(v.Item2,
                "\uE8A9", () =>
            {
                view = v.Item1;
                viewButton!.Content = ViewName() + "  ⌄";
                Preview();
            }, view == v.Item1))));
        viewButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        viewButton.Margin = new Thickness(0, 12, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(viewButton, "Choisir la vue ouverte par le raccourci");
        body.Children.Add(viewButton);
        var apply = UI.Button("Appliquer le raccourci", () =>
        {
            bool applied = coordinator.Keys.SetShortcut(Modifiers(), key, view, out var error);
            status.Text = applied ? "Actif : " + GlobalShortcut.Label(Modifiers(), key) + " · " + ViewName() : error;
            status.Foreground = UI.Brush(applied ? "#A0E7CF" : "#F3CF91");
        });
        apply.HorizontalAlignment = HorizontalAlignment.Left;
        apply.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(apply);
        body.Children.Add(status);
        status.Text = string.IsNullOrEmpty(coordinator.Keys.ShortcutStatus)
            ? "Actif : " + GlobalShortcut.Label(current.Modifiers, current.Key)
            : coordinator.Keys.ShortcutStatus;
        Description("Ctrl + Alt + Espace reste disponible. Le raccourci fonctionne même si la poignée est masquée en plein écran.");
    }
}
