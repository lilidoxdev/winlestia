// Barre de commandes au-dessus de la liste (Nouveau, Couper, Copier, Coller, Renommer, Copier le chemin, Supprimer, Trier, Affichage,
// Annuler) et barre d'état en dessous
// (compteurs, taille de la sélection, actions rapides quand quelque chose est sélectionné). Chaque couleur est un jeton de palette
// vivant ; une commande désactivée utilise le rôle outline (jamais l'opacité).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PanneauxGlass;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint format);
    const int WM_CLIPBOARDUPDATE_CMD = 0x031D;

    // Une commande : icône + libellé facultatif (+ chevron pour les menus). Le Button n'est pas focalisable pour que les raccourcis
    // clavier fonctionnent encore après un clic.
    sealed class Cmd
    {
        internal readonly Button Button; internal readonly TextBlock Label; readonly TextBlock glyph; readonly Func<bool> enabled;
            readonly bool accent, small;
        internal bool Hot, Down;
        internal Cmd(string glyphCode, string? text, string tip, bool chevron, bool small, bool accent, Action click, Func<bool> enabled)
        {
            this.enabled = enabled;
            this.accent = accent;
            this.small = small;
            glyph = UI.Glyph(glyphCode, small ? 14 : 16);
            glyph.Width = small ? 18 : 20;
            Label = UI.Text(text ?? "", M3.FontSmall);
            Label.Margin = new Thickness(small ? 4 : 6, 0, 0, 0);
            Label.VerticalAlignment = VerticalAlignment.Center;
            Label.TextWrapping = TextWrapping.NoWrap;
            if (text == null)
                Label.Visibility = Visibility.Collapsed;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(glyph);
            row.Children.Add(Label);
            if (chevron)
            {
                var c = UI.Glyph("", 8);
                c.Margin = new Thickness(6, 0, 0, 0);
                c.Tag = "chevron";
                row.Children.Add(c);
            }
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(small ? 13 : 18));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            border.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            Button = new Button
            {
                Content = row,
                Height = small ? 26 : 36,
                MinWidth = small ? 26 : 36,
                Template = new ControlTemplate(typeof(Button)) { VisualTree = border },
                Background = Brushes.Transparent,
                Foreground = UI.White,
                FontFamily = M3.Body,
                FontSize = M3.FontSmall,
                Focusable = false,
                OverridesDefaultStyle = true,
                ToolTip = tip,
                SnapsToDevicePixels = true
            };
            ToolTipService.SetShowOnDisabled(Button, true);
            SetCompact(false);
            Button.MouseEnter += (_, _) => { Hot = true; Paint(); };
            Button.MouseLeave += (_, _) => { Hot = false; Down = false; Paint(); };
            Button.PreviewMouseLeftButtonDown += (_, _) => { Down = true; Paint(); };
            Button.PreviewMouseLeftButtonUp += (_, _) => { Down = false; Paint(); };
            Button.Click += (_, _) => click();
            Paint();
        }
        internal void SetCompact(bool compact)
        {
            bool hasText = Label.Text.Length > 0;
            Label.Visibility = hasText && !compact ? Visibility.Visible : Visibility.Collapsed;
            Button.Padding = hasText && !compact ? new Thickness(small ? 8 : 12, 0, small ? 10 : 12, 0) : new Thickness(0);
        }
        internal void Refresh()
        {
            Button.IsEnabled = enabled();
            Paint();
        }
        internal void Paint()
        {
            bool on = Button.IsEnabled;
            Button.Background = !on ? Brushes.Transparent : Down ? UI.Pressed : Hot ? UI.Hover : Brushes.Transparent;
            Button.Cursor = on ? Cursors.Hand : Cursors.Arrow;
            Brush fg = on ? UI.White : UI.T(M3Role.Outline);
            glyph.Foreground = on && accent ? UI.Accent : fg;
            Label.Foreground = fg;
            if (Button.Content is StackPanel p)
                foreach (var c in p.Children)
                    if (c is TextBlock { Tag: "chevron" } t)
                        t.Foreground = on ? UI.Label : fg;
        }
    }

    readonly List<Cmd> cmds = new();
    Cmd cmdUndo = null!, cmdPaste = null!;
    StackPanel selActions = null!;
    TextBlock searchHint = null!;

    bool RealFolder => activeView == null || !ThisPc.Is(Active.Location);
    int SelCount => list.SelectedItems.Count;

    FrameworkElement CommandBar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Cmd Add(string g, string? text, string tip, Action a, Func<bool> en, bool chevron = false, bool accent = false)
        {
            var c = new Cmd(g, text, tip, chevron, false, accent, a, en);
            cmds.Add(c);
            row.Children.Add(c.Button);
            return c;
        }
        void Sep() => row.Children.Add(new Border { Width = 1, Height = 20, Margin = new Thickness(M3.Pad2, 0, M3.Pad2, 0),
            Background = UI.Hairline, SnapsToDevicePixels = true });
        Add("", "Nouveau", "Nouveau dossier (Ctrl+Maj+N)", () => Fire(NewFolderAsync), () => RealFolder, false, true);
        Sep();
        Add("", null, "Couper (Ctrl+X)", () => CopySelected(true), () => RealFolder && SelCount > 0);
        Add("", null, "Copier (Ctrl+C)", () => CopySelected(false), () => RealFolder && SelCount > 0);
        cmdPaste = Add("", null, "Coller (Ctrl+V)", () => Fire(() => PasteAsync()), () => RealFolder && IsClipboardFormatAvailable(15));
        Add("", null, "Renommer (F2)", RenameSelected, () => RealFolder && SelCount == 1);
        Add("", null, "Copier le chemin (Ctrl+Maj+C)", () => CopyPaths(Selection()), () => SelCount > 0);
        Add("", null, "Supprimer (Suppr)", () => Fire(() => DeleteSelectedAsync(false)), () => RealFolder && SelCount > 0);
        Sep();
        var sort = Add("", "Trier", "Trier (clic droit sur l'en-tête : colonnes) - inclut « Rétablir le tri par défaut »",
            ShowSortMenu, ()
            => RealFolder, true);
        sortButton = sort.Button;
        Add("", "Affichage", "Affichage : Liste, Détails, Icônes", ShowViewMenu, () => RealFolder, true);
        cmdUndo = new Cmd("", null, "Annuler (Ctrl+Z)", false, false, false, () => Fire(() => UndoAsync()), ()
            => journal.NextUndo != null);
        cmds.Add(cmdUndo);

        var grid = new Grid { Height = 44 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Margin = new Thickness(M3.Pad3, 0, 0, 0);
        cmdUndo.Button.Margin = new Thickness(0, 0, M3.Pad3, 0);
        Grid.SetColumn(cmdUndo.Button, 1);
        grid.Children.Add(row);
        grid.Children.Add(cmdUndo.Button);
        grid.SizeChanged += (_, e) => { bool compact = e.NewSize.Width < 640; foreach (var c in cmds) c.SetCompact(compact);
            foreach (var c in quickCmds) c.SetCompact(e.NewSize.Width < 800); };
        journal.Changed += () => Dispatcher.BeginInvoke(UpdateCommands);
        Activated += (_, _) => UpdateCommands();
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(CmdHook);
        UpdateCommands();
        return new Border { Child = grid, BorderBrush = UI.Hairline, BorderThickness = new Thickness(0, 0, 0, 1),
            SnapsToDevicePixels = true
            };
    }

    IntPtr CmdHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE_CMD)
            Dispatcher.BeginInvoke(UpdateCommands);
        return IntPtr.Zero;
    }

    readonly List<Cmd> quickCmds = new();

    void UpdateCommands()
    {
        if (closed || cmds.Count == 0)
            return;
        foreach (var c in cmds)
            c.Refresh();
        cmdUndo.Button.ToolTip = journal.NextUndo is { } u ? (u.Irreversible ? "Annuler (impossible : " + u.Why + ")" : "Annuler "
            + u.Label
            + " (Ctrl+Z)") : "Annuler (Ctrl+Z) : rien à annuler";
        cmdPaste.Button.ToolTip = cmdPaste.Button.IsEnabled ? "Coller (Ctrl+V)"
            : "Coller (Ctrl+V) : le presse-papiers ne contient aucun fichier";
    }

    void ShowViewMenu()
    {
        var id = NativeMenu.Show(Hwnd, new NativeMenu.Item?[] {
            new(1, "Liste", view == ViewMode.List), new(2, "Détails", view == ViewMode.Details),
            new(3, "Icônes moyennes", view == ViewMode.Medium), new(4, "Grandes icônes", view == ViewMode.Large) });
        if (id is >= 1 and <= 4)
            SetView((ViewMode)(id - 1));
    }

    // Barre d'état : compteurs à gauche, actions rapides pour la sélection courante, les quatre boutons d'affichage à droite.
    FrameworkElement StatusBar()
    {
        var grid = new Grid { Height = 34 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.Margin = new Thickness(M3.Sp3, 0, M3.Sp3, 0);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.TextTrimming = TextTrimming.CharacterEllipsis;
        selActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed };
        void Quick(string g, string text, string tip, Action a)
        {
            var c = new Cmd(g, text, tip, false, true, false, a, () => true);
            quickCmds.Add(c);
            selActions.Children.Add(c.Button);
        }
        Quick("", "Ouvrir", "Ouvrir la sélection (Entrée)", () => OpenSelected(false));
        Quick("", "Copier le chemin", "Copier le chemin (Ctrl+Maj+C)", () => CopyPaths(Selection()));
        Quick("", "Supprimer", "Supprimer (Suppr)", () => Fire(() => DeleteSelectedAsync(false)));
        var viewBar = ViewBar();
        Grid.SetColumn(selActions, 1);
        Grid.SetColumn(viewBar, 2);
        grid.Children.Add(status);
        grid.Children.Add(selActions);
        grid.Children.Add(viewBar);
        return new Border { Child = grid, BorderBrush = UI.Hairline, BorderThickness = new Thickness(0, 1, 0, 0),
            SnapsToDevicePixels = true
            };
    }

    static string FormatBytes(long size) => size < 1024 ? size + " o" : size < 1024 * 1024 ? Math.Ceiling(size / 1024.0) + " Ko"
        : size < 1024L * 1024 * 1024 ? (size / 1048576.0).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " Mo"
            : (size / 1073741824.0).ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR")) + " Go";

    // « N sélectionnés · taille totale » (fichiers seulement : la taille d'un dossier exigerait un parcours récursif, jamais fait
    // ici).
    string SelectionSummary(int m)
    {
        long total = 0;
        int dirs = 0;
        foreach (FileItem item in list.SelectedItems)
        {
            if (item.IsDir)
                dirs++;
            else
                total += item.Size;
        }
        if (dirs == m)
            return "";
        return " — " + FormatBytes(total) + (dirs > 0 ? " (hors dossiers)" : "");
    }

    // Le champ de recherche nomme le dossier qu'il parcourt.
    void UpdateSearchHint(string location)
    {
        if (searchHint == null)
            return;
        string name = ThisPc.Is(location) ? ThisPc.Label : System.IO.Path.GetFileName(location.TrimEnd('\\', '/')) is { Length: > 0 } leaf
            ? leaf : location;
        searchHint.Text = "Rechercher dans " + name;
    }
}
