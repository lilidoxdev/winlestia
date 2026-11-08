// MainWindow : cycle de vie des onglets et affichage de l'onglet actif.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using PanneauxGlass;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    // ---- tabs ------------------------------------------------------------------------------------------------

    TabView AddTab(string path)
    {
        if (tabs.Count >= MaxTabs && activeView != null)
        {
            Toast("Trop d'onglets ouverts");
            return activeView;
        }
        var view = new TabView();
        view.Title = UI.Text("", M3.FontSmall, UI.White);
        view.Title.MaxWidth = 140;
        view.Title.TextTrimming = TextTrimming.CharacterEllipsis;
        view.Title.VerticalAlignment = VerticalAlignment.Center;
        var close = UI.Flat(UI.Glyph("", 10), 22, new CornerRadius(11));
        close.Width = 22;
        close.Margin = new Thickness(M3.Pad1, 0, 0, 0);
        close.ToolTip = "Fermer (Ctrl+W)";
        close.Click += (_, _) => CloseTab(view);
        var icon = UI.Symbol("", "", 16);
        icon.Foreground = UI.Accent;
        icon.Margin = new Thickness(0, 0, M3.Pad1, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        row.Children.Add(view.Title);
        row.Children.Add(close);
        view.Pill = new Border
        {
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(M3.Sp2, 0, M3.Pad1, 0),
            Child = row,
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 0, 2, 0),
            Cursor = Cursors.Hand
        };
        view.Pill.MouseEnter += (_, _) =>
        {
            view.Hot = true;
            Paint(view);
        };
        view.Pill.MouseLeave += (_, _) =>
        {
            view.Hot = false;
            Paint(view);
        };
        view.Pill.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                Activate(view);
            else if (e.ChangedButton == MouseButton.Middle)
            {
                CloseTab(view);
                e.Handled = true;
            }
        };
        view.Tab.Changed += OnTabChanged;
        Subscribe(view.Tab);
        tabs.Add(view);
        tabStrip.Children.Insert(tabStrip.Children.Count - 1, view.Pill);
        Activate(view);
        view.Tab.Go(path);
        return view;
    }

    void CloseTab(TabView view)
    {
        int index = tabs.IndexOf(view);
        if (index < 0)
            return;
        if (tabs.Count == 1)
        {
            Close();
            return;
        }
        tabs.RemoveAt(index);
        saved.Remove(view);
        tabStrip.Children.Remove(view.Pill);
        view.Tab.Dispose();
        if (view == activeView)
            Activate(tabs[Math.Min(index, tabs.Count - 1)]);
    }

    void Activate(TabView view)
    {
        var previous = activeView;
        if (previous != null && previous != view)
        {
            SaveView(previous);
            previous.Tab.Stale = true;
        }   // not watched while inactive: re-synced when shown again
        activeView = view;
        if (previous != null)
            Paint(previous);
        Paint(view);
        view.Pill.BringIntoView();
        list.ItemsSource = view.Tab.Items;
        crumbPath = "";
        shownKey = "";
        Refresh(view.Tab);
        RestoreView(view);
    }

    void Cycle(int step)
    {
        if (tabs.Count < 2)
            return;
        Activate(tabs[(tabs.IndexOf(activeView) + step + tabs.Count) % tabs.Count]);
    }

    void Paint(TabView view)
    {
        bool on = view == activeView;
        view.Pill.Background = view.Drop ? UI.T(M3Role.PrimaryContainer) : on ? UI.T(M3Role.PrimaryContainer) : view.Hot ? UI.Tint
            : Brushes.Transparent;
        view.Title.Foreground = view.Drop ? UI.T(M3Role.OnPrimaryContainer) : on ? UI.T(M3Role.OnPrimaryContainer) : UI.White;
    }

    void OnTabChanged(FolderTab tab)
    {
        var view = tabs.FirstOrDefault(v => v.Tab == tab);
        if (view == null)
            return;
        if (view.Title.Text != tab.Title)
            view.Title.Text = tab.Title;
        if (tab == Active)
            Refresh(tab);
    }

    // ---- active tab view -------------------------------------------------------------------------------------

    void Refresh(FolderTab tab)
    {
        string title = tab.Title;
        if (activeView.Title.Text != title)
            activeView.Title.Text = title;
        Title = "Winlestia Fichiers - " + tab.Location;
        SetEnabled(back, tab.CanBack);
        SetEnabled(forward, tab.CanForward);
        SetEnabled(up, tab.Parent != null);
        if (crumbPath != tab.Location)
        {
            crumbPath = tab.Location;
            BuildCrumbs(tab.Location);
            UpdateSearchHint(tab.Location);
            sidebar.Highlight(tab.Location);
        }
        for (int i = 0; i < chevrons.Length; i++)
        {
            bool sorted = (int)tab.Sort.Column == i;
            chevrons[i].Visibility = sorted ? Visibility.Visible : Visibility.Collapsed;
            if (sorted)
                chevrons[i].Text = tab.Sort.Ascending ? "" : "";
        }
        WatchFolder(tab.Location);
        SyncFilterUi(tab);
        SyncLocationColumn(tab);
        bool noResult = tab.FilterEmpty;
        bool pc = ThisPc.Is(tab.Location);
        string key =
            pc ? "thispc" : noResult ? "nores|" + tab.Filter : tab.State is LoadState.Loading or LoadState.Ready ? "list" : tab.State + "|"
                + tab.Location;
        if (key != shownKey)
        {
            shownKey = key;
            content.Child = pc ? ThisPcPageView() : noResult ? NoResult(tab) : tab.State is LoadState.Loading or LoadState.Ready ? listHost
                : EmptyFor(tab);
        }
        if (renaming != null && Active.Find(renaming.FullPath) == null)
            CancelRename();
        UpdateStatus();
    }

    static void SetEnabled(Button b, bool on)
    {
        if (b.IsEnabled != on)
            b.IsEnabled = on;
    }

    FrameworkElement EmptyFor(FolderTab tab)
    {
        string where = tab.Location;
        Action explorer = () => PathRouter.LaunchExplorer(where);
        return tab.State switch
        {
            LoadState.Denied => UI.EmptyState("",
                                              "",
                                              "Accès refusé",
                                              "Vous n'avez pas l'autorisation d'ouvrir ce dossier.",
                                              "Ouvrir dans Explorer",
                                              explorer),
            LoadState.NotFound => UI.EmptyState("",
                                                "",
                                                "Dossier introuvable",
                                                "Ce dossier n'existe pas ou n'est plus disponible.",
                                                "Remonter",
                                                () => Active.Up()),
            LoadState.Failed => UI.EmptyState("", "", "Impossible d'ouvrir ce dossier", tab.Error, "Ouvrir dans Explorer", explorer),
            _ => UI.EmptyState("", "", "Ce dossier est vide", "Ce dossier ne contient aucun élément.")
        };
    }

    void UpdateStatus()
    {
        if (ThisPc.Is(Active.Location))
        {
            status.Text = $"{ThisPcCards.Count} lecteur{(ThisPcCards.Count > 1 ? "s" : "")}";
            UpdateCommands();
            return;
        }
        int n = Active.Items.Count, m = list.SelectedItems.Count;
        string count = Active.Filter.Length > 0 ? $"{n} sur {Active.Total} élément{(Active.Total > 1 ? "s" : "")}"
            : $"{n} élément{(n > 1 ? "s" : "")}";
        status.Text = count
            + (m > 0 ? $" · {m} sélectionné{(m > 1 ? "s" : "")}{SelectionSummary(m)}" : "")
            + (Active.Searching ? " · Recherche en cours…" : "");
        selActions.Visibility = m > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCommands();
    }
}
