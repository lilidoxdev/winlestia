// Éditeur d'une entrée de tableau, extrait de PersonalizeWindow.

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

internal sealed class EditWindow : GlassWindow
{
    internal EditWindow(Coordinator coordinator, Entry entry, bool custom) : base("Winlestia — modifier un accès")
    {
        Width = 620;
        Height = 590;
        ShowActivated = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ContentHost.Margin = new Thickness(30, 25, 30, 25);
        var form = new StackPanel();
        ContentHost.Children.Add(form);
        form.Children.Add(UI.Text("Cet accès, à ta façon.", 25, null, true));
        void Label(string text)
        {
            var label = UI.Text(text, 12, UI.Dim);
            label.Margin = new Thickness(0, 21, 0, 8);
            form.Children.Add(label);
        }
        Label("NOM AFFICHÉ");
        var name = new TextBox { Text = entry.Name };
        form.Children.Add(name);
        Label("CATÉGORIE");
        var category = new TextBox { Text = entry.Category };
        form.Children.Add(category);
        Label("PANNEAU");
        BoardState destination = Store.State.Boards.First(b => b.Id == Store.BoardFor(entry));
        Button? destinationButton = null;
        destinationButton = UI.Button(destination.Title
            + "  ⌄", () => GlassMenu.Open(coordinator, this, destinationButton!, "Choisir un panneau", Store.State.Boards.Select(b
                => new MenuAction(b.Title, GlassMenu.CategoryGlyph(b.Title), () =>
            {
                destination = b;
                destinationButton!.Content = b.Title + "  ⌄";
            }, destination == b))));
        destinationButton.HorizontalContentAlignment = HorizontalAlignment.Left;
        form.Children.Add(destinationButton);
        Label("ICÔNE");
        string selectedIcon = entry.IconPath;
        var iconRow = new DockPanel();
        var choose = UI.Button("Choisir une icône…", () =>
        {
            var picker =
            new OpenFileDialog { Title = "Choisir une icône",
                Filter = "Icône ou application|*.png;*.ico;*.exe;*.lnk|Tous les fichiers|*.*"
                };
            if (picker.ShowDialog(this) == true)
                selectedIcon = picker.FileName;
        });
        iconRow.Children.Add(choose);
        var native = UI.Button("Icône d’origine", () => selectedIcon = entry.Target);
        native.Margin = new Thickness(10, 0, 0, 0);
        iconRow.Children.Add(native);
        form.Children.Add(iconRow);
        var path = UI.Text(entry.Target, 11, UI.Dim);
        path.TextWrapping = TextWrapping.Wrap;
        path.MaxHeight = 43;
        path.Margin = new Thickness(0, 20, 0, 20);
        form.Children.Add(path);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(UI.Button("Annuler", Close));
        var save = UI.Button("Enregistrer", () =>
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
        });
        save.Margin = new Thickness(12, 0, 0, 0);
        actions.Children.Add(save);
        form.Children.Add(actions);
        Loaded += (_, _) => ApplyGlass();
    }
}
