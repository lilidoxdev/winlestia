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

public sealed partial class MainWindow : Window
{
    TextBlock? toastText;
    System.Windows.Threading.DispatcherTimer? toastTimer;

    void Toast(string message)
    {
        if (toast == null)
            return;
        toastText!.Text = message;
        toast.Visibility = Visibility.Visible;
        toastTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ToastMs) };
        toastTimer.Tick -= HideToast;
        toastTimer.Tick += HideToast;
        toastTimer.Stop();
        toastTimer.Start();
    }
    void HideToast(object? sender, EventArgs e)
    {
        toastTimer?.Stop();
        if (toast != null)
            toast.Visibility = Visibility.Collapsed;
    }

    void DropTabs()
    {
        EndOpsState();
        StopWatch();
        saved.Clear();
        foreach (var view in tabs)
            view.Tab.Dispose();      // cancels enumerations, clears items
        tabs.Clear();
        tabStrip.Children.Clear();
        tabStrip.Children.Add(plus);
        list.ItemsSource = null;
        Thumbs.Reset();
        activeView = null!;
        shownKey = "";
        crumbPath = "";
    }

    void HideResident()
    {
        if (closed)
            return;
        Hide();
        DropTabs();
        Dispatcher.BeginInvoke(Trim, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        TryRecycle();
    }

    // Résident caché au-delà du budget de menus, sans opération de fichier, dialogue ni renommage ouvert : on quitte (code 75).
    // Appelé
    // au
    // masquage et à la fin de la dernière opération.
    void TryRecycle()
    {
        if (!App.Resident || App.Quitting || App.Recycling || IsVisible)
            return;
        if (ShellMenu.Builds <= App.RecycleMenus)
            return;
        if (FileOps.Active > 0 || OwnedWindows.Count > 0 || renaming != null)
            return;
        (Application.Current as App)?.BeginRecycle();
    }

    // Une fois par masquage : une collecte compactante bloquante, puis les pages libérées sont rendues au système.
    static void Trim()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (App.Resident && !App.Quitting)
        {
            e.Cancel = true;
            HideResident();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        deferred.Clear();
        toastTimer?.Stop();
        StopWatch();
        filterTimer?.Stop();
        // un client qui arrive maintenant obtient un démarrage à froid plutôt qu'une ouverture sur une fenêtre morte
        (Application.Current as App)?.StopServer();
        base.OnClosed(e);
        foreach (var view in tabs)
            view.Tab.Dispose();
    }

    // ---- sort ------------------------------------------------------------------------------------------------

    // Un seul tri pour tous les onglets, enregistré dans settings.json.
    internal void ApplySort(SortSpec spec)
    {
        foreach (var view in tabs.ToList())
            view.Tab.SetSort(spec);
        FilesSettings.SaveSort(spec);
    }

    internal void ResetSort()
    {
        foreach (var view in tabs.ToList())
            view.Tab.SetSort(SortSpec.Default);
        FilesSettings.ClearSort();
    }

    void ShowSortMenu()
    {
        var s = Active.Sort;
        var id = NativeMenu.Show(Hwnd, new NativeMenu.Item?[] {
            new(1, "Nom", s.Column == SortColumn.Name), new(2,
                                                            "Date de modification",
                                                            s.Column == SortColumn.Modified), new(3, "Type", s.Column == SortColumn.Type),
                                                                new(
                                                                4,
                                                                "Taille",
                                                                s.Column == SortColumn.Size),
            null, new(5, "Croissant", s.Ascending), new(6, "Décroissant", !s.Ascending),
            null, new(7, "Dossiers en premier", s.FoldersFirst),
            null, new(8, "Rétablir le tri par défaut") });
        switch (id)
        {
            case >= 1 and <= 4:
                ApplySort(s with
                {
                    Column = (SortColumn)(id switch
                    {
                        1 => SortColumn.Name,
                        2 => SortColumn.Modified,
                        3 => SortColumn.Type,
                        _ => SortColumn.Size
                    })
                });
                break;
            case 5:
                ApplySort(s with
                {
                    Ascending = true
                });
                break;
            case 6:
                ApplySort(s with
                {
                    Ascending = false
                });
                break;
            case 7:
                ApplySort(s with
                {
                    FoldersFirst = !s.FoldersFirst
                });
                break;
            case 8:
                ResetSort();
                break;
        }
    }

    // Arrivée / retrait d'un lecteur : les cibles épinglées sont revérifiées (sans sondage).
    IntPtr DeviceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0219 && wParam.ToInt64() is 0x8000 or 0x8004 or 0x0007)
            // reconstruit la liste des lecteurs et revérifie les épingles ; DBT_DEVICEARRIVAL, DBT_DEVICEREMOVECOMPLETE,
            // DBT_DEVNODES_CHANGED
            sidebar.ScheduleRecheck();
        return IntPtr.Zero;
    }

    void OpenPinnedFile(string path) => Open(new FileItem(System.IO.Path.GetFileName(path), path, false, 0, DateTime.Now, ""));

    // ---- opening / input -------------------------------------------------------------------------------------

    void OpenFromSidebar(string path, bool newTab)
    {
        if (newTab)
            AddTab(path);
        else
            Active.Go(path);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HandleKey(key, Keyboard.Modifiers))
            e.Handled = true;
    }

    // Tous les raccourcis de la fenêtre (l'auto-test l'appelle avec des touches simulées).
    internal bool HandleKey(Key key, ModifierKeys mod)
    {
        bool ctrl = (mod & ModifierKeys.Control) != 0, alt = (mod & ModifierKeys.Alt) != 0, shift = (mod & ModifierKeys.Shift) != 0;
        bool editing = addressBox.IsKeyboardFocusWithin || renameBox.IsKeyboardFocusWithin || sidebar.Editing;
        bool typing = editing || filterBox.IsKeyboardFocusWithin;
        clickArm++;
        if (alt && key == Key.Left)
            Active.Back();
        else if (alt && key == Key.Right)
            Active.Forward();
        else if (alt && key == Key.Up)
            Active.Up();
        else if (alt && !ctrl && !shift && key == Key.Return && !editing)
            ShowProperties();
        else if (key == Key.Back && mod == ModifierKeys.None && !typing)
            Active.Up();
        else if ((ctrl && key == Key.L) || (!alt && key == Key.F4) || (alt && !ctrl && key == Key.D))
            BeginEdit();
        else if (!typing && ctrl && shift && !alt && key == Key.C)
            CopyPaths(Selection());
        else if (ctrl && key == Key.T)
            AddTab(Active.Location);
        else if (ctrl && key == Key.W)
            CloseTab(activeView);
        else if (ctrl && key == Key.Tab)
            Cycle(shift ? -1 : 1);
        else if (ctrl && shift && key == Key.D1)
            SetView(ViewMode.Details);
        else if (ctrl && shift && key == Key.D2)
            SetView(ViewMode.Large);
        else if (ctrl && shift && key == Key.D3)
            SetView(ViewMode.Medium);
        else if (ctrl && shift && key == Key.D5)
            SetView(ViewMode.List);
        else if (ctrl && shift && key == Key.D6)
            SetView(ViewMode.Details);
        else if (!typing && ctrl && !shift && !alt && key == Key.Z)
            Fire(() => UndoAsync());
        else if (!typing && ((ctrl && !shift && !alt && key == Key.Y) || (ctrl && shift && !alt && key == Key.Z)))
            Fire(() => RedoAsync());
        else if (!typing && ctrl && !shift && !alt && key == Key.C)
            CopySelected(false);
        else if (!typing && ctrl && !shift && !alt && key == Key.X)
            CopySelected(true);
        else if (!typing && ctrl && !shift && !alt && key == Key.V)
            Fire(() => PasteAsync());
        else if (!typing && ctrl && !shift && !alt && key == Key.A)
            list.SelectAll();
        else if (!typing && ctrl && !shift && !alt && key == Key.Space)
            ToggleFocused();
        else if (!typing && ctrl && shift && key == Key.N)
            Fire(NewFolderAsync);
        else if (!editing && !ctrl && !alt && key == Key.F2)
            RenameSelected();
        else if (!typing && !ctrl && !alt && key == Key.Delete)
            Fire(() => DeleteSelectedAsync(shift));
        else if (!editing && !ctrl && !alt && shift && key == Key.F10)
            Dispatcher.BeginInvoke(() => ShowShellMenu(true, null), System.Windows.Threading.DispatcherPriority.Input);
        else if (!typing && !ctrl && !alt && !shift && key == Key.Apps)
            Dispatcher.BeginInvoke(() => ShowContextMenu(null), System.Windows.Threading.DispatcherPriority.Input);
        else if (!alt && ctrl && !shift && (key == Key.F || key == Key.E))
            ShowFilter(null);
        else if (key == Key.Escape
            && !renameBox.IsKeyboardFocusWithin
            && !addressBox.IsKeyboardFocusWithin
            && (Active.DeepActive || Active.Searching || Active.Filter.Length > 0 || list.SelectedItems.Count > 0))
        {
            if (Active.Searching || Active.DeepActive)
            {
                Active.CancelDeep();
                if (Active.Filter.Length > 0)
                    ClearFilter(true);
                UpdateStatus();
            }
            else if (Active.Filter.Length > 0)
                ClearFilter(true);
            else
                list.UnselectAll();
        }
        else if (key == Key.F5)
            Active.Reload();
        else if (ctrl && key == Key.H)
        {
            FolderTab.ShowHidden = !FolderTab.ShowHidden;
            Active.Reload();
        }
        else
            return false;
        return true;
    }

    // Ctrl+Espace : bascule l'élément qui a le focus clavier (le dernier sélectionné si le focus est ailleurs), sans toucher aux
    // autres.
    internal void ToggleFocused()
    {
        object? item = (Keyboard.FocusedElement as System.Windows.Controls.ListBoxItem)?.DataContext;
        if (item == null || !list.Items.Contains(item))
            item = list.SelectedItem ?? (list.Items.Count > 0 ? list.Items[0] : null);
        if (item == null)
            return;
        if (list.SelectedItems.Contains(item))
            list.SelectedItems.Remove(item);
        else
            list.SelectedItems.Add(item);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        if (e.ChangedButton == MouseButton.XButton1)
        {
            Active.Back();
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.XButton2)
        {
            Active.Forward();
            e.Handled = true;
        }
    }

    sealed class Relay : ICommand
    {
        readonly Action run; public Relay(Action run)
        {
            this.run = run;
        }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => run();
    }
}
