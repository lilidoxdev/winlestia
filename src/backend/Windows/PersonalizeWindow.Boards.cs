// PersonalizeWindow : liste des tableaux, export et import.

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

internal sealed partial class PersonalizeWindow
{
    void BoardRow(BoardState model, bool selected)
    {
        var outer =
            new Border
            {
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 10, 10),
                Background = UI.Brush(selected ? "#25FFFFFF" : "#0AFFFFFF"),
                BorderBrush = UI.T(M3Role.OnSurface, 0x24),
                BorderThickness = new Thickness(1)
            };
        var content = new StackPanel();
        var row = new Grid();
        row.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        row.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
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
        var up = UI.IconButton("\uE74A", "Déplacer ce panneau avant le précédent", () =>
        {
            int index = Store.State.Boards.IndexOf(model);
            if (index < 1)
                return;
            Store.State.Boards.RemoveAt(index);
            Store.State.Boards.Insert(index - 1, model);
            coordinator.Boards.Sort((a, b) => Store.State.Boards.IndexOf(a.Model).CompareTo(Store.State.Boards.IndexOf(b.Model)));
            Changed();
        });
        Grid.SetColumn(up, 1);
        up.Margin = new Thickness(10, 0, 0, 0);
        row.Children.Add(up);
        content.Children.Add(row);
        var colors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 13, 0, 0) };
        foreach (var color in new[] { "#91DFF5", "#ADA7FF", "#8BE6C5", "#F0D195", "#F4A8C5", "#EAF3FF" })
        {
            string local = color;
            var button = UI.Button("", () =>
            {
                model.Accent = local;
                Changed();
            });
            button.Width = 26;
            button.Height = 26;
            button.Padding = new Thickness(0);
            button.Background = UI.Brush(color);
            button.BorderBrush = UI.T(M3Role.OnSurface, 0x65);
            button.Margin = new Thickness(0, 0, 9, 0);
            button.ToolTip = color;
            colors.Children.Add(button);
        }
        var hex = new TextBox { Text = model.Accent, Width = 108, Margin = new Thickness(4, 0, 0, 0), FontSize = 11,
            Padding = new Thickness(8, 4, 8, 4) };
        hex.LostFocus += (_, _) =>
        {
            try
            {
                _ = UI.Color(hex.Text);
                model.Accent = hex.Text;
                Changed();
            }
            catch { hex.Text = model.Accent; }
        };
        colors.Children.Add(hex);
        content.Children.Add(colors);
        outer.Child = content;
        body.Children.Add(outer);
    }
    void Export()
    {
        var dialog = new SaveFileDialog { Title = "Exporter la personnalisation", Filter = "Personnalisation Panneaux|*.json",
            FileName = "Panneaux-dj.json" };
        if (dialog.ShowDialog(this) == true)
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(Store.State, Store.Json));
    }
    void Import()
    {
        var dialog = new OpenFileDialog { Title = "Importer une personnalisation Panneaux", Filter = "Personnalisation Panneaux|*.json" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var state = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(dialog.FileName), Store.Json);
            if (state == null)
                throw new InvalidDataException("Le fichier ne contient pas de personnalisation.");
            state.Normalize();
            File.Copy(Path.Combine(Store.Root, "preferences-glass.json"), Path.Combine(Store.Root, "preferences-avant-import.json"), true);
            var previousShortcut = Store.State.Shortcut;
            Store.State = state;
            if (!coordinator.SetShortcut(state.Shortcut.Modifiers, state.Shortcut.Key, state.Shortcut.View, out var shortcutError))
            {
                Store.State.Shortcut = previousShortcut;
                coordinator.Message(shortcutError);
            }
            Store.Save();
            Store.LoadCatalogue();
            foreach (var board in coordinator.Boards)
                board.Model = Store.State.Boards.First(b => b.Id == board.Model.Id);
            coordinator.Boards.Sort((a, b) => Store.State.Boards.IndexOf(a.Model).CompareTo(Store.State.Boards.IndexOf(b.Model)));
            coordinator.Arrange();
            Close();
        }
        catch (Exception ex) { coordinator.Message("Import impossible : " + ex.Message); }
    }
}
