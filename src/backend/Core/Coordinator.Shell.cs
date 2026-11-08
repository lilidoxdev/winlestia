// language: C# 12, file: Coordinator.Shell.cs, target: Windows 11 / .NET 8 WPF, barre latérale, barre d'outils, barre des tâches et menus
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;

internal sealed partial class Coordinator
{
    void BuildRail()
    {
        rail.ContentHost.Children.Clear();
        rail.Width = Store.State.RailWidth;
        rail.KeepOpen = true;
        rail.Radius = 32;
        var hit = UI.Button("", Open);
        hit.Padding = new Thickness(0);
        hit.BorderThickness = new Thickness(0);
        hit.BorderBrush = Brushes.Transparent;
        hit.Background = Brushes.Transparent;
        hit.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        hit.VerticalContentAlignment = VerticalAlignment.Stretch;
        hit.ToolTip = "Ouvrir ton espace · " + GlobalShortcut.Label(Store.State.Shortcut.Modifiers, Store.State.Shortcut.Key);
        System.Windows.Automation.AutomationProperties.SetName(hit, "Ouvrir Panneaux Glass");
        var layout = new Grid();
        var spine =
            new Border
            {
                Width = 3,
                Height = 100,
                CornerRadius = new CornerRadius(2),
                Background = UI.Accent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        layout.Children.Add(spine);
        var glyph = UI.Glyph("\uE8A9", 20);
        glyph.VerticalAlignment = VerticalAlignment.Top;
        glyph.Margin = new Thickness(0, 28, 0, 0);
        glyph.Opacity = .9;
        layout.Children.Add(glyph);
        var dot =
            new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = UI.Accent,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 28)
            };
        layout.Children.Add(dot);
        hit.Content = layout;
        rail.ContentHost.Children.Add(hit);
    }
    void BuildToolbar()
    {
        toolbar.Radius = 30;
        toolbar.Height = 64;
        toolbar.ContentHost.Margin = new Thickness(18, 10, 12, 10);
        var row = new Grid();
        row.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        row.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        row.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 30, 0) };
        var logo = UI.Glyph("\uE8A9", 22);
        logo.Margin = new Thickness(5, 0, 12, 0);
        brand.Children.Add(logo);
        brand.Children.Add(UI.Text("Panneaux", 18, null, true));
        var dj = UI.Text(" / dj", 13, UI.Dim);
        dj.VerticalAlignment = VerticalAlignment.Center;
        brand.Children.Add(dj);
        row.Children.Add(brand);
        // Le champ vit désormais dans la commande rapide (Ctrl F2) : la barre ne garde que l'espace et ses raccourcis.
        var hint = UI.Text("Ctrl F2 · tout ton espace en une ligne", 12, UI.Dim);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Opacity = .75;
        Grid.SetColumn(hint, 1);
        row.Children.Add(hint);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 0, 0, 0) };
        var favorite = UI.IconButton("\uE734", "Afficher uniquement les favoris", () =>
        {
            FavoritesOnly = !FavoritesOnly;
            RefreshBoards();
        });
        favorite.Click += (_, _) => favorite.Background = FavoritesOnly ? UI.Brush("#45C5E8FF") : UI.Brush("#0FFFFFFF");
        paletteChip = UI.Button(PaletteLabel, TogglePalette);
        paletteChip.Padding = new Thickness(11, 7, 11, 7);
        paletteChip.FontSize = 12;
        paletteChip.ToolTip = "Commande rapide · " + PaletteLabel;
        System.Windows.Automation.AutomationProperties.SetName(paletteChip, "Ouvrir la commande rapide");
        actions.Children.Add(paletteChip);
        actions.Children.Add(UI.IconButton("\uE80F", "Revenir à l’accueil", ShowHome));
        actions.Children.Add(UI.IconButton("\uE8A9", "Rétablir la disposition des panneaux", ResetLayout));
        actions.Children.Add(favorite);
        var pin = UI.IconButton("\uE718", "Garder l’espace ouvert", () => { Pinned = !Pinned; });
        pin.Background = Pinned ? UI.Brush("#45C5E8FF") : UI.Brush("#0FFFFFFF");
        pin.Click += (_, _) => pin.Background = Pinned ? UI.Brush("#45C5E8FF") : UI.Brush("#0FFFFFFF");
        actions.Children.Add(pin);
        actions.Children.Add(UI.IconButton("\uE713", "Personnaliser l’espace", () => Settings()));
        actions.Children.Add(UI.IconButton("\uE777", "Retrouver le Bureau organisé", Rescue));
        actions.Children.Add(UI.IconButton("\uE711", "Replier l’espace", Collapse));
        foreach (Button button in actions.Children)
            button.Margin = new Thickness(3, 0, 0, 0);
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        toolbar.ContentHost.Children.Add(row);
        toolbar.KeyDown += (_, e) => { if (e.Key == Key.Escape) Collapse(); };
    }
    internal void Open()
    {
        if (QtShell != null)
        {
            QtShell.Send("toggle");
            return;
        }
        OpenView(false);
    }
    internal bool TaskbarEnabled => taskbar?.Enabled == true;
    internal void ToggleTaskbarPin(Entry entry)
    {
        if (!Store.State.TaskbarPins.Remove(entry.Id))
            Store.State.TaskbarPins.Add(entry.Id);
        Store.Save();
        taskbar?.Refresh();
    }
    internal (bool Running, long Samples) ShellSampling => home.ShellSampling;
    internal string ShellPage => home.SelectedPage;
    internal void SetTaskbar(bool enabled)
    {
        bool applied = !enabled || taskbar?.Enable() == true;
        if (!enabled)
            taskbar?.Disable();
        Store.State.ReplaceTaskbar = enabled && applied;
        Store.Save();
        if (!applied)
            Message("Le secours de la barre n’a pas démarré. La barre Windows reste disponible.");
    }
    // L'arrêt du moteur rétablit la barre Windows sans réécrire la préférence de l'utilisateur :
    // un frontend planté ne doit pas décider que le dock est indésirable pour la suite.
    internal void SuspendTaskbar()
    {
        taskbar?.Disable();
    }
    internal void ShowShellPage(string id)
    {
        if (QtShell != null)
        {
            QtShell.Send(id == "dashboard" ? "toggle" : id == "media" ? "media" : id == "performance" ? "performance" : "access");
            return;
        }
        if (id == "files")
        {
            ShowWorkspace();
            return;
        }
        if (!open)
            Open();
        if (detailed)
            ShowHome();
        controlsExpanded = pcExpanded = false;
        home.SelectPage(id);
        Arrange(true);
        Native.Raise(home);
    }
    internal void LaunchTarget(string target)
    {
        CollapseImmediately();
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) { Message(ex.Message); }
    }
    internal void TaskbarMenu(Window parent, FrameworkElement anchor)
        => GlassMenu.Open(this,
                          parent,
                          anchor,
                          "Ta barre des tâches",
                          new[]
                          {
                              new MenuAction("Tableau de bord", "\uE8A9", () => ShowShellPage("dashboard")),
                              new MenuAction("Applications et fichiers", "\uE8B7", () => ShowWorkspace()),
                              new MenuAction("Personnaliser…", "\uE713", () => Settings(), Separator: true),
                              new MenuAction("Rétablir la barre Windows", "\uE777", () => SetTaskbar(false)),
                              new MenuAction("Quitter Panneaux", "\uE711", () => System.Windows.Application.Current.Shutdown())
                          },
                          true);
    internal void TaskbarAppMenu(Window parent, FrameworkElement anchor, Entry entry, bool pinned)
    {
        GlassMenu.Open(this, parent, anchor, entry.Name, new[] { new MenuAction("Ouvrir une nouvelle fenêtre",
                                                                                "\uE8A7",
                                                                                () => Launch(entry)), new MenuAction(pinned
                                                                                    ? "Détacher de la barre"
                                                                                    : "Épingler à la barre", "\uE718", () => { string id =
            Store.Entries.Any(e => e.Id == entry.Id) ? entry.Id : entry.Target;
        if (pinned) Store.State.TaskbarPins.Remove(entry.Id); else if (!string.IsNullOrWhiteSpace(id)
            && !Store.State.TaskbarPins.Contains(id)) Store.State.TaskbarPins.Add(id);
        Store.Save();
        taskbar?.Refresh(); }), new MenuAction("Tout mon espace", "\uE8B7", () => ShowWorkspace(), Separator: true) }, true);
    }
    internal string ShortcutStatus => shortcut.Error;
    internal string PaletteStatus => shortcut.PaletteError;
    internal string PinStatus => shortcut.PinError;
    internal string PaletteLabel => GlobalShortcut.Label(Store.State.PaletteShortcut.Modifiers, Store.State.PaletteShortcut.Key);
    internal string PinShortcutLabel => GlobalShortcut.Label(Store.State.PinShortcut.Modifiers, Store.State.PinShortcut.Key);
    internal string WallpaperStatus => shortcut.WallpaperError;
    internal string WallpaperLabel
        => shortcut.WallpaperActive
            ? GlobalShortcut.Label(shortcut.WallpaperModifiers, shortcut.WallpaperKey)
            : GlobalShortcut.Label(Store.State.WallpaperShortcut.Modifiers, Store.State.WallpaperShortcut.Key);
    internal string CaptureStatus => shortcut.CaptureError;
    internal string CaptureShortcutLabel => GlobalShortcut.Label(Store.State.CaptureShortcut.Modifiers, Store.State.CaptureShortcut.Key);
    internal bool SetPinShortcut(uint modifiers, uint key, out string error)
    {
        if (!shortcut.TrySetPin(modifiers, key, out error))
            return false;
        Store.State.PinShortcut = new()
        {
            Modifiers = modifiers,
            Key = key
        };
        Store.Save();
        RefreshPaletteLabels();
        return true;
    }
    internal bool SetCaptureShortcut(uint modifiers, uint key, out string error)
    {
        if (!shortcut.TrySetCapture(modifiers, key, out error))
            return false;
        Store.State.CaptureShortcut = new()
        {
            Modifiers = modifiers,
            Key = key
        };
        Store.Save();
        RefreshPaletteLabels();
        return true;
    }
    internal void ToggleAlwaysOnTop(IntPtr? handle = null)
    {
        IntPtr target = handle ?? Native.GetForegroundWindow();
        if (target != IntPtr.Zero && Native.Owns(target))
            target = lastForeign;
        if (target == IntPtr.Zero || !Native.IsWindow(target))
        {
            ShowToast("Aucune fenêtre à épingler");
            return;
        }
        bool applied = Native.ToggleTopmost(target, out bool topmost);
        if (!applied)
        {
            ShowToast("Windows a refusé ce changement");
            return;
        }
        string title = Native.Title(target);
        if (title.Length > 38)
            title = title[..37] + "…";
        ShowToast((topmost ? "Au premier plan · " : "Rangée normalement · ") + title);
    }
    internal void ApplyClipboardSettings() => clipboard.ApplySettings();
    internal void ToggleNotifications()
    {
        bool next = !SystemTools.NotificationsOn;
        SystemTools.SetNotifications(next);
        ShowToast(next ? "Notifications rétablies" : "Notifications coupées");
    }
    internal void RestartExplorer()
    {
        if (!Confirm("Redémarrer l’Explorateur Windows ?\n\n"
                + "La barre des tâches et le Bureau vont clignoter, tes fenêtres restent ouvertes."))
            return;
        CloseDialogs();
        SystemTools.RestartExplorer();
        ShowToast("Explorateur redémarré");
    }
}
