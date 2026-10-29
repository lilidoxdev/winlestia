// Step 4b: native shell context menu (right-click / Menu key / Shift+F10) and drag & drop.
//
// Drop target choice: we do NOT use WPF's AllowDrop route. WPF's own IDropTarget never calls IDropTargetHelper, so an incoming Explorer drag would show no drag image,
// and its DataObject wrapper hides the raw IDataObject. Instead the main HWND is registered with our own IDropTarget (RevokeDragDrop of WPF's first if it got there).
// OLE then calls us on the UI thread; we feed IDropTargetHelper (drag image), hit-test the WPF tree for the hovered folder row / tab / sidebar entry,
// and run the copy/move through FileOps. Outgoing drags use SHDoDragDrop with the shell's own data object (Explorer-style drag image).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PanneauxGlass;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace WinlestiaFiles;

[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTarget {
    [PreserveSig] int DragEnter(ComIDataObject data, int keyState, NPoint pt, ref int effect);
    [PreserveSig] int DragOver(int keyState, NPoint pt, ref int effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(ComIDataObject data, int keyState, NPoint pt, ref int effect);
}

[ComImport, Guid("4657278B-411B-11d2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTargetHelper {
    [PreserveSig] int DragEnter(IntPtr hwnd, ComIDataObject data, ref NPoint pt, int effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int DragOver(ref NPoint pt, int effect);
    [PreserveSig] int Drop(ComIDataObject data, ref NPoint pt, int effect);
    [PreserveSig] int Show([MarshalAs(UnmanagedType.Bool)] bool show);
}

// Thin COM shell around the window: the logic lives in MainWindow.Dnd.cs.
[ComVisible(true)]
internal sealed class OleDropTarget : IDropTarget {
    readonly MainWindow w;
    internal OleDropTarget(MainWindow w) { this.w = w; }
    public int DragEnter(ComIDataObject data, int keyState, NPoint pt, ref int effect) { try { effect = w.DndEnter(data, keyState, pt, effect); } catch (Exception ex) { App.CrashLog(ex); effect = 0; } return 0; }
    public int DragOver(int keyState, NPoint pt, ref int effect) { try { effect = w.DndOver(keyState, pt, effect, true); } catch (Exception ex) { App.CrashLog(ex); effect = 0; } return 0; }
    public int DragLeave() { try { w.DndLeave(); } catch (Exception ex) { App.CrashLog(ex); } return 0; }
    public int Drop(ComIDataObject data, int keyState, NPoint pt, ref int effect) { try { effect = w.DndDrop(data, keyState, pt, effect); } catch (Exception ex) { App.CrashLog(ex); effect = 0; } return 0; }
}

public sealed partial class MainWindow {
    [DllImport("ole32.dll")] static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll")] static extern int RegisterDragDrop(IntPtr hwnd, IDropTarget target);
    [DllImport("ole32.dll")] static extern int RevokeDragDrop(IntPtr hwnd);
    [DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    [DllImport("shell32.dll")] static extern int SHDoDragDrop(IntPtr hwnd, IntPtr dataObject, IntPtr dropSource, int okEffects, out int effect);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint DragQueryFileW(IntPtr hdrop, uint index, StringBuilder? buffer, uint cch);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out NPoint p);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetPropW(IntPtr hwnd, string name);

    const int DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2, MK_SHIFT = 4, MK_CONTROL = 8;
    const uint OwnTab = 1, OwnPath = 2, OwnExplorer = 3;
    static readonly Guid ClsidDragDropHelper = new("4657278A-411B-11d2-839A-00C04FD918D0");

    sealed record DropSpot(string Path, FileItem? Item, TabView? Tab, bool Side);

    OleDropTarget? dropTarget;
    ComIDataObject? dndData;
    string[] dndFiles = Array.Empty<string>();
    IDropTargetHelper? dndHelper;
    DropSpot? dndSpot;
    DispatcherTimer? tabTimer;
    FileItem? dragItem, swallowedPress;
    Point dragFrom;
    internal Task LastDrop = Task.CompletedTask;

    void WireDnd() {
        SourceInitialized += (_, _) => RegisterDrop();
        ContentRendered += (_, _) => { if (!OwnsDropTarget()) RegisterDrop(); };       // WPF may have registered its own in between
        Closed += (_, _) => { try { RevokeDragDrop(Hwnd); } catch { } };

        list.PreviewMouseLeftButtonDown += (_, e) => {
            dragItem = null; swallowedPress = null;
            if (e.ClickCount != 1 || Keyboard.Modifiers != ModifierKeys.None || renaming != null) return;
            if (e.OriginalSource is not DependencyObject src || ItemsControl.ContainerFromElement(list, src) is not ListBoxItem { DataContext: FileItem item }) return;
            dragItem = item; dragFrom = e.GetPosition(this);
            // Pressing inside a multi-selection must keep it (WPF would collapse it on press): selection is settled on release if no drag started.
            if (list.SelectedItems.Count > 1 && list.SelectedItems.Contains(item)) { swallowedPress = item; e.Handled = true; list.Focus(); }
        };
        list.PreviewMouseMove += (_, e) => {
            if (dragItem == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { dragItem = null; swallowedPress = null; return; }
            var d = e.GetPosition(this) - dragFrom;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragItem = null; swallowedPress = null;
            Dispatcher.BeginInvoke(StartDrag, DispatcherPriority.Input);      // out of the input event first
        };
        list.PreviewMouseLeftButtonUp += (_, _) => {
            if (swallowedPress != null) { var item = swallowedPress; list.Select(new[] { item }); }
            dragItem = null; swallowedPress = null;
        };
        list.PreviewMouseRightButtonUp += (_, e) => {
            var src = e.OriginalSource as DependencyObject;
            for (var d = src; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : null) if (d is ScrollBar) return;
            e.Handled = true;
            Dispatcher.BeginInvoke(() => ShowShellMenu(false, src), DispatcherPriority.Input);
        };
    }

    // ---- drop target registration ----------------------------------------------------------------------------

    internal bool OwnsDropTarget() {
        var p = GetPropW(Hwnd, "OleDropTargetInterface");
        if (p == IntPtr.Zero || dropTarget == null) return false;
        var mine = Marshal.GetComInterfaceForObject(dropTarget, typeof(IDropTarget));
        try { return mine == p; } finally { Marshal.Release(mine); }
    }

    void RegisterDrop() {
        var hwnd = Hwnd; if (hwnd == IntPtr.Zero) return;
        OleInitialize(IntPtr.Zero);
        dropTarget ??= new OleDropTarget(this);
        int hr = RegisterDragDrop(hwnd, dropTarget);
        if (hr == unchecked((int)0x80040101)) { RevokeDragDrop(hwnd); hr = RegisterDragDrop(hwnd, dropTarget); }   // DRAGDROP_E_ALREADYREGISTERED: WPF's own
        if (hr != 0) Perf.Log("RegisterDragDrop failed: 0x" + hr.ToString("X8"));
    }

    // ---- incoming drops (UI thread, called by OLE) -----------------------------------------------------------

    internal static string[] ReadFiles(ComIDataObject data) {
        var format = new FORMATETC { cfFormat = 15 /* CF_HDROP */, ptd = IntPtr.Zero, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        try {
            if (data.QueryGetData(ref format) != 0) return Array.Empty<string>();
            data.GetData(ref format, out var medium);
            try {
                uint n = DragQueryFileW(medium.unionmember, 0xFFFFFFFF, null, 0);
                var files = new string[n];
                for (uint i = 0; i < n; i++) {
                    uint len = DragQueryFileW(medium.unionmember, i, null, 0);
                    var sb = new StringBuilder((int)len + 1);
                    DragQueryFileW(medium.unionmember, i, sb, len + 1);
                    files[i] = sb.ToString();
                }
                return files;
            } finally { ReleaseStgMedium(ref medium); }
        } catch { return Array.Empty<string>(); }
    }

    internal int DndEnter(ComIDataObject data, int key, NPoint pt, int allowed) {
        DndReset();
        dndData = data; dndFiles = ReadFiles(data);
        try { dndHelper = (IDropTargetHelper)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidDragDropHelper)!)!; } catch { dndHelper = null; }
        try { dndHelper?.DragEnter(Hwnd, data, ref pt, allowed); } catch { }
        return DndOver(key, pt, allowed, false);
    }

    internal int DndOver(int key, NPoint pt, int allowed, bool helper) {
        var spot = dndFiles.Length == 0 ? null : SpotAt(pt);
        int effect = spot == null ? 0 : DecideEffect(dndFiles, spot.Path, key, allowed);
        SetHot(effect != 0 ? spot : null);
        if (helper) { try { dndHelper?.DragOver(ref pt, effect); } catch { } }
        return effect;
    }

    internal void DndLeave() {
        try { dndHelper?.DragLeave(); } catch { }
        DndReset();
    }

    internal int DndDrop(ComIDataObject data, int key, NPoint pt, int allowed) {
        var files = ReadFiles(data);
        dndFiles = files;
        var spot = files.Length == 0 ? null : SpotAt(pt);
        int effect = spot == null ? 0 : DecideEffect(files, spot.Path, key, allowed);
        try { dndHelper?.Drop(data, ref pt, effect); } catch { }
        DndReset();
        if (effect == 0 || spot == null) return 0;
        LastDrop = RunDrop(files, spot.Path, effect == DROPEFFECT_MOVE);
        // The move is performed here, by FileOps: report NONE so a source that deletes on MOVE (optimized-move convention) never removes anything itself.
        return effect == DROPEFFECT_MOVE ? 0 : effect;
    }

    void DndReset() {
        SetHot(null);
        if (dndHelper != null) { try { Marshal.FinalReleaseComObject(dndHelper); } catch { } dndHelper = null; }
        dndData = null; dndFiles = Array.Empty<string>();
    }

    async Task RunDrop(string[] files, string dest, bool move) {
        try { await DropFilesAsync(files, dest, move); } catch (Exception ex) { App.CrashLog(ex); }
    }

    // Copy / move into a folder; the list(s) showing an affected folder are patched in place.
    internal async Task<OpResult> DropFilesAsync(IReadOnlyList<string> files, string dest, bool move) {
        var watch = WatchSet(); watch.Add(Norm(dest));
        var result = await FileOps.Copy(Hwnd, files, dest, move, watch);
        ApplyResult(result, string.Equals(Norm(dest), Norm(Active.Location), StringComparison.OrdinalIgnoreCase));
        return result;
    }

    // Same volume -> move, else copy; Ctrl copies, Shift moves (Alt/link is not offered). Nothing for no-op drops (onto a source, into itself, move within the same folder).
    internal static int DecideEffect(string[] files, string dest, int key, int allowed) {
        if (files.Length == 0 || dest.Length == 0) return 0;
        string target = Norm(dest);
        foreach (var f in files) {
            string src = Norm(f);
            if (string.Equals(src, target, StringComparison.OrdinalIgnoreCase) || target.StartsWith(src + "\\", StringComparison.OrdinalIgnoreCase)) return 0;
        }
        bool sameVolume = string.Equals(Path.GetPathRoot(files[0]), Path.GetPathRoot(dest), StringComparison.OrdinalIgnoreCase);
        int want = (key & MK_CONTROL) != 0 ? DROPEFFECT_COPY : (key & MK_SHIFT) != 0 ? DROPEFFECT_MOVE : sameVolume ? DROPEFFECT_MOVE : DROPEFFECT_COPY;
        if (want == DROPEFFECT_MOVE && files.All(f => string.Equals(ParentOf(f), target, StringComparison.OrdinalIgnoreCase))) return 0;
        if ((allowed & want) == 0) want = (allowed & DROPEFFECT_COPY) != 0 ? DROPEFFECT_COPY : (allowed & DROPEFFECT_MOVE) != 0 ? DROPEFFECT_MOVE : 0;
        return want;
    }

    // What is under the cursor: a folder row/tile, a tab, a sidebar entry, or (anywhere else on the list card) the current folder.
    DropSpot? SpotAt(NPoint pt) {
        if (activeView == null) return null;
        var hit = InputHitTest(PointFromScreen(new Point(pt.X, pt.Y))) as DependencyObject;
        bool inList = false;
        for (var d = hit; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : (d is ContentElement ce ? ContentOperations.GetParent(ce) : null)) {
            if (d is ListBoxItem { DataContext: FileItem item }) {
                var sources = new HashSet<string>(dndFiles, StringComparer.OrdinalIgnoreCase);
                if (item.IsDir && !sources.Contains(item.FullPath)) return new DropSpot(item.FullPath, item, null, false);
                inList = true;
            }
            var tab = tabs.FirstOrDefault(t => ReferenceEquals(t.Pill, d));
            if (tab != null) return tab.Tab.Location.Length > 0 ? new DropSpot(tab.Tab.Location, null, tab, false) : null;
            if (sidebar.PathAt(d) is { } place) return new DropSpot(place, null, null, true);
            if (ReferenceEquals(d, content)) inList = true;
        }
        var cur = Active;
        if (!inList || cur.Location.Length == 0 || cur.State is LoadState.Denied or LoadState.NotFound or LoadState.Failed) return null;
        return new DropSpot(cur.Location, null, null, false);
    }

    void SetHot(DropSpot? spot) {
        if (Equals(spot, dndSpot)) return;
        var old = dndSpot; dndSpot = spot;
        if (old?.Item != null) old.Item.SetDrop(false);
        if (old?.Tab != null) { old.Tab.Drop = false; Paint(old.Tab); }
        if (old?.Side == true) sidebar.SetDrop(null);
        if (spot?.Item != null) spot.Item.SetDrop(true);
        if (spot?.Tab != null) { spot.Tab.Drop = true; Paint(spot.Tab); }
        if (spot?.Side == true) sidebar.SetDrop(spot.Path);
        // Hovering another tab for 600 ms switches to it: one-shot timer that only exists while hovering.
        tabTimer?.Stop(); tabTimer = null;
        if (spot?.Tab != null && spot.Tab != activeView) {
            var target = spot.Tab;
            var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(600) };
            timer.Tick += (_, _) => { timer.Stop(); if (ReferenceEquals(tabTimer, timer)) tabTimer = null; if (tabs.Contains(target)) Activate(target); };
            tabTimer = timer; timer.Start();
        }
    }

    // ---- outgoing drag ---------------------------------------------------------------------------------------

    void StartDrag() {
        if (renaming != null || (GetAsyncKeyState(0x01) & 0x8000) == 0) return;
        var paths = Selection().Select(i => i.FullPath).ToList();
        if (paths.Count == 0) return;
        Mouse.Capture(null);
        int hr, effect = 0;
        using (var items = ShellItems.Of(paths)) {
            if (items == null) return;
            var data = items.DataObject(Hwnd); if (data == IntPtr.Zero) return;
            try { hr = SHDoDragDrop(Hwnd, data, IntPtr.Zero, DROPEFFECT_COPY | DROPEFFECT_MOVE | 4, out effect); } finally { Marshal.Release(data); }
        }
        if (hr == 0x40100) Fire(() => AfterDrag(paths));        // DRAGDROP_S_DROP
    }

    // The target (Explorer, another folder, ...) did the work: whatever left its folder is removed from the list. Step 5 (watcher) will make this automatic.
    async Task AfterDrag(List<string> paths) {
        var gone = await Task.Run(() => paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList());
        if (gone.Count == 0) return;
        var result = new OpResult(); result.Removed.AddRange(gone);
        ApplyResult(result, false);
    }

    // ---- native context menu ---------------------------------------------------------------------------------

    internal sealed class MenuSession : IDisposable {
        internal ShellItems Items = null!; internal ShellMenu Menu = null!; internal List<FileItem> Sel = new(); internal bool Background;
        public void Dispose() { Menu.Dispose(); Items.Dispose(); }
    }

    MenuSession? BuildMenu(List<FileItem> sel, bool background, bool shift) {
        ShellMenu.InitMenuTheme(Theme.Current.IsDark);
        var items = background ? ShellItems.OfFolder(Active.Location) : ShellItems.Of(sel.Select(i => i.FullPath).ToList());
        if (items == null) return null;
        var menu = ShellMenu.Create(Hwnd, items, shift);
        if (menu == null) { items.Dispose(); return null; }
        var own = new List<(uint, string)>();
        if (!background && sel.All(i => i.IsDir)) own.Add((OwnTab, "Ouvrir dans un nouvel onglet"));
        own.Add((OwnPath, "Copier le chemin")); own.Add((OwnExplorer, "Ouvrir dans Explorer"));
        menu.Prepend(own);
        return new MenuSession { Items = items, Menu = menu, Sel = sel, Background = background };
    }

    void ShowShellMenu(bool keyboard, DependencyObject? source) {
        if (renaming != null || activeView == null) return;
        var tab = Active;
        if (tab.Location.Length == 0 || tab.State is LoadState.Denied or LoadState.NotFound or LoadState.Failed) return;
        bool onItem;
        if (keyboard) onItem = list.SelectedItems.Count > 0;
        else { onItem = source != null && ItemsControl.ContainerFromElement(list, source) is ListBoxItem; if (!onItem) list.UnselectAll(); }
        var sel = onItem ? Selection() : new List<FileItem>();
        NPoint at;
        if (keyboard) {
            var box = list.SelectedItem != null ? list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as FrameworkElement : null;
            var p = box != null ? box.PointToScreen(new Point(Math.Min(48, box.ActualWidth / 2), box.ActualHeight / 2)) : list.PointToScreen(new Point(48, 48));
            at = new NPoint((int)p.X, (int)p.Y);
        } else GetCursorPos(out at);
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        using var session = BuildMenu(sel, !onItem, shift);
        if (session == null) return;
        uint cmd = session.Menu.Track(Hwnd, at.X, at.Y);
        if (cmd != 0) Fire(() => ExecuteMenuCommand(session, cmd, at, shift));   // everything that needs the menu runs before the first await
    }

    // Verbs we handle ourselves for consistency with the rest of the window (inline rename, FileOps delete, cut-dimming clipboard, in-place new folder); the rest is invoked natively.
    internal Task ExecuteMenuCommand(MenuSession s, uint id, NPoint at, bool shift) {
        var sel = s.Sel;
        if (id < ShellMenu.First) {
            switch (id) {
                case OwnTab: foreach (var item in sel) AddTab(item.FullPath); break;
                case OwnPath: try { Clipboard.SetText(string.Join("\r\n", s.Background ? new[] { Active.Location } : sel.Select(i => i.FullPath))); } catch (Exception ex) { Perf.Log("copy path: " + ex.Message); } break;
                case OwnExplorer:
                    if (s.Background) PathRouter.LaunchExplorer(Active.Location);
                    else if (sel.Count == 1 && sel[0].IsDir) PathRouter.LaunchExplorer(sel[0].FullPath);
                    else PathRouter.LaunchExplorer("/select," + sel[0].FullPath);
                    break;
            }
            return Task.CompletedTask;
        }
        string verb = s.Menu.Verb(id).ToLowerInvariant();
        if (!s.Background && sel.Count > 0) {
            switch (verb) {
                case "open": if (sel.Count == 1) Open(sel[0]); else foreach (var item in sel) Open(item, item.IsDir); return Task.CompletedTask;
                case "rename" when sel.Count == 1: var one = sel[0]; Dispatcher.InvokeAsync(() => BeginRename(one), DispatcherPriority.Loaded); return Task.CompletedTask;
                case "delete": return DeleteAsync(sel, shift);
                case "copy": return SetClipboardAsync(sel.Select(i => i.FullPath).ToList(), false);
                case "cut": return SetClipboardAsync(sel.Select(i => i.FullPath).ToList(), true);
                case "paste" when sel.Count == 1 && sel[0].IsDir: return PasteAsync(sel[0].FullPath);
            }
        } else if (s.Background) {
            switch (verb) {
                case "paste": return PasteAsync();
                case "newfolder": return NewFolderAsync();
            }
        }
        bool ok = s.Menu.Invoke(Hwnd, id, at.X, at.Y, shift);
        if (ok && (s.Background || verb is "link" or "createshortcut" || verb.Contains("zip") || verb.Contains("extract") || verb.Contains("compress"))) return ReloadSoon();
        return Task.CompletedTask;
    }

    // Until the folder watcher of step 5: one refresh after a command that may create files (the shell finishes those asynchronously).
    async Task ReloadSoon() {
        var tab = Active;
        await Task.Delay(1200);
        if (ReferenceEquals(tab, Active)) tab.Reload();
    }
}
