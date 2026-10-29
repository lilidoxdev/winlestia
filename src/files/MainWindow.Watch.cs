// Step 5: auto refresh (shell change notifications on the ACTIVE tab's folder only), filter field, selection / scroll preservation.
// No timer runs while idle: the only timers are one-shot (100 ms burst coalescing after an event, 120 ms filter debounce after a key press).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PanneauxGlass;

namespace WinlestiaFiles;

public sealed partial class MainWindow {
    // ---- shell change notifications ---------------------------------------------------------------------------

    const int WmChange = 0x8000 + 0x57;                                   // WM_APP + n
    const int RfInterrupt = 0x1, RfShell = 0x2, RfNewDelivery = 0x8000;
    // CREATE | DELETE | MKDIR | RMDIR | RENAMEITEM | RENAMEFOLDER | UPDATEITEM | UPDATEDIR | ATTRIBUTES
    const int Events = 0x2 | 0x4 | 0x8 | 0x10 | 0x1 | 0x20000 | 0x2000 | 0x1000 | 0x800;
    const int EvUpdateDir = 0x1000, EvRenameItem = 0x1, EvRenameFolder = 0x20000;
    const int MaxPendingEvents = 500;

    [StructLayout(LayoutKind.Sequential)] struct NotifyEntry { public IntPtr Pidl; [MarshalAs(UnmanagedType.Bool)] public bool Recursive; }
    [DllImport("shell32.dll")] static extern uint SHChangeNotifyRegister(IntPtr hwnd, int sources, int events, uint message, int count, ref NotifyEntry entries);
    [DllImport("shell32.dll")] static extern bool SHChangeNotifyDeregister(uint id);
    [DllImport("shell32.dll")] static extern IntPtr SHChangeNotification_Lock(IntPtr change, uint pid, out IntPtr pidls, out int events);
    [DllImport("shell32.dll")] static extern bool SHChangeNotification_Unlock(IntPtr handle);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, IntPtr bind, out IntPtr pidl, uint attrsIn, out uint attrsOut);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool SHGetPathFromIDListW(IntPtr pidl, StringBuilder path);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr pointer);

    static readonly bool NoWatch = Environment.GetEnvironmentVariable("WINLESTIA_FILES_NOWATCH") == "1";   // test isolation: the older ops/dnd selftests hold FileItem references that a refresh may replace
    uint watchId; string watchPath = "";
    bool hookAdded;
    readonly HashSet<string> changed = new(StringComparer.OrdinalIgnoreCase);
    bool changedFull;
    DispatcherTimer? burst;
    internal static int WatchEvents, WatchBatches;                      // diagnostics (selftest)

    void InitWatch() {
        SourceInitialized += (_, _) => AddHook();
        if (new WindowInteropHelper(this).Handle != IntPtr.Zero) AddHook();
    }

    void AddHook() {
        if (hookAdded) return;
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (source == null) return;
        source.AddHook(WatchHook); hookAdded = true;
        if (activeView != null) WatchFolder(Active.Location);
    }

    // Re-registers only when the watched folder differs (tab switch, navigation); one registration at a time.
    void WatchFolder(string path) {
        if (closed || !hookAdded || NoWatch) return;
        if (watchId != 0 && string.Equals(path, watchPath, StringComparison.OrdinalIgnoreCase)) return;
        StopWatch();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || path.Length == 0 || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || path.StartsWith("::", StringComparison.Ordinal)) return;
        IntPtr pidl = IntPtr.Zero;
        try {
            if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero) return;
            var entry = new NotifyEntry { Pidl = pidl, Recursive = false };
            watchId = SHChangeNotifyRegister(hwnd, RfInterrupt | RfShell | RfNewDelivery, Events, WmChange, 1, ref entry);
            watchPath = path;
        } catch (Exception ex) { App.CrashLog(ex); }
        finally { if (pidl != IntPtr.Zero) CoTaskMemFree(pidl); }
    }

    void StopWatch() {
        if (watchId != 0) { try { SHChangeNotifyDeregister(watchId); } catch { } watchId = 0; }
        watchPath = "";
        burst?.Stop(); changed.Clear(); changedFull = false;
    }

    static string? PathOf(IntPtr pidl) {
        if (pidl == IntPtr.Zero) return null;
        var sb = new StringBuilder(520);
        return SHGetPathFromIDListW(pidl, sb) ? sb.ToString() : null;
    }

    IntPtr WatchHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
        if (msg != WmChange) return IntPtr.Zero;
        handled = true;
        IntPtr lockHandle = IntPtr.Zero;
        try {
            lockHandle = SHChangeNotification_Lock(wParam, (uint)lParam.ToInt64(), out var pidls, out int ev);
            if (lockHandle == IntPtr.Zero || pidls == IntPtr.Zero || watchId == 0) return IntPtr.Zero;
            string? a = PathOf(Marshal.ReadIntPtr(pidls, 0)), b = PathOf(Marshal.ReadIntPtr(pidls, IntPtr.Size));
            WatchEvents++;
            if ((ev & EvUpdateDir) != 0 || (a == null && b == null)) changedFull = true;
            if (a != null && Under(a)) changed.Add(a);
            else if (a != null && string.Equals(a.TrimEnd('\\'), watchPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) changedFull = true;   // the folder itself changed
            if ((ev & (EvRenameItem | EvRenameFolder)) != 0 && b != null && Under(b)) changed.Add(b);
            if (changed.Count > MaxPendingEvents) { changed.Clear(); changedFull = true; }
            if (burst == null) { burst = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) }; burst.Tick += FlushChanges; }
            if (!burst.IsEnabled) burst.Start();
        } catch (Exception ex) { App.CrashLog(ex); }
        finally { if (lockHandle != IntPtr.Zero) SHChangeNotification_Unlock(lockHandle); }
        return IntPtr.Zero;
    }

    bool Under(string path) => string.Equals(Path.GetDirectoryName(path)?.TrimEnd('\\'), watchPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    void FlushChanges(object? sender, EventArgs e) {
        burst?.Stop();
        if (closed || activeView == null) { changed.Clear(); changedFull = false; return; }
        var paths = changed.ToList(); bool full = changedFull;
        changed.Clear(); changedFull = false;
        WatchBatches++;
        Active.Sync(paths, full || paths.Count == 0);
    }

    // ---- selection / scroll preservation --------------------------------------------------------------------------

    sealed class ViewState { internal HashSet<string> Selected = new(StringComparer.OrdinalIgnoreCase); internal string? TopPath; internal double Offset; }
    ViewState? mutation; int mutationDepth;

    ScrollViewer? ListScroller() {
        if (VisualTreeHelper.GetChildrenCount(list) == 0) return null;
        DependencyObject d = list;
        for (int guard = 0; guard < 6 && d != null; guard++) {
            if (d is ScrollViewer sv) return sv;
            d = VisualTreeHelper.GetChildrenCount(d) > 0 ? VisualTreeHelper.GetChild(d, 0) : null!;
        }
        return null;
    }

    int Cols() => gridMode ? Math.Max(1, (int)((ListScroller()?.ViewportWidth ?? TilePanel.CellW) / TilePanel.CellW)) : 1;

    ViewState Capture() {
        var s = new ViewState();
        foreach (FileItem item in list.SelectedItems) s.Selected.Add(item.FullPath);
        var sv = ListScroller();
        if (sv != null) {
            s.Offset = sv.VerticalOffset;
            int index = gridMode ? (int)(sv.VerticalOffset / TilePanel.CellH) * Cols() : (int)sv.VerticalOffset;
            if (index > 0 && index < list.Items.Count && list.Items[index] is FileItem top) s.TopPath = top.FullPath;
        }
        return s;
    }

    // keepScroll: anchor (the first visible row stays first when it still exists) or the raw offset.
    void Restore(ViewState s, Keep kind) {
        var items = Active.Items;
        if (s.Selected.Count > 0) {
            bool same = list.SelectedItems.Count == s.Selected.Count;
            if (same) foreach (FileItem it in list.SelectedItems) if (!s.Selected.Contains(it.FullPath)) { same = false; break; }
            if (!same) list.Select(items.Where(i => s.Selected.Contains(i.FullPath)).ToList());
        }
        if (kind == Keep.Selection || (s.Offset <= 0 && s.TopPath == null)) return;
        var sv = ListScroller(); if (sv == null) return;
        double target = s.Offset;
        if (kind == Keep.Anchor && s.TopPath != null) {
            int at = -1;
            for (int i = 0; i < items.Count; i++) if (string.Equals(items[i].FullPath, s.TopPath, StringComparison.OrdinalIgnoreCase)) { at = i; break; }
            if (at >= 0) target = gridMode ? (at / Cols()) * TilePanel.CellH : at;
        }
        if (Math.Abs(sv.VerticalOffset - target) < 0.5) return;
        list.UpdateLayout();                                               // the panel must know the new extent before the offset is clamped
        sv.ScrollToVerticalOffset(target);
    }

    void OnMutating(FolderTab tab, Keep? kind) {
        if (activeView == null || tab != Active || closed) return;
        if (kind != null) { if (mutationDepth++ == 0) mutation = Capture(); return; }
        if (--mutationDepth > 0 || mutation == null) return;
        var state = mutation; mutation = null; mutationDepth = 0;
        Restore(state, lastKeep);
    }
    Keep lastKeep;

    void Subscribe(FolderTab tab) {
        tab.Mutating += (t, k) => { if (k != null) lastKeep = k.Value; OnMutating(t, k); };
        tab.Notice += (_, message) => Toast(message);
    }

    // ---- tab switch: keep each tab's selection and scroll ---------------------------------------------------------

    readonly Dictionary<TabView, ViewState> saved = new();

    void SaveView(TabView? view) { if (view != null && view.Tab.State is LoadState.Ready) saved[view] = Capture(); }

    void RestoreView(TabView view) {
        view.Tab.SyncIfStale();
        if (!saved.Remove(view, out var s)) return;
        Dispatcher.BeginInvoke(() => { if (view == activeView && !closed) Restore(s, Keep.Anchor); }, DispatcherPriority.Loaded);
    }

    // ---- filter field -----------------------------------------------------------------------------------------------

    Border filterHost = null!; TextBox filterBox = null!; DispatcherTimer? filterTimer;
    bool settingFilter;

    FrameworkElement BuildFilter() {
        var icon = UI.Symbol("", "", 18); icon.Foreground = UI.Label; icon.Margin = new Thickness(M3.Sp2, 0, M3.Pad1, 0); icon.VerticalAlignment = VerticalAlignment.Center;
        filterBox = new TextBox { Width = 170, Height = double.NaN, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = UI.White, CaretBrush = UI.White,
            FontSize = M3.FontSmall, FontFamily = M3.Body, VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Template = AddressTemplate(), ToolTip = "Filtrer (Ctrl+F, Échap pour effacer)" };
        filterBox.TextChanged += (_, _) => {
            if (settingFilter) return;
            filterTimer ??= new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(120) };
            filterTimer.Tick -= ApplyFilter; filterTimer.Tick += ApplyFilter;
            filterTimer.Stop(); filterTimer.Start();                        // one-shot: ApplyFilter stops it
        };
        filterBox.PreviewKeyDown += (_, e) => {
            if (e.Key == Key.Escape) { ClearFilter(true); e.Handled = true; }
            else if (e.Key is Key.Down or Key.Enter) { list.Focus(); if (list.SelectedItem == null && list.Items.Count > 0) list.SelectedIndex = 0; e.Handled = true; }
        };
        filterBox.LostKeyboardFocus += (_, _) => { if (filterBox.Text.Length == 0) filterHost.Visibility = Visibility.Collapsed; };
        var close = UI.Flat(UI.Glyph("", 10), 24, new CornerRadius(12)); close.Width = 24; close.Margin = new Thickness(0, 0, M3.Pad1, 0); close.ToolTip = "Effacer le filtre (Échap)";
        close.Click += (_, _) => ClearFilter(true);
        var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(icon); row.Children.Add(filterBox); row.Children.Add(close);
        filterHost = new Border { Height = 36, CornerRadius = new CornerRadius(18), Background = UI.T(M3Role.OnSurface, 15), Child = row, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, M3.Sp2, 0) };
        return filterHost;
    }

    void ApplyFilter(object? sender, EventArgs e) {
        filterTimer?.Stop();
        if (closed || activeView == null) return;
        var t = Perf.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        Active.SetFilter(filterBox.Text);
        if (Perf.Enabled) Perf.Log($"filter '{filterBox.Text}': {Perf.Ms(t):F1} ms ({Active.Items.Count}/{Active.Total})");
    }

    void ShowFilter(string? seed) {
        filterHost.Visibility = Visibility.Visible;
        if (seed != null) { filterBox.Text = seed; }
        filterBox.Focus(); filterBox.CaretIndex = filterBox.Text.Length;
    }

    void ClearFilter(bool focusList) {
        filterTimer?.Stop();
        settingFilter = true; filterBox.Text = ""; settingFilter = false;
        filterHost.Visibility = Visibility.Collapsed;
        if (activeView != null) Active.SetFilter("");
        if (focusList) list.Focus();
    }

    // Called from Refresh: the field mirrors the active tab's filter (cleared by navigation, kept per tab on switch).
    void SyncFilterUi(FolderTab tab) {
        if (filterBox == null) return;
        if (filterTimer is { IsEnabled: true }) return;                     // the user is typing: the debounce will apply it
        if (filterBox.Text.Trim() == tab.Filter) return;
        settingFilter = true; filterBox.Text = tab.Filter; settingFilter = false;
        filterHost.Visibility = tab.Filter.Length > 0 || filterBox.IsKeyboardFocusWithin ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override void OnPreviewTextInput(TextCompositionEventArgs e) {
        base.OnPreviewTextInput(e);
        if (e.Handled || activeView == null || e.Text.Length == 0 || char.IsControl(e.Text[0])) return;
        if (!list.IsKeyboardFocusWithin || renameBox.IsKeyboardFocusWithin || addressBox.IsKeyboardFocusWithin) return;
        ShowFilter(e.Text); e.Handled = true;
    }

    FrameworkElement NoResult(FolderTab tab) =>
        UI.EmptyState("", "", "Aucun résultat", "Aucun élément ne correspond à « " + tab.Filter + " ».");
}
