// language: C# 12, file: ControlEditorWindow.cs, target: Windows 11 / .NET 8 WPF
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PanneauxGlass.Views;

internal sealed class ControlEditorWindow : GlassWindow
{
    readonly Coordinator owner;
    readonly StackPanel list = new();

    internal ControlEditorWindow(Coordinator coordinator, bool addImmediately)
        : base(Strings.PanneauxGlassCustomizeTheControls)
    {
        owner = coordinator;
        Width = 680;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Radius = 32;
        ContentHost.Margin = new Thickness(28);
        ContentHost.RowDefinitions.Add(new() { Height = GridLength.Auto });
        ContentHost.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        ContentHost.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        var close = UI.IconButton("\uE711", Strings.CloseCustomization, Close);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var title = new StackPanel();
        title.Children.Add(UI.Text(Strings.TheControlsYourWay, 24, null, true));
        var sub = UI.Text(Strings.ShowArrangeAndAddSavedAutomatically, 12, UI.Dim);
        sub.Margin = new Thickness(0, 7, 0, 0);
        title.Children.Add(sub);
        header.Children.Add(title);
        ContentHost.Children.Add(header);
        var scroll = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 8, 0),
        };
        Grid.SetRow(scroll, 1);
        ContentHost.Children.Add(scroll);
        var footer = new DockPanel { Margin = new Thickness(0, 20, 0, 0) };
        var add = UI.Button(Strings.AddAShortcut, () => Edit(null));
        add.Content = ControlCenterWindow.IconLabel("\uE710", Strings.AddAShortcut, 13);
        add.Padding = new Thickness(17, 12, 17, 12);
        footer.Children.Add(add);
        var done = UI.Button("Terminer", Close);
        done.Padding = new Thickness(20, 12, 20, 12);
        done.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(done);
        Grid.SetRow(footer, 2);
        ContentHost.Children.Add(footer);
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                Close();
        };
        Rebuild();
        Loaded += (_, _) =>
        {
            if (addImmediately)
                Dispatcher.BeginInvoke((Action)(() => Edit(null)));
        };
    }

    void Changed()
    {
        Store.State.Controls.Normalize();
        Store.Save();
        owner.ControlsChanged();
        Rebuild();
    }

    void Rebuild()
    {
        list.Children.Clear();
        list.Children.Add(UI.Text("MODULES", 11, UI.Dim, true));
        var prefs = Store.State.Controls;
        foreach (string id in prefs.Modules.ToArray())
        {
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var order = new StackPanel { Orientation = Orientation.Horizontal };
            var up = UI.IconButton(
                "\uE70E",
                "Monter " + ControlCenterWindow.ModuleName(id),
                () =>
                {
                    int index = prefs.Modules.IndexOf(id);
                    if (index > 0)
                    {
                        prefs.Modules.RemoveAt(index);
                        prefs.Modules.Insert(index - 1, id);
                        Changed();
                    }
                }
            );
            up.IsEnabled = prefs.Modules.IndexOf(id) > 0;
            var down = UI.IconButton(
                "\uE70D",
                "Descendre " + ControlCenterWindow.ModuleName(id),
                () =>
                {
                    int index = prefs.Modules.IndexOf(id);
                    if (index < prefs.Modules.Count - 1)
                    {
                        prefs.Modules.RemoveAt(index);
                        prefs.Modules.Insert(index + 1, id);
                        Changed();
                    }
                }
            );
            down.IsEnabled = prefs.Modules.IndexOf(id) < prefs.Modules.Count - 1;
            order.Children.Add(up);
            order.Children.Add(down);
            DockPanel.SetDock(order, Dock.Right);
            row.Children.Add(order);
            var check = new CheckBox
            {
                Content = ControlCenterWindow.ModuleName(id),
                IsChecked = !prefs.HiddenModules.Contains(id),
            };
            check.Click += (_, _) =>
            {
                if (check.IsChecked == true)
                    prefs.HiddenModules.Remove(id);
                else if (!prefs.HiddenModules.Contains(id))
                    prefs.HiddenModules.Add(id);
                Changed();
            };
            row.Children.Add(check);
            list.Children.Add(row);
        }
        var sliders = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 22) };
        foreach (bool isVolume in new[] { true, false })
        {
            var check = new CheckBox
            {
                Content = isVolume ? Strings.VolumeSlider : Strings.BrightnessSlider,
                IsChecked = isVolume ? prefs.ShowVolume : prefs.ShowBrightness,
                Margin = new Thickness(0, 0, 20, 0),
            };
            check.Click += (_, _) =>
            {
                if (isVolume)
                    prefs.ShowVolume = check.IsChecked == true;
                else
                    prefs.ShowBrightness = check.IsChecked == true;
                Changed();
            };
            sliders.Children.Add(check);
        }
        list.Children.Add(sliders);
        list.Children.Add(UI.Text(Strings.WINDOWSSHORTCUTS, 11, UI.Dim, true));
        foreach (var shortcut in prefs.Shortcuts.ToArray())
        {
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var up = UI.IconButton("\uE70E", "Monter " + shortcut.Name, () => Move(shortcut, -1));
            var down = UI.IconButton("\uE70D", "Descendre " + shortcut.Name, () => Move(shortcut, 1));
            up.IsEnabled = prefs.Shortcuts.IndexOf(shortcut) > 0;
            down.IsEnabled = prefs.Shortcuts.IndexOf(shortcut) < prefs.Shortcuts.Count - 1;
            actions.Children.Add(up);
            actions.Children.Add(down);
            actions.Children.Add(UI.IconButton("\uE70F", Strings.Edit3 + shortcut.Name, () => Edit(shortcut)));
            actions.Children.Add(
                UI.IconButton(
                    "\uE711",
                    Strings.RemoveTheShortcut + shortcut.Name,
                    () =>
                    {
                        prefs.Shortcuts.Remove(shortcut);
                        Changed();
                    }
                )
            );
            DockPanel.SetDock(actions, Dock.Right);
            row.Children.Add(actions);
            var name = UI.Button("", () => Edit(shortcut));
            name.Content = ControlCenterWindow.IconLabel(shortcut.Glyph, shortcut.Name, 13);
            name.HorizontalContentAlignment = HorizontalAlignment.Left;
            name.Padding = new Thickness(11, 10, 11, 10);
            name.ToolTip = shortcut.Target;
            row.Children.Add(name);
            list.Children.Add(row);
        }
    }

    void Move(WindowsShortcut shortcut, int by)
    {
        var items = Store.State.Controls.Shortcuts;
        int index = items.IndexOf(shortcut),
            target = index + by;
        if (index < 0 || target < 0 || target >= items.Count)
            return;
        items.RemoveAt(index);
        items.Insert(target, shortcut);
        Changed();
    }

    void Edit(WindowsShortcut? shortcut)
    {
        owner.RunControlDialog(
            this,
            () =>
            {
                var dialog = new WindowsShortcutEditor(owner, shortcut)
                {
                    Owner = this,
                    ShowInTaskbar = owner.PreviewMode,
                };
                if (dialog.ShowDialog() == true)
                    Changed();
            }
        );
    }
}

internal sealed class WindowsShortcutEditor : GlassWindow
{
    readonly Coordinator owner;
    readonly WindowsShortcut? original;
    readonly TextBox name = new(),
        target = new(),
        arguments = new();
    readonly TextBlock status;
    readonly Button iconButton;
    string glyph;

    internal WindowsShortcutEditor(Coordinator coordinator, WindowsShortcut? shortcut)
        : base(Strings.PanneauxGlassWindowsShortcut)
    {
        owner = coordinator;
        original = shortcut;
        glyph = shortcut?.Glyph ?? "\uE713";
        Width = 620;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Radius = 32;
        ContentHost.Margin = new Thickness(28);
        var stack = new StackPanel();
        ContentHost.Children.Add(stack);
        var header = new DockPanel();
        var close = UI.IconButton("\uE711", Strings.Close, Close);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(
            UI.Text(shortcut == null ? Strings.OneMoreShortcut : Strings.YourWindowsShortcut, 24, null, true)
        );
        stack.Children.Add(header);
        var presets = UI.Button(Strings.ChooseAWindowsCommand, () => OpenPresets());
        presets.HorizontalAlignment = HorizontalAlignment.Left;
        presets.Margin = new Thickness(0, 22, 0, 20);
        presets.Padding = new Thickness(16, 10, 16, 10);
        presetAnchor = presets;
        stack.Children.Add(presets);
        Label(stack, Strings.NAME);
        name.Text = shortcut?.Name ?? "";
        stack.Children.Add(name);
        System.Windows.Automation.AutomationProperties.SetName(name, Strings.WindowsShortcutName);
        Label(stack, Strings.WINDOWSSETTINGORAPP);
        target.Text = shortcut?.Target ?? "";
        System.Windows.Automation.AutomationProperties.SetName(target, Strings.WindowsShortcutTarget);
        var choose = UI.Button("Parcourir…", ChooseFile);
        choose.Margin = new Thickness(10, 0, 0, 0);
        choose.Padding = new Thickness(13, 9, 13, 9);
        var row = new DockPanel();
        DockPanel.SetDock(choose, Dock.Right);
        row.Children.Add(choose);
        row.Children.Add(target);
        stack.Children.Add(row);
        var hint = UI.Text(Strings.ExampleMsSettingsDisplayOrThe, 11, UI.Dim);
        hint.Margin = new Thickness(0, 7, 0, 0);
        stack.Children.Add(hint);
        Label(stack, "ARGUMENTS — FACULTATIF");
        arguments.Text = shortcut?.Arguments ?? "";
        System.Windows.Automation.AutomationProperties.SetName(arguments, Strings.AppArguments);
        stack.Children.Add(arguments);
        iconButton = UI.Button("", OpenIcons);
        iconButton.Content = ControlCenterWindow.IconLabel(glyph, Strings.ChooseTheIcon, 13);
        iconButton.HorizontalAlignment = HorizontalAlignment.Left;
        iconButton.Padding = new Thickness(16, 11, 16, 11);
        iconButton.Margin = new Thickness(0, 20, 0, 15);
        stack.Children.Add(iconButton);
        status = UI.Text("", 12, UI.Brush("#FFD6B2"));
        status.TextWrapping = TextWrapping.Wrap;
        status.MinHeight = 38;
        stack.Children.Add(status);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = UI.Button(Strings.Cancel, Close);
        cancel.Padding = new Thickness(19, 12, 19, 12);
        buttons.Children.Add(cancel);
        var save = UI.Button(shortcut == null ? Strings.Add : Strings.Save, Save);
        save.Margin = new Thickness(12, 0, 0, 0);
        save.Padding = new Thickness(19, 12, 19, 12);
        buttons.Children.Add(save);
        stack.Children.Add(buttons);
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                Close();
        };
    }

    readonly Button presetAnchor;

    static void Label(Panel panel, string text)
    {
        var label = UI.Text(text, 10, UI.Dim, true);
        label.Margin = new Thickness(0, 15, 0, 8);
        panel.Children.Add(label);
    }

    void OpenPresets() =>
        GlassMenu.Open(
            owner,
            this,
            presetAnchor,
            Strings.WindowsControls,
            ControlPreferences
                .Library()
                .Select(s => new MenuAction(
                    s.Name,
                    s.Glyph,
                    () =>
                    {
                        name.Text = s.Name;
                        target.Text = s.Target;
                        arguments.Text = s.Arguments;
                        glyph = s.Glyph;
                        iconButton.Content = ControlCenterWindow.IconLabel(glyph, Strings.ChooseTheIcon, 13);
                    }
                ))
                .ToArray()
        );

    void OpenIcons()
    {
        var icons = new[]
        {
            (Strings.Settings, "\uE713"),
            ("Volume", "\uE767"),
            ("Wi-Fi", "\uE701"),
            ("Bluetooth", "\uE702"),
            (Strings.Screen, "\uE7F4"),
            ("Capture", "\uE722"),
            (Strings.KnownFolder("Music"), "\uE8D6"),
            (Strings.Folder, "\uE8B7"),
            (Strings.Calculator, "\uE8EF"),
            ("Horloge", "\uE917"),
            ("Applications", "\uE71D"),
            (Strings.Network, "\uE839"),
            ("Alimentation", "\uE7E8"),
            (Strings.Customization, "\uE790"),
        };
        GlassMenu.Open(
            owner,
            this,
            iconButton,
            Strings.ShortcutIcon,
            icons
                .Select(i => new MenuAction(
                    i.Item1,
                    i.Item2,
                    () =>
                    {
                        glyph = i.Item2;
                        iconButton.Content = ControlCenterWindow.IconLabel(glyph, Strings.ChooseTheIcon, 13);
                    },
                    glyph == i.Item2
                ))
                .ToArray()
        );
    }

    void ChooseFile()
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.ChooseAnAppOrAShortcut,
            Filter = Strings.AppsAndShortcutsExeLnkAll,
        };
        if (picker.ShowDialog(this) == true)
        {
            target.Text = picker.FileName;
            if (string.IsNullOrWhiteSpace(name.Text))
                name.Text = Path.GetFileNameWithoutExtension(picker.FileName);
        }
    }

    internal static bool ValidTarget(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length < 2048
        && !value.Contains('\n')
        && !value.Contains('\r')
        && !value.Contains('"')
        && (
            !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("javascript" or "data" or "vbscript")
        );

    void Save()
    {
        string label = name.Text.Trim(),
            destination = target.Text.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            status.Text = Strings.AddANameForThisShortcut;
            return;
        }
        if (!ValidTarget(destination))
        {
            status.Text = Strings.ChooseAnApplicationOrAValid;
            return;
        }
        var shortcut = original ?? new WindowsShortcut();
        shortcut.Name = label;
        shortcut.Target = destination;
        shortcut.Arguments = arguments.Text.Trim();
        shortcut.Glyph = glyph;
        if (original == null)
            Store.State.Controls.Shortcuts.Add(shortcut);
        Store.Save();
        owner.ControlsChanged();
        DialogResult = true;
    }
}
