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
    // Regroupe les réglages rapprochés (curseurs) avant d'enregistrer.
    const int SaveDebounceMs = 400;
    readonly Coordinator coordinator;
    readonly StackPanel body = new();
    readonly WrapPanel themeGrid = new();
    bool loaded;
    readonly DispatcherTimer applyTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(32)
    }, saveTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(SaveDebounceMs)
    };
    internal PersonalizeWindow(Coordinator owner, string? boardId) : base("Winlestia — personnalisation")
    {
        coordinator = owner;
        Width = 800;
        Height = Math.Min(920, SystemParameters.WorkArea.Height - 70);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowActivated = true;
        ContentHost.Margin = new Thickness(30, 26, 30, 24);
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
        var titleRow = new DockPanel();
        var done = UI.IconButton("\uE711", "Fermer la personnalisation", Close);
        DockPanel.SetDock(done, Dock.Right);
        titleRow.Children.Add(done);
        titleRow.Children.Add(UI.Text("À ta façon.", 29, null, true));
        ContentHost.Children.Add(titleRow);
        var scroll =
            new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 22, 0, 20),
                Content = body
            };
        Grid.SetRow(scroll, 1);
        ContentHost.Children.Add(scroll);
        ThemeSection();
        DailySection();
        ShortcutSection();
        Section("LE VERRE");
        Description("La matière, les reflets et la densité de ton espace.");
        AddSlider("Teinte du verre", .05, .75, Store.State.Tint, v => Store.State.Tint = v, v => Math.Round(v * 100) + " %");
        AddSlider("Intensité des reflets", 0, 1, Store.State.Reflections, v => Store.State.Reflections = v, v => Math.Round(v * 100)
            + " %");
        AddSlider("Taille des icônes", 40, 80, Store.State.IconSize, v => Store.State.IconSize = v, v => Math.Round(v) + " px");
        Check("Verre clair", Store.State.LightGlass, value => Store.State.LightGlass = value);
        Section("LES QUATRE PANNEAUX");
        Description("Renomme, colore et réordonne les surfaces. Glisse les accès d’un panneau à l’autre pour les ranger.");
        foreach (var model in Store.State.Boards.ToArray())
            BoardRow(model, model.Id == boardId);
        AddSlider("Largeur de la colonne gauche", .3, .7, Store.State.LeftRatio, v => Store.State.LeftRatio = v, v => Math.Round(v * 100)
            + " %");
        AddSlider("Hauteur de la première rangée", .3, .7, Store.State.TopRatio, v => Store.State.TopRatio = v, v => Math.Round(v * 100)
            + " %");
        AddSlider("Espacement des panneaux", 12, 48, Store.State.Gap, v => Store.State.Gap = v, v => Math.Round(v) + " px");
        AddSlider("Marge du Bureau", 20, 100, Store.State.Margin, v => Store.State.Margin = v, v => Math.Round(v) + " px");
        Section("LA BARRE DES TÂCHES ET LE BORD");
        Check("Utiliser la barre Winlestia à la place de la barre Windows", coordinator.TaskbarEnabled, coordinator.SetTaskbar);
        Check("Afficher la poignée sur le bord gauche", Store.State.RailSide != "right", value => Store.State.RailSide = value ? "left"
            : "right");
        AddSlider("Largeur de la poignée", 40, 90, Store.State.RailWidth, v => Store.State.RailWidth = v, v => Math.Round(v) + " px");
        AddSlider("Hauteur de la poignée", .45, .94, Store.State.RailHeight, v => Store.State.RailHeight = v, v => Math.Round(v * 100)
            + " % de l’écran");
        AddSlider("Durée des animations",
                  0,
                  1.5,
                  Store.State.Motion,
                  v => Store.State.Motion = v,
                  v => v < .02 ? "Sans animation" : Math.Round(v * 160) + " ms");
        Check("Replier après 1,8 seconde hors des panneaux", Store.State.AutoHide, value => Store.State.AutoHide = value);
        Check("Mettre le bord en pause pendant le plein écran", Store.State.PauseFullscreen, value =>
            Store.State.PauseFullscreen = value);
        Check("Lancer Winlestia à l’ouverture de session", Native.Autostart, value => Native.SetAutostart(value));
        Section("TES ACCÈS");
        var choices = new WrapPanel();
        foreach (var board in Store.State.Boards)
        {
            string local = board.Id;
            var add = UI.Button("+ " + board.Title, () => coordinator.AddAccess(local));
            add.Margin = new Thickness(0, 0, 8, 8);
            choices.Children.Add(add);
        }
        body.Children.Add(choices);
        var folder = UI.Button("Ajouter un dossier…", () => coordinator.AddFolder(boardId ?? "documents"));
        folder.HorizontalAlignment = HorizontalAlignment.Left;
        folder.Margin = new Thickness(0, 3, 0, 12);
        body.Children.Add(folder);
        if (Store.State.Hidden.Count > 0)
        {
            var unhide = UI.Button("Réafficher les " + Store.State.Hidden.Count + " accès masqués", () =>
            {
                Store.State.Hidden.Clear();
                Changed();
            });
            unhide.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(unhide);
        }
        var transfers = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var export = UI.Button("Exporter ma personnalisation", Export);
        export.Margin = new Thickness(0, 0, 8, 8);
        transfers.Children.Add(export);
        var import = UI.Button("Importer…", Import);
        transfers.Children.Add(import);
        body.Children.Add(transfers);
        Section("LE BUREAU, TOUJOURS ACCESSIBLE");
        Description("Le secours natif réaffiche et organise tes icônes, arrête Winlestia et désactive son démarrage automatique. Il " +
            "fonctionne même lorsque l’interface ne répond plus.");
        var rescue = UI.Button("Retrouver le Bureau organisé", coordinator.Rescue);
        rescue.HorizontalAlignment = HorizontalAlignment.Left;
        body.Children.Add(rescue);
        var show = UI.Button("Afficher seulement les icônes du Bureau", () => { Native.Desktop(false); });
        show.HorizontalAlignment = HorizontalAlignment.Left;
        show.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(show);
        var hide = UI.Button("Masquer les icônes du Bureau", () => { Native.Desktop(true); });
        hide.HorizontalAlignment = HorizontalAlignment.Left;
        hide.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(hide);
        var footer = new DockPanel();
        var close = UI.Button("Terminé", Close);
        close.MinWidth = 120;
        DockPanel.SetDock(close, Dock.Right);
        footer.Children.Add(close);
        var auto = UI.Text("Enregistré automatiquement · Les fichiers restent à leur place.", 12, UI.Dim);
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
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }
    void ThemeSection()
    {
        Section("THÈMES");
        Description("Chaque thème change la matière, la typographie, la forme des cartes et la disposition de l’accueil. Toutes les " +
            "fonctions restent identiques.");
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
                : new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(theme.Surface, 0),
                    new GradientStop(Theme.Mix(theme.Surface, theme.HaloA, .55), .58),
                    new GradientStop(Theme.Mix(theme.Surface, theme.HaloB, .45), 1)
                },
                                          new Point(0, 0),
                                          new Point(1, 1));
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
                    : new SolidColorBrush(Color.FromArgb(48, (byte)(theme.Light ? 40 : 255), (byte)(theme.Light ? 40 : 255),
                        (byte)(theme.Light ? 40 : 255)))
            };
            System.Windows.Automation.AutomationProperties.SetName(card, "Thème " + theme.Name);
            var text = new SolidColorBrush(theme.Text);
            var dim = new SolidColorBrush(theme.Dim);
            var stack = new StackPanel();
            var head = new Grid();
            head.ColumnDefinitions.Add(new()
            {
                Width = GridLength.Auto
            });
            head.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            head.ColumnDefinitions.Add(new()
            {
                Width = GridLength.Auto
            });
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
}
