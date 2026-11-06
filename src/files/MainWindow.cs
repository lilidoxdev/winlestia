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

// Step 2: tabs with per-tab history, breadcrumb/editable address, sidebar, virtualized details list, status line.
public sealed partial class MainWindow : Window {
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    sealed class TabView {
        internal readonly FolderTab Tab = new();
        internal Border Pill = null!; internal TextBlock Title = null!; internal bool Hot, Drop;
    }

    readonly List<TabView> tabs = new();
    TabView activeView = null!;
    FolderTab Active => activeView.Tab;
    readonly StackPanel tabStrip = new() { Orientation = Orientation.Horizontal };
    readonly BulkListBox list = new();
    readonly Border content = new();
    readonly TextBlock status = UI.Text("", M3.FontSmall, UI.Label);
    readonly TextBlock[] chevrons = new TextBlock[4];
    Grid listHost = null!;
    FrameworkElement headerRow = null!;
    Button sortButton = null!; Border addressPill = null!;
    Button back = null!, forward = null!, up = null!, plus = null!;
    StackPanel crumbs = null!; ScrollViewer crumbScroll = null!; TextBox addressBox = null!;
    Sidebar sidebar = null!;
    bool containersLogged;
    string shownKey = "", crumbPath = "";
    readonly bool reduced = M3.ReducedEffects;

    internal MainWindow(string? path) {   // null: hidden preload, no tab yet
        Perf.Mark("ctor begin");
        Title = "Winlestia Fichiers" + (path == null ? "" : " - " + path);
        WindowStyle = WindowStyle.None;
        Width = 1100; Height = 700; MinWidth = 760; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = M3.Body;
        UseLayoutRounding = true; SnapsToDevicePixels = true;     // crisp 1 px edges at 125 % / 150 %
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/winlestia-files.ico"), System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation, System.Windows.Media.Imaging.BitmapCacheOption.OnDemand); } catch { }
        Background = UI.T(M3Role.Surface, reduced ? (byte)255 : (byte)0xEE);
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 48, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = reduced ? new Thickness(0) : new Thickness(-1), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
        SourceInitialized += (_, _) => { Thumbs.Dpi = VisualTreeHelper.GetDpi(this).DpiScaleX; Perf.Mark("SourceInitialized"); ApplyDwm(); Perf.Mark("dwm applied"); };
        InputBindings.Add(new KeyBinding(new Relay(() => PathRouter.LaunchExplorer(Active.Location)), Key.E, ModifierKeys.Control | ModifierKeys.Shift));

        Perf.Mark("chrome set");
        FileItem.UseMaterial = M3.Symbols != null;
        Resources["IconFont"] = M3.Symbols ?? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        Perf.Mark("symbols font");
        Resources.MergedDictionaries.Add(Styles.Load());
        Perf.Mark("Styles.Load");

        var root = new Grid { Margin = new Thickness(M3.Sp2) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var tabRow = TabRow(); Perf.Mark("TabRow"); var navRow = NavRow();
        Grid.SetRow(navRow, 1); Grid.SetRow(tabRow, 0);
        root.Children.Add(tabRow); root.Children.Add(navRow);

        Perf.Mark("tab+nav rows");
        sidebar = new Sidebar(OpenFromSidebar, OpenPinnedFile);
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(DeviceHook);
        Activated += (_, _) => sidebar.RecheckThrottled();
        Perf.Mark("sidebar");
        var body = new Grid { Margin = new Thickness(0, M3.Sp1, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(M3.Sp2) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(sidebar.Root, 0);
        var main = MainCard();
        Grid.SetColumn(main, 2);
        body.Children.Add(sidebar.Root); body.Children.Add(main);
        Grid.SetRow(body, 2); root.Children.Add(body);
        Perf.Mark("main card");
        Content = root;
        Perf.Mark("BuildUi done");

        if (Perf.Enabled) {
            Perf.Stall = new StallMonitor();
            ContentRendered += (_, _) => { Perf.Mark("ContentRendered"); Perf.FirstFrame(); };
            ContentRendered += (_, _) => Perf.Log($"process start -> first window render (ContentRendered) {(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds:F0} ms");
        }
        Perf.Mark("before AddTab");
        if (path != null) OpenFirstTab(path);
        Perf.Mark("after AddTab");
        PaletteLoader.Start(Dispatcher, ApplyDwm);
        WireOps();
        WireDnd();
        WireRubber();
        sidebar.DriveMenu = p => ShowContextMenu(null, p);
        sidebar.DrivesChanged += () => { if (activeView?.Tab is { } t && ThisPc.Is(t.Location) && thisPcWrap != null) FillThisPc(); };
        InitWatch();
        if (App.Resident) FileOps.Idle += () => Dispatcher.BeginInvoke(TryRecycle);
        StartSelfTest();
    }

    // Enumeration runs in parallel with the rest of the construction; rows queued within 60 ms are in the first layout pass.
    void OpenFirstTab(string path) {
        AddTab(path);
        if (Active.FirstPosted.Wait(60)) Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
    }

    // ---- resident mode ---------------------------------------------------------------------------------------

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    // Hidden preload: create the HWND and run one ListBox measure/arrange with fake rows so the first real Show() has nothing left to JIT.
    internal void Preload() {
        new WindowInteropHelper(this).EnsureHandle();
        if (Content is FrameworkElement root) {
            var data = new List<FileItem>();
            for (int i = 0; i < 40; i++) data.Add(new FileItem("f" + i, "f" + i, false, 1, DateTime.Now, "Fichier"));
            list.ItemsSource = data;
            var size = new Size(Width, Height);
            root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
            list.ItemsSource = null;
        }
        Dispatcher.BeginInvoke(Trim, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    // Open request from the pipe (UI thread). Hidden: back to a single fresh tab. Visible: the existing tab of that folder, else one more tab (max MaxTabs).
    // Never re-entrant: OpenFirstTab may pump the dispatcher, and a request handled inside that pump would nest another ShowAt (one more stack level per
    // queued request: this is the 0xC00000FD seen with ~450 rapid opens). A nested call is parked and run by the outermost one.
    internal void ShowAt(string path) {
        if (closed) { App.LogLine("ShowAt ignored: window already closed"); return; }
        if (showing) { if (deferred.Count < MaxTabs) deferred.Enqueue(path); return; }
        showing = true;
        try {
            ShowAtCore(path);
            while (deferred.Count > 0 && !closed) ShowAtCore(deferred.Dequeue());
        } finally { showing = false; }
    }

    void ShowAtCore(string path) {
        Perf.Log("ShowAt " + (IsVisible ? "visible" : "hidden"));
        if (!IsVisible) {
            DropTabs();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            OpenFirstTab(path);
            if (closed) return;
            try { Show(); }
            catch (InvalidOperationException ex) {   // window closed between the check and Show(): not a crash, a fresh process takes the request
                App.LogLine("ShowAt: Show after close (" + ex.Message + "), handed to a fresh process");
                closed = true;
                try {
                    (Application.Current as App)?.ReleasePipe();
                    // the parser has no "--" separator and joins all args into one path: one process per path, '-' prefixed paths made absolute
                    var all = new List<string> { path }; all.AddRange(deferred); deferred.Clear();
                    foreach (var p in all) {
                        var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                        psi.ArgumentList.Add(p.StartsWith('-') ? System.IO.Path.GetFullPath(p) : p);
                        System.Diagnostics.Process.Start(psi)?.Dispose();
                    }
                } catch { }
                return;
            }
            PaletteLoader.Start(Dispatcher, ApplyDwm);       // one-shot, off the UI thread: picks up a theme/wallpaper change made while hidden
        } else {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            var same = tabs.Find(v => string.Equals(v.Tab.Location.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (same != null) { if (same != activeView) Activate(same); }
            else AddTab(path);
        }
        if (closed) return;
        Activate();
        SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    // ---- tab cap and inline toast ----------------------------------------------------------------------------

    internal const int MaxTabs = 50;
    bool closed, showing;
    readonly Queue<string> deferred = new();
    Border? toast; TextBlock? toastText; System.Windows.Threading.DispatcherTimer? toastTimer;

    void Toast(string message) {
        if (toast == null) return;
        toastText!.Text = message; toast.Visibility = Visibility.Visible;
        toastTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
        toastTimer.Tick -= HideToast; toastTimer.Tick += HideToast;
        toastTimer.Stop(); toastTimer.Start();
    }
    void HideToast(object? sender, EventArgs e) { toastTimer?.Stop(); if (toast != null) toast.Visibility = Visibility.Collapsed; }

    void DropTabs() {
        EndOpsState(); StopWatch(); saved.Clear();
        foreach (var view in tabs) view.Tab.Dispose();      // cancels enumerations, clears items
        tabs.Clear();
        tabStrip.Children.Clear(); tabStrip.Children.Add(plus);
        list.ItemsSource = null;
        Thumbs.Reset();
        activeView = null!; shownKey = ""; crumbPath = "";
    }

    void HideResident() {
        if (closed) return;
        Hide();
        DropTabs();
        Dispatcher.BeginInvoke(Trim, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        TryRecycle();
    }

    // Hidden resident past the menu budget, with no file operation, dialog or rename open: leave (exit 75). Called when hiding and when the last operation ends.
    void TryRecycle() {
        if (!App.Resident || App.Quitting || App.Recycling || IsVisible) return;
        if (ShellMenu.Builds <= App.RecycleMenus) return;
        if (FileOps.Active > 0 || OwnedWindows.Count > 0 || renaming != null) return;
        (Application.Current as App)?.BeginRecycle();
    }

    // Once per hide: one blocking compacting collection, then give the freed pages back to the OS.
    static void Trim() {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) {
        if (App.Resident && !App.Quitting) { e.Cancel = true; HideResident(); return; }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e) {
        closed = true; deferred.Clear(); toastTimer?.Stop(); StopWatch(); filterTimer?.Stop();
        (Application.Current as App)?.StopServer();           // a client arriving now gets a cold start instead of an open on a dead window
        base.OnClosed(e);
        foreach (var view in tabs) view.Tab.Dispose();
    }

    // ---- layout ----------------------------------------------------------------------------------------------

    FrameworkElement TabRow() {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        plus = UI.Flat(UI.Glyph("", 14), 32, new CornerRadius(16)); plus.Width = 32; plus.ToolTip = "Nouvel onglet (Ctrl+T)";
        plus.Click += (_, _) => AddTab(Active.Location);
        tabStrip.Children.Add(plus);
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalAlignment = HorizontalAlignment.Left, Content = tabStrip, VerticalAlignment = VerticalAlignment.Center };
        WindowChrome.SetIsHitTestVisibleInChrome(scroll, true);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(Caption("", () => WindowState = WindowState.Minimized));
        controls.Children.Add(Caption("", () => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized));
        controls.Children.Add(Caption("", Close));
        Grid.SetColumn(controls, 1);
        grid.Children.Add(scroll); grid.Children.Add(controls);
        return grid;
    }

    FrameworkElement NavRow() {
        var grid = new Grid { Margin = new Thickness(0, M3.Pad1, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sortButton = null!;                         // the sort command lives in the command bar (MainWindow.Cmd.cs)
        var right = new StackPanel { Orientation = Orientation.Horizontal }; right.Children.Add(BuildFilter());
        Grid.SetColumn(right, 2);
        var nav = new StackPanel { Orientation = Orientation.Horizontal };
        back = NavButton("", "Précédent (Alt+Gauche)", () => Active.Back());
        forward = NavButton("", "Suivant (Alt+Droite)", () => Active.Forward());
        up = NavButton("", "Dossier parent (Alt+Haut)", () => Active.Up());
        nav.Children.Add(back); nav.Children.Add(forward); nav.Children.Add(up);

        crumbs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(M3.Pad1, 0, M3.Pad1, 0) };
        crumbScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = crumbs, VerticalAlignment = VerticalAlignment.Center };
        crumbs.SizeChanged += (_, _) => crumbScroll.ScrollToRightEnd();
        addressBox = new TextBox { Height = 36, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(M3.Sp3, 0, M3.Sp3, 0), Background = Brushes.Transparent, Foreground = UI.White,
            BorderThickness = new Thickness(0), FontSize = M3.FontSmall, FontFamily = M3.Body, CaretBrush = UI.White, Visibility = Visibility.Collapsed, Template = AddressTemplate() };
        addressBox.LostKeyboardFocus += (_, _) => EndEdit();
        addressBox.GotKeyboardFocus += (_, _) => addressPill.BorderBrush = UI.T(M3Role.Primary);
        addressBox.LostKeyboardFocus += (_, _) => addressPill.BorderBrush = UI.Hairline;
        addressBox.KeyDown += (_, e) => {
            if (e.Key == Key.Enter) { SubmitAddress(addressBox.Text); e.Handled = true; }
            else if (e.Key == Key.Escape) { EndEdit(); list.Focus(); e.Handled = true; }
        };
        var pill = addressPill = new Border { Height = 36, CornerRadius = new CornerRadius(18), Background = UI.T(M3Role.SurfaceContainer), BorderBrush = UI.Hairline, BorderThickness = new Thickness(1), Margin = new Thickness(M3.Sp2, 0, M3.Sp2, 0), Cursor = Cursors.IBeam, ClipToBounds = true, SnapsToDevicePixels = true };
        var inner = new Grid(); inner.Children.Add(crumbScroll); inner.Children.Add(addressBox);
        pill.Child = inner;
        pill.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { BeginEdit(); e.Handled = true; } };   // double click on the free area right of the segments (buttons mark their own clicks handled)
        Grid.SetColumn(pill, 1);
        grid.Children.Add(nav); grid.Children.Add(pill); grid.Children.Add(right);
        return grid;
    }

    Button NavButton(string glyph, string tip, Action action) {
        var b = UI.Flat(UI.Glyph(glyph, 16), 36, new CornerRadius(18)); b.ToolTip = tip; b.IsEnabled = false; b.Opacity = .38; if (b.Content is TextBlock g) g.Foreground = UI.Accent;
        b.Click += (_, _) => action();
        return b;
    }

    FrameworkElement MainCard() {
        list.Style = (Style)Resources["ListStyle"];
        list.ItemTemplate = (DataTemplate)Resources["RowTemplate"];
        list.ItemContainerStyle = (Style)Resources["RowStyle"];
        list.SelectionMode = SelectionMode.Extended;
        list.Background = Brushes.Transparent; list.BorderThickness = new Thickness(0); list.Foreground = UI.White; list.FontSize = M3.FontSmall;
        list.Padding = new Thickness(M3.Pad1, 0, 0, M3.Pad1);
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Item);
        ScrollViewer.SetCanContentScroll(list, true);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Visible);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.SelectionChanged += (_, _) => UpdateStatus();
        if (Perf.Enabled) list.ItemContainerGenerator.StatusChanged += (_, _) => { if (list.ItemContainerGenerator.Status == System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated && !containersLogged) { containersLogged = true; Perf.Mark("first containers generated"); } };
        list.MouseDoubleClick += (_, e) => {
            if (e.ChangedButton == MouseButton.Left && e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(list, source) is ListBoxItem { DataContext: FileItem item }) { clickArm++; Open(item); }
        };
        list.KeyDown += (_, e) => {
            if (e.Key == Key.Enter && !renameBox.IsKeyboardFocusWithin && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Control && list.SelectedItem != null) { OpenSelected(Keyboard.Modifiers == ModifierKeys.Control); e.Handled = true; }
        };

        listHost = new Grid();
        listHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
        listHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = headerRow = Header(); list.SetValue(Grid.RowProperty, 1);
        listHost.Children.Add(header); listHost.Children.Add(list);

        var card = new Grid();
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var cmdBar = CommandBar(); var statusBar = StatusBar();
        Grid.SetRow(content, 1); Grid.SetRow(statusBar, 2); 
        card.Children.Add(cmdBar); card.Children.Add(content); card.Children.Add(statusBar);
        SetView(ParseView(FilesSettings.InitialView()), false);
        toastText = UI.Text("", M3.FontSmall, UI.T(M3Role.OnSecondaryContainer));
        toast = new Border { Child = toastText, Background = UI.T(M3Role.SecondaryContainer), CornerRadius = new CornerRadius(16), Padding = new Thickness(M3.Sp3, 6, M3.Sp3, 6),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, M3.Sp2), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
        Grid.SetRowSpan(toast, 3); card.Children.Add(toast);
        return new Border { Background = UI.Panel, CornerRadius = new CornerRadius(M3.Normal), Child = card, BorderBrush = UI.Hairline, BorderThickness = new Thickness(1), ClipToBounds = true };
    }

    static ControlTemplate AddressTemplate() {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        host.SetValue(Control.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(host);
        return new ControlTemplate(typeof(TextBox)) { VisualTree = border };
    }

    static Button Caption(string glyph, Action action) {
        var b = UI.Flat(UI.Glyph(glyph, 12), 32, new CornerRadius(16)); b.Width = 40;
        b.Click += (_, _) => action(); WindowChrome.SetIsHitTestVisibleInChrome(b, true); return b;
    }

    // DWM: immersive dark follows the palette (20), rounded corners (33=2), Mica backdrop (38=2) unless reduced effects.
    internal void RepaintChrome() => ApplyDwm();     // after a live palette swap (dark/light title bar attribute)

    void ApplyDwm() {
        var hwnd = new WindowInteropHelper(this).Handle; if (hwnd == IntPtr.Zero) return;
        var source = HwndSource.FromHwnd(hwnd); if (source?.CompositionTarget != null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
        int dark = Theme.Current.IsDark ? 1 : 0; DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
        int corner = 2; DwmSetWindowAttribute(hwnd, 33, ref corner, 4);
        int backdrop = reduced ? 1 : 2; DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) {
        base.OnDpiChanged(oldDpi, newDpi);
        Thumbs.Dpi = newDpi.DpiScaleX;
    }

    // ---- tabs ------------------------------------------------------------------------------------------------

    TabView AddTab(string path) {
        if (tabs.Count >= MaxTabs && activeView != null) { Toast("Trop d'onglets ouverts"); return activeView; }
        var view = new TabView();
        view.Title = UI.Text("", M3.FontSmall, UI.White); view.Title.MaxWidth = 140; view.Title.TextTrimming = TextTrimming.CharacterEllipsis; view.Title.VerticalAlignment = VerticalAlignment.Center;
        var close = UI.Flat(UI.Glyph("", 10), 22, new CornerRadius(11)); close.Width = 22; close.Margin = new Thickness(M3.Pad1, 0, 0, 0); close.ToolTip = "Fermer (Ctrl+W)";
        close.Click += (_, _) => CloseTab(view);
        var icon = UI.Symbol("", "", 16); icon.Foreground = UI.Accent; icon.Margin = new Thickness(0, 0, M3.Pad1, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon); row.Children.Add(view.Title); row.Children.Add(close);
        view.Pill = new Border { Height = 32, CornerRadius = new CornerRadius(16), Padding = new Thickness(M3.Sp2, 0, M3.Pad1, 0), Child = row, Background = Brushes.Transparent, Margin = new Thickness(0, 0, 2, 0), Cursor = Cursors.Hand };
        view.Pill.MouseEnter += (_, _) => { view.Hot = true; Paint(view); };
        view.Pill.MouseLeave += (_, _) => { view.Hot = false; Paint(view); };
        view.Pill.MouseDown += (_, e) => {
            if (e.ChangedButton == MouseButton.Left) Activate(view);
            else if (e.ChangedButton == MouseButton.Middle) { CloseTab(view); e.Handled = true; }
        };
        view.Tab.Changed += OnTabChanged;
        Subscribe(view.Tab);
        tabs.Add(view);
        tabStrip.Children.Insert(tabStrip.Children.Count - 1, view.Pill);
        Activate(view);
        view.Tab.Go(path);
        return view;
    }

    void CloseTab(TabView view) {
        int index = tabs.IndexOf(view); if (index < 0) return;
        if (tabs.Count == 1) { Close(); return; }
        tabs.RemoveAt(index); saved.Remove(view);
        tabStrip.Children.Remove(view.Pill);
        view.Tab.Dispose();
        if (view == activeView) Activate(tabs[Math.Min(index, tabs.Count - 1)]);
    }

    void Activate(TabView view) {
        var previous = activeView;
        if (previous != null && previous != view) { SaveView(previous); previous.Tab.Stale = true; }   // not watched while inactive: re-synced when shown again
        activeView = view;
        if (previous != null) Paint(previous);
        Paint(view);
        view.Pill.BringIntoView();
        list.ItemsSource = view.Tab.Items;
        crumbPath = ""; shownKey = "";
        Refresh(view.Tab);
        RestoreView(view);
    }

    void Cycle(int step) {
        if (tabs.Count < 2) return;
        Activate(tabs[(tabs.IndexOf(activeView) + step + tabs.Count) % tabs.Count]);
    }

    void Paint(TabView view) {
        bool on = view == activeView;
        view.Pill.Background = view.Drop ? UI.T(M3Role.PrimaryContainer) : on ? UI.T(M3Role.PrimaryContainer) : view.Hot ? UI.Tint : Brushes.Transparent;
        view.Title.Foreground = view.Drop ? UI.T(M3Role.OnPrimaryContainer) : on ? UI.T(M3Role.OnPrimaryContainer) : UI.White;
    }

    void OnTabChanged(FolderTab tab) {
        var view = tabs.FirstOrDefault(v => v.Tab == tab);
        if (view == null) return;
        if (view.Title.Text != tab.Title) view.Title.Text = tab.Title;
        if (tab == Active) Refresh(tab);
    }

    // ---- active tab view -------------------------------------------------------------------------------------

    void Refresh(FolderTab tab) {
        string title = tab.Title;
        if (activeView.Title.Text != title) activeView.Title.Text = title;
        Title = "Winlestia Fichiers - " + tab.Location;
        SetEnabled(back, tab.CanBack); SetEnabled(forward, tab.CanForward); SetEnabled(up, tab.Parent != null);
        if (crumbPath != tab.Location) { crumbPath = tab.Location; BuildCrumbs(tab.Location); UpdateSearchHint(tab.Location); sidebar.Highlight(tab.Location); }
        for (int i = 0; i < chevrons.Length; i++) {
            bool sorted = (int)tab.Sort.Column == i;
            chevrons[i].Visibility = sorted ? Visibility.Visible : Visibility.Collapsed;
            if (sorted) chevrons[i].Text = tab.Sort.Ascending ? "" : "";
        }
        WatchFolder(tab.Location); SyncFilterUi(tab); SyncLocationColumn(tab);
        bool noResult = tab.FilterEmpty;
        bool pc = ThisPc.Is(tab.Location);
        string key = pc ? "thispc" : noResult ? "nores|" + tab.Filter : tab.State is LoadState.Loading or LoadState.Ready ? "list" : tab.State + "|" + tab.Location;
        if (key != shownKey) { shownKey = key; content.Child = pc ? ThisPcPageView() : noResult ? NoResult(tab) : tab.State is LoadState.Loading or LoadState.Ready ? listHost : EmptyFor(tab); }
        if (renaming != null && Active.Find(renaming.FullPath) == null) CancelRename();
        UpdateStatus();
    }

    static void SetEnabled(Button b, bool on) { if (b.IsEnabled != on) b.IsEnabled = on; }

    FrameworkElement EmptyFor(FolderTab tab) {
        string where = tab.Location;
        Action explorer = () => PathRouter.LaunchExplorer(where);
        return tab.State switch {
            LoadState.Denied => UI.EmptyState("", "", "Accès refusé", "Vous n'avez pas l'autorisation d'ouvrir ce dossier.", "Ouvrir dans Explorer", explorer),
            LoadState.NotFound => UI.EmptyState("", "", "Dossier introuvable", "Ce dossier n'existe pas ou n'est plus disponible.", "Remonter", () => Active.Up()),
            LoadState.Failed => UI.EmptyState("", "", "Impossible d'ouvrir ce dossier", tab.Error, "Ouvrir dans Explorer", explorer),
            _ => UI.EmptyState("", "", "Ce dossier est vide", "Ce dossier ne contient aucun élément.") };
    }

    void UpdateStatus() {
        if (ThisPc.Is(Active.Location)) { status.Text = $"{ThisPcCards.Count} lecteur{(ThisPcCards.Count > 1 ? "s" : "")}"; UpdateCommands(); return; }
        int n = Active.Items.Count, m = list.SelectedItems.Count;
        string count = Active.Filter.Length > 0 ? $"{n} sur {Active.Total} élément{(Active.Total > 1 ? "s" : "")}" : $"{n} élément{(n > 1 ? "s" : "")}";
        status.Text = count + (m > 0 ? $" · {m} sélectionné{(m > 1 ? "s" : "")}{SelectionSummary(m)}" : "") + (Active.Searching ? " · Recherche en cours…" : "");
        selActions.Visibility = m > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCommands();
    }

    // ---- address ---------------------------------------------------------------------------------------------

    void BuildCrumbs(string path) {
        crumbs.Children.Clear();
        if (ThisPc.Is(path)) { var pcb = UI.Flat(ThisPc.Label, 28, new CornerRadius(14)); pcb.Padding = new Thickness(M3.Pad3, 0, M3.Pad3, 0); pcb.Foreground = UI.Accent; crumbs.Children.Add(pcb); return; }
        string root = System.IO.Path.GetPathRoot(path) ?? "";
        var parts = new List<(string label, string target)>();
        if (root.Length > 0) parts.Add((root.TrimEnd('\\'), root.EndsWith('\\') ? root : root + "\\"));
        string walk = root;
        foreach (var segment in path.Substring(Math.Min(root.Length, path.Length)).Split('\\', StringSplitOptions.RemoveEmptyEntries)) {
            walk = System.IO.Path.Combine(walk, segment);
            parts.Add((segment, walk));
        }
        for (int i = 0; i < parts.Count; i++) {
            var (label, target) = parts[i];
            if (i > 0) { var sep = UI.Glyph("", 10); sep.Foreground = UI.Label; crumbs.Children.Add(sep); }
            var b = UI.Flat(label, 28, new CornerRadius(14)); b.Padding = new Thickness(M3.Pad3, 0, M3.Pad3, 0);
            b.Click += (_, _) => Active.Go(target);
            if (i == parts.Count - 1) b.Foreground = UI.Accent;
            crumbs.Children.Add(b);
        }
    }

    void BeginEdit() {
        addressBox.Text = Active.Location;
        crumbScroll.Visibility = Visibility.Collapsed; addressBox.Visibility = Visibility.Visible;
        addressBox.UpdateLayout(); addressBox.Focus(); addressBox.SelectAll();
    }

    void EndEdit() {
        if (addressBox.Visibility != Visibility.Visible) return;
        addressBox.Visibility = Visibility.Collapsed; crumbScroll.Visibility = Visibility.Visible;
    }

    // Environment variables are expanded, the disk is probed off the UI thread. A path that does not exist leaves the box in edit mode with a clear message.
    enum AddressKind { Folder, File, Shell, Invalid }

    static (AddressKind Kind, string Value) ResolveAddress(string text) {
        string value = Environment.ExpandEnvironmentVariables((text ?? "").Trim().Trim('"').Trim());
        if (value.Length == 0) return (AddressKind.Invalid, "");
        if (value.Equals(ThisPc.Label, StringComparison.OrdinalIgnoreCase) || value.Equals("This PC", StringComparison.OrdinalIgnoreCase) || ThisPc.Is(value)) return (AddressKind.Folder, ThisPc.Path);
        if (value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("::{", StringComparison.Ordinal)) return (AddressKind.Shell, value);
        try {
            if (value.Length == 2 && value[1] == ':' && char.IsAsciiLetter(value[0])) value += "\\";
            string full = System.IO.Path.GetFullPath(value);
            if (System.IO.Directory.Exists(full)) {
                string root = System.IO.Path.GetPathRoot(full) ?? "";
                if (full.Length > root.Length) full = full.TrimEnd('\\', '/');
                return (AddressKind.Folder, full);
            }
            if (System.IO.File.Exists(full)) return (AddressKind.File, full);
            return (AddressKind.Invalid, value);
        } catch { return (AddressKind.Invalid, value); }
    }

    async void SubmitAddress(string text) {
        if (string.IsNullOrWhiteSpace(text)) { EndEdit(); list.Focus(); return; }
        var tab = Active;
        try {
            var (kind, value) = await Task.Run(() => ResolveAddress(text));
            if (kind == AddressKind.Invalid) { Toast("Chemin introuvable : " + value); addressBox.Focus(); return; }
            EndEdit(); list.Focus();
            if (kind == AddressKind.Folder) tab.Go(value);
            else if (kind == AddressKind.File) Open(new FileItem(System.IO.Path.GetFileName(value), value, false, 0, DateTime.Now, ""));
            else PathRouter.LaunchExplorer(value);
        } catch (Exception ex) { App.CrashLog(ex); }
    }

    // Labels of the clickable segments, left to right (self-test).
    internal List<string> CrumbLabels() => crumbs.Children.OfType<Button>().Select(b => b.Content as string ?? "").ToList();

    // ---- sort ------------------------------------------------------------------------------------------------

    // One sort for every tab, saved in settings.json.
    internal void ApplySort(SortSpec spec) {
        foreach (var view in tabs.ToList()) view.Tab.SetSort(spec);
        FilesSettings.SaveSort(spec);
    }

    internal void ResetSort() {
        foreach (var view in tabs.ToList()) view.Tab.SetSort(SortSpec.Default);
        FilesSettings.ClearSort();
    }

    void ShowSortMenu() {
        var s = Active.Sort;
        var id = NativeMenu.Show(Hwnd, new NativeMenu.Item?[] {
            new(1, "Nom", s.Column == SortColumn.Name), new(2, "Date de modification", s.Column == SortColumn.Modified), new(3, "Type", s.Column == SortColumn.Type), new(4, "Taille", s.Column == SortColumn.Size),
            null, new(5, "Croissant", s.Ascending), new(6, "Décroissant", !s.Ascending),
            null, new(7, "Dossiers en premier", s.FoldersFirst),
            null, new(8, "Rétablir le tri par défaut") });
        switch (id) {
            case >= 1 and <= 4: ApplySort(s with { Column = (SortColumn)(id switch { 1 => SortColumn.Name, 2 => SortColumn.Modified, 3 => SortColumn.Type, _ => SortColumn.Size }) }); break;
            case 5: ApplySort(s with { Ascending = true }); break;
            case 6: ApplySort(s with { Ascending = false }); break;
            case 7: ApplySort(s with { FoldersFirst = !s.FoldersFirst }); break;
            case 8: ResetSort(); break;
        }
    }

    // Drive arrival / removal: the pinned targets are checked again (no polling).
    IntPtr DeviceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
        if (msg == 0x0219 && wParam.ToInt64() is 0x8000 or 0x8004 or 0x0007) sidebar.ScheduleRecheck();   // rebuilds the drive list and rechecks the pins; DBT_DEVICEARRIVAL, DBT_DEVICEREMOVECOMPLETE, DBT_DEVNODES_CHANGED
        return IntPtr.Zero;
    }

    void OpenPinnedFile(string path) => Open(new FileItem(System.IO.Path.GetFileName(path), path, false, 0, DateTime.Now, ""));

    // ---- opening / input -------------------------------------------------------------------------------------

    void OpenFromSidebar(string path, bool newTab) { if (newTab) AddTab(path); else Active.Go(path); }

    protected override void OnPreviewKeyDown(KeyEventArgs e) {
        base.OnPreviewKeyDown(e);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (HandleKey(key, Keyboard.Modifiers)) e.Handled = true;
    }

    // Every window shortcut (the self-test calls this with simulated keys).
    internal bool HandleKey(Key key, ModifierKeys mod) {
        bool ctrl = (mod & ModifierKeys.Control) != 0, alt = (mod & ModifierKeys.Alt) != 0, shift = (mod & ModifierKeys.Shift) != 0;
        bool editing = addressBox.IsKeyboardFocusWithin || renameBox.IsKeyboardFocusWithin || sidebar.Editing;
        bool typing = editing || filterBox.IsKeyboardFocusWithin;
        clickArm++;
        if (alt && key == Key.Left) Active.Back();
        else if (alt && key == Key.Right) Active.Forward();
        else if (alt && key == Key.Up) Active.Up();
        else if (alt && !ctrl && !shift && key == Key.Return && !editing) ShowProperties();
        else if (key == Key.Back && mod == ModifierKeys.None && !typing) Active.Up();
        else if ((ctrl && key == Key.L) || (!alt && key == Key.F4) || (alt && !ctrl && key == Key.D)) BeginEdit();
        else if (!typing && ctrl && shift && !alt && key == Key.C) CopyPaths(Selection());
        else if (ctrl && key == Key.T) AddTab(Active.Location);
        else if (ctrl && key == Key.W) CloseTab(activeView);
        else if (ctrl && key == Key.Tab) Cycle(shift ? -1 : 1);
        else if (ctrl && shift && key == Key.D1) SetView(ViewMode.Details);
        else if (ctrl && shift && key == Key.D2) SetView(ViewMode.Large);
        else if (ctrl && shift && key == Key.D3) SetView(ViewMode.Medium);
        else if (ctrl && shift && key == Key.D5) SetView(ViewMode.List);
        else if (ctrl && shift && key == Key.D6) SetView(ViewMode.Details);
        else if (!typing && ctrl && !shift && !alt && key == Key.Z) Fire(() => UndoAsync());
        else if (!typing && ((ctrl && !shift && !alt && key == Key.Y) || (ctrl && shift && !alt && key == Key.Z))) Fire(() => RedoAsync());
        else if (!typing && ctrl && !shift && !alt && key == Key.C) CopySelected(false);
        else if (!typing && ctrl && !shift && !alt && key == Key.X) CopySelected(true);
        else if (!typing && ctrl && !shift && !alt && key == Key.V) Fire(() => PasteAsync());
        else if (!typing && ctrl && !shift && !alt && key == Key.A) list.SelectAll();
        else if (!typing && ctrl && !shift && !alt && key == Key.Space) ToggleFocused();
        else if (!typing && ctrl && shift && key == Key.N) Fire(NewFolderAsync);
        else if (!editing && !ctrl && !alt && key == Key.F2) RenameSelected();
        else if (!typing && !ctrl && !alt && key == Key.Delete) Fire(() => DeleteSelectedAsync(shift));
        else if (!editing && !ctrl && !alt && shift && key == Key.F10) Dispatcher.BeginInvoke(() => ShowShellMenu(true, null), System.Windows.Threading.DispatcherPriority.Input);
        else if (!typing && !ctrl && !alt && !shift && key == Key.Apps) Dispatcher.BeginInvoke(() => ShowContextMenu(null), System.Windows.Threading.DispatcherPriority.Input);
        else if (!alt && ctrl && !shift && (key == Key.F || key == Key.E)) ShowFilter(null);
        else if (key == Key.Escape && !renameBox.IsKeyboardFocusWithin && !addressBox.IsKeyboardFocusWithin && (Active.DeepActive || Active.Searching || Active.Filter.Length > 0 || list.SelectedItems.Count > 0)) {
            if (Active.Searching || Active.DeepActive) { Active.CancelDeep(); if (Active.Filter.Length > 0) ClearFilter(true); UpdateStatus(); }
            else if (Active.Filter.Length > 0) ClearFilter(true);
            else list.UnselectAll();
        }
        else if (key == Key.F5) Active.Reload();
        else if (ctrl && key == Key.H) { FolderTab.ShowHidden = !FolderTab.ShowHidden; Active.Reload(); }
        else return false;
        return true;
    }

    // Ctrl+Space: toggles the item that has the keyboard focus (the last selected one if the focus is elsewhere), without touching the others.
    internal void ToggleFocused() {
        object? item = (Keyboard.FocusedElement as System.Windows.Controls.ListBoxItem)?.DataContext;
        if (item == null || !list.Items.Contains(item)) item = list.SelectedItem ?? (list.Items.Count > 0 ? list.Items[0] : null);
        if (item == null) return;
        if (list.SelectedItems.Contains(item)) list.SelectedItems.Remove(item); else list.SelectedItems.Add(item);
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e) {
        base.OnPreviewMouseDown(e);
        if (e.ChangedButton == MouseButton.XButton1) { Active.Back(); e.Handled = true; }
        else if (e.ChangedButton == MouseButton.XButton2) { Active.Forward(); e.Handled = true; }
    }

    sealed class Relay : ICommand {
        readonly Action run; public Relay(Action run) { this.run = run; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => run();
    }
}
