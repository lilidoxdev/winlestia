// Step 4a: open, clipboard (CF_HDROP + Preferred DropEffect), rename (inline), delete, new folder, select all.
// Every disk-touching part runs in FileOps on a dedicated STA thread; the list is patched in place from the SUCCEEDED results.
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PanneauxGlass;

namespace WinlestiaFiles;

public sealed partial class MainWindow {
    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] static extern int SHOpenWithDialog(IntPtr parent, ref OpenAsInfo info);
    const int WM_CLIPBOARDUPDATE = 0x031D;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OpenAsInfo { public string File; public string? Class; public uint Flags; }

    readonly TextBox renameBox = new();
    readonly TextBlock renameError = UI.Text("", M3.FontSmall, UI.T(M3Role.Error));
    readonly StackPanel renamePanel = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    FileItem? renaming;
    int clickArm;
    (FileItem item, bool alone, bool onName)? pressed;
    uint cutSequence;
    HwndSource? clipSource;

    IntPtr Hwnd => new WindowInteropHelper(this).Handle;
    static string Norm(string path) => path.TrimEnd('\\');
    static string? ParentOf(string path) => Path.GetDirectoryName(path) is { } p ? Norm(p) : null;
    HashSet<string> WatchSet() => new(tabs.Select(t => Norm(t.Tab.Location)), StringComparer.OrdinalIgnoreCase);
    static async void Fire(Func<Task> work) { try { await work(); } catch (Exception ex) { App.CrashLog(ex); } }

    void WireOps() {
        renameBox.Template = RenameTemplate();
        renameBox.Height = double.NaN; renameBox.MinHeight = 28; renameBox.FontSize = M3.FontSmall; renameBox.FontFamily = M3.Body; renameBox.Foreground = UI.White; renameBox.CaretBrush = UI.White;
        renameBox.Padding = new Thickness(M3.Pad2, 2, M3.Pad2, 2); renameBox.VerticalContentAlignment = VerticalAlignment.Center;
        renameBox.BorderThickness = new Thickness(2); renameBox.BorderBrush = UI.T(M3Role.Primary); renameBox.Background = UI.T(M3Role.SurfaceHigh);
        renameBox.TextChanged += (_, _) => ValidateRename();
        renameBox.KeyDown += (_, e) => {
            if (e.Key == Key.Enter) { e.Handled = true; Fire(CommitRename); }
            else if (e.Key == Key.Escape) { e.Handled = true; CancelRename(); }
        };
        renameBox.LostKeyboardFocus += (_, _) => { if (renaming != null) Fire(CommitRename); };
        renameError.Margin = new Thickness(M3.Pad2, 2, 0, 0); renameError.Visibility = Visibility.Collapsed; renameError.TextWrapping = TextWrapping.Wrap;
        renamePanel.Children.Add(renameBox); renamePanel.Children.Add(renameError);
        Grid.SetRowSpan(renamePanel, 2); Panel.SetZIndex(renamePanel, 10);
        listHost.Children.Add(renamePanel);
        list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => { if (renaming != null && e.VerticalChange != 0 && !PlaceRename(renaming)) CancelRename(); }));

        list.PreviewMouseLeftButtonDown += (_, e) => {
            clickArm++; pressed = null;
            if (e.OriginalSource is not DependencyObject src || ItemsControl.ContainerFromElement(list, src) is not ListBoxItem { DataContext: FileItem item }) return;
            if (e.ClickCount == 1 && Keyboard.Modifiers == ModifierKeys.None)
                pressed = (item, list.SelectedItems.Count == 1 && ReferenceEquals(list.SelectedItem, item), e.OriginalSource is TextBlock t && t.Text == item.Name);
        };
        list.PreviewMouseLeftButtonUp += (_, _) => {
            if (pressed is not { alone: true, onName: true } p || list.SelectedItems.Count != 1 || !ReferenceEquals(list.SelectedItem, p.item)) return;
            Fire(() => SlowClick(p.item));
        };
        list.PreviewMouseDown += (_, e) => {
            if (e.ChangedButton != MouseButton.Middle) return;
            if (e.OriginalSource is DependencyObject src && ItemsControl.ContainerFromElement(list, src) is ListBoxItem { DataContext: FileItem { IsDir: true } dir }) { AddTab(dir.FullPath); e.Handled = true; }
        };
        SourceInitialized += (_, _) => {
            clipSource = HwndSource.FromHwnd(Hwnd);
            ShellMenu.InitMenuTheme(Theme.Current.IsDark);
            clipSource?.AddHook(ClipHook);
            AddClipboardFormatListener(Hwnd);
        };
        Closed += (_, _) => { if (clipSource != null) { RemoveClipboardFormatListener(clipSource.Handle); clipSource.RemoveHook(ClipHook); clipSource = null; } };
    }

    // The toolbar: no focus stealing, so the shortcuts keep working right after a click.
    FrameworkElement OpsBar() {
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        void Add(string glyph, string tip, Action action) {
            var b = UI.Flat(UI.Glyph(glyph, 16), 36, new CornerRadius(18)); b.Width = 36; b.ToolTip = tip; b.Focusable = false;
            b.Click += (_, _) => action();
            bar.Children.Add(b);
        }
        Add("", "Nouveau dossier (Ctrl+Maj+N)", () => Fire(NewFolderAsync));
        Add("", "Couper (Ctrl+X)", () => CopySelected(true));
        Add("", "Copier (Ctrl+C)", () => CopySelected(false));
        Add("", "Coller (Ctrl+V)", () => Fire(() => PasteAsync()));
        Add("", "Renommer (F2)", RenameSelected);
        Add("", "Supprimer (Suppr)", () => Fire(() => DeleteSelectedAsync(false)));
        return bar;
    }

    List<FileItem> Selection() => list.SelectedItems.Cast<FileItem>().ToList();

    // ---- open ------------------------------------------------------------------------------------------------

    void Open(FileItem item, bool newTab = false) {
        if (item.IsDir) { if (newTab) AddTab(item.FullPath); else Active.Go(item.FullPath); return; }
        string path = item.FullPath; IntPtr owner = Hwnd;
        // Default verb on a short-lived STA thread (a slow handler never blocks the UI). No association: the Windows "Open with" dialog.
        var thread = new Thread(() => {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 1155 or 31 or 1156) {
                try { var info = new OpenAsInfo { File = path, Class = null, Flags = 0x1 | 0x4 }; SHOpenWithDialog(owner, ref info); } catch (Exception inner) { App.CrashLog(inner); }
            } catch (Exception ex) { App.CrashLog(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    void OpenSelected(bool newTab) {
        var sel = Selection();
        if (sel.Count == 1) Open(sel[0], newTab);
        else foreach (var item in sel) Open(item, item.IsDir);
    }

    // ---- result patching -------------------------------------------------------------------------------------

    // Patches every tab that shows an affected folder, then selects the new items of the active one.
    void ApplyResult(OpResult r, bool select) {
        if (r.Error != null) Perf.Log("fileop: " + r.Error);
        foreach (var view in tabs.ToList()) {
            string where = Norm(view.Tab.Location);
            var removed = r.Removed.Where(p => string.Equals(ParentOf(p), where, StringComparison.OrdinalIgnoreCase)).ToList();
            var added = r.Added.Where(i => string.Equals(ParentOf(i.FullPath), where, StringComparison.OrdinalIgnoreCase)).ToList();
            if (removed.Count > 0 || added.Count > 0) view.Tab.ApplyChanges(removed, added);
        }
        if (!select) return;
        var shown = r.Added.Select(i => Active.Find(i.FullPath)).Where(i => i != null).Cast<FileItem>().ToList();
        if (shown.Count == 0) return;
        list.Select(shown);
        list.ScrollIntoView(shown[0]);
    }

    // ---- clipboard -------------------------------------------------------------------------------------------

    void CopySelected(bool cut) => Fire(() => CopySelectedAsync(cut));

    internal Task CopySelectedAsync(bool cut) {
        var sel = Selection();
        if (sel.Count == 0 || renaming != null) return Task.CompletedTask;
        return SetClipboardAsync(sel.Select(i => i.FullPath).ToList(), cut);
    }

    // WPF already retries OpenClipboard; if another process (clipboard history, a viewer) still holds it, retry here without blocking the UI.
    internal async Task<bool> SetClipboardAsync(IReadOnlyList<string> paths, bool cut) {
        var files = new StringCollection(); files.AddRange(paths.ToArray());
        for (int attempt = 0; ; attempt++) {
            var data = new DataObject();
            data.SetFileDropList(files);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 1)));
            try { Clipboard.SetDataObject(data, true); break; }
            catch (Exception ex) when (attempt < 4) { Perf.Log("clipboard set retry: " + ex.Message); await Task.Delay(100); }
            catch (Exception ex) { Perf.Log("clipboard set failed: " + ex.Message); return false; }
        }
        cutSequence = GetClipboardSequenceNumber();      // our own write; any later sequence means somebody else took the clipboard
        SetCut(cut ? new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return true;
    }

    void SetCut(HashSet<string> next) {
        var before = FileItem.CutPaths;
        if (before.Count == 0 && next.Count == 0) return;
        FileItem.CutPaths = next;
        foreach (var view in tabs) view.Tab.RaiseCut(before, next);
    }

    IntPtr ClipHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) {
        if (msg == WM_CLIPBOARDUPDATE && FileItem.CutPaths.Count > 0 && GetClipboardSequenceNumber() != cutSequence) SetCut(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return IntPtr.Zero;
    }

    // Reads CF_HDROP and Preferred DropEffect (what Explorer writes) and runs Copy or Move into the current (or given) folder.
    internal async Task<OpResult?> PasteAsync(string? destination = null) {
        string dest = destination ?? Active.Location;
        if (dest.Length == 0 || renaming != null) return null;
        string[] files; int effect = 1; uint sequence;
        try {
            var data = Clipboard.GetDataObject();
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] drop || drop.Length == 0) return null;
            files = drop;
            if (data.GetDataPresent("Preferred DropEffect") && data.GetData("Preferred DropEffect") is Stream s) { var b = new byte[4]; if (s.Read(b, 0, 4) == 4) effect = BitConverter.ToInt32(b, 0); }
            sequence = GetClipboardSequenceNumber();
        } catch (Exception ex) { Perf.Log("clipboard read failed: " + ex.Message); return null; }
        bool move = (effect & 2) != 0;
        var watch = WatchSet(); watch.Add(Norm(dest));
        var result = await FileOps.Copy(Hwnd, files, dest, move, watch);
        ApplyResult(result, true);
        if (move && !result.Aborted && result.Error == null && GetClipboardSequenceNumber() == sequence) {
            try { Clipboard.Clear(); } catch { }
            SetCut(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
        return result;
    }

    // ---- delete / new folder ---------------------------------------------------------------------------------

    internal async Task<OpResult?> DeleteAsync(IReadOnlyList<FileItem> items, bool permanent) {
        if (items.Count == 0) return null;
        if (permanent && !ConfirmDialog.Ask(this, "Supprimer définitivement ?",
            items.Count == 1 ? $"« {items[0].Name} » sera supprimé définitivement, sans passer par la Corbeille."
                             : $"{items.Count} éléments seront supprimés définitivement, sans passer par la Corbeille.", "Supprimer définitivement")) return null;
        var result = await FileOps.Delete(Hwnd, items.Select(i => i.FullPath).ToList(), permanent, WatchSet());
        ApplyResult(result, false);
        return result;
    }

    Task<OpResult?> DeleteSelectedAsync(bool permanent) => renaming != null ? Task.FromResult<OpResult?>(null) : DeleteAsync(Selection(), permanent);

    internal async Task<FileItem?> NewFolderAsync() {
        var tab = Active;
        if (tab.Location.Length == 0 || tab.State is LoadState.Denied or LoadState.NotFound or LoadState.Failed) return null;
        var watch = WatchSet();
        var result = await FileOps.NewFolder(Hwnd, tab.Location, "Nouveau dossier", watch);
        ApplyResult(result, true);
        var made = result.Added.FirstOrDefault(i => string.Equals(ParentOf(i.FullPath), Norm(tab.Location), StringComparison.OrdinalIgnoreCase));
        if (made == null || tab != Active) return null;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        BeginRename(made);
        return made;
    }

    // ---- rename ----------------------------------------------------------------------------------------------

    void RenameSelected() { if (list.SelectedItems.Count == 1 && list.SelectedItem is FileItem item) BeginRename(item); }

    async Task SlowClick(FileItem item) {
        int arm = ++clickArm;
        await Task.Delay((int)GetDoubleClickTime() + 120);      // one-shot; any click / key / double-click bumps clickArm and cancels it
        if (arm == clickArm && list.SelectedItems.Count == 1 && ReferenceEquals(list.SelectedItem, item)) BeginRename(item);
    }

    static TextBlock? FindText(DependencyObject root, string text) {
        for (int i = 0, n = VisualTreeHelper.GetChildrenCount(root); i < n; i++) {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock t && t.Text == text) return t;
            if (FindText(child, text) is { } found) return found;
        }
        return null;
    }

    // Overlay positioned on the name label of the realized container (rows and tiles alike).
    bool PlaceRename(FileItem item) {
        if (list.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem box || FindText(box, item.Name) is not { } label) return false;
        var cell = box.TransformToAncestor(listHost).Transform(new Point(0, 0));
        var text = label.TransformToAncestor(listHost).Transform(new Point(0, 0));
        if (gridMode) {
            renamePanel.Margin = new Thickness(cell.X + 4, text.Y - 6, 0, 0);
            renameBox.Width = box.ActualWidth - 8; renameBox.TextWrapping = TextWrapping.Wrap; renameBox.TextAlignment = TextAlignment.Center; renameBox.MaxHeight = 84;
        } else {
            renamePanel.Margin = new Thickness(text.X - 8, cell.Y + 2, 0, 0);
            renameBox.Width = Math.Max(label.ActualWidth + 8, 180); renameBox.TextWrapping = TextWrapping.NoWrap; renameBox.TextAlignment = TextAlignment.Left; renameBox.MaxHeight = 28;
        }
        return true;
    }

    internal void BeginRename(FileItem item) {
        if (renaming != null) CancelRename();
        clickArm++;
        list.ScrollIntoView(item); list.UpdateLayout();
        if (!PlaceRename(item)) return;
        renaming = item;
        renameBox.Text = item.Name; renameError.Visibility = Visibility.Collapsed; renameBox.BorderBrush = UI.T(M3Role.Primary);
        renamePanel.Visibility = Visibility.Visible; renamePanel.UpdateLayout();
        renameBox.Focus(); Keyboard.Focus(renameBox);
        int dot = item.IsDir ? -1 : item.Name.LastIndexOf('.');
        if (dot > 0) renameBox.Select(0, dot); else renameBox.SelectAll();
    }

    string? RenameProblem(string name) {
        if (name.Length == 0) return "Le nom ne peut pas être vide.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "Un nom ne peut pas contenir : \\ / : * ? \" < > |";
        if (name.EndsWith('.')) return "Un nom ne peut pas se terminer par un point.";
        if (renaming != null && !string.Equals(name, renaming.Name, StringComparison.OrdinalIgnoreCase) && Active.Find(Path.Combine(Active.Location, name)) != null) return "Ce nom existe déjà dans ce dossier.";
        return null;
    }

    bool ValidateRename() {
        if (renaming == null) return true;
        string? problem = RenameProblem(renameBox.Text.Trim());
        renameError.Text = problem ?? ""; renameError.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
        renameBox.BorderBrush = UI.T(problem == null ? M3Role.Primary : M3Role.Error);
        return problem == null;
    }

    void EndRename() {
        renaming = null;
        renamePanel.Visibility = Visibility.Collapsed;
        if (renameBox.IsKeyboardFocusWithin) list.Focus();
    }

    void CancelRename() { if (renaming != null) EndRename(); }

    internal async Task CommitRename() {
        var item = renaming; if (item == null) return;
        string name = renameBox.Text.Trim();
        if (name == item.Name) { EndRename(); return; }
        if (!ValidateRename()) { if (renameBox.IsKeyboardFocusWithin) return; EndRename(); return; }   // invalid + focus lost: drop the edit
        EndRename();
        var result = await FileOps.Rename(Hwnd, item.FullPath, name, WatchSet());
        ApplyResult(result, true);
    }

    // Same chrome as the address pill but with the small radius and a primary (or error) focus border.
    static ControlTemplate RenameTemplate() {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(M3.Small));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        host.SetValue(Control.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(host);
        return new ControlTemplate(typeof(TextBox)) { VisualTree = border };
    }

    // Hide / tab drop: nothing may stay armed.
    void EndOpsState() { CancelRename(); clickArm++; SetCut(new HashSet<string>(StringComparer.OrdinalIgnoreCase)); }
}

// ListBox with a public one-shot multi-select (a single selection notification, not one per item).
internal sealed class BulkListBox : ListBox {
    internal void Select(System.Collections.IEnumerable items) => SetSelectedItems(items);
}
