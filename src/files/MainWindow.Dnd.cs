// Étape 4b : menu contextuel natif du shell (clic droit / touche Menu / Maj+F10) et glisser-déposer.
//
// Choix de la cible de dépôt : on N'utilise PAS la voie AllowDrop de WPF. L'IDropTarget propre à WPF n'appelle jamais
// IDropTargetHelper, donc un glisser entrant de l'Explorateur n'afficherait aucune image,
// et son enveloppe DataObject masque l'IDataObject brut. À la place, le HWND principal est enregistré avec notre propre IDropTarget
// (RevokeDragDrop de celui de WPF s'il est arrivé le premier).
// OLE nous appelle alors sur le thread UI ; nous alimentons IDropTargetHelper (image de glisser), testons la zone touchée dans l'arbre
// WPF pour trouver la ligne de dossier / l'onglet / l'entrée de la barre latérale survolé,
// et exécutons la copie/le déplacement via FileOps. Les glissers sortants utilisent SHDoDragDrop avec l'objet de données propre au
// shell (image de glisser à la manière de l'Explorateur).
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
internal interface IDropTarget
{
    [PreserveSig] int DragEnter(ComIDataObject data, int keyState, NPoint pt, ref int effect);
    [PreserveSig] int DragOver(int keyState, NPoint pt, ref int effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop(ComIDataObject data, int keyState, NPoint pt, ref int effect);
}

[ComImport, Guid("4657278B-411B-11d2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTargetHelper
{
    [PreserveSig] int DragEnter(IntPtr hwnd, ComIDataObject data, ref NPoint pt, int effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int DragOver(ref NPoint pt, int effect);
    [PreserveSig] int Drop(ComIDataObject data, ref NPoint pt, int effect);
    [PreserveSig] int Show([MarshalAs(UnmanagedType.Bool)] bool show);
}

// Fine enveloppe COM autour de la fenêtre : la logique vit dans MainWindow.Dnd.cs.
[ComVisible(true)]
internal sealed class OleDropTarget : IDropTarget
{
    readonly MainWindow w;
    internal OleDropTarget(MainWindow w)
    {
        this.w = w;
    }
    public int DragEnter(ComIDataObject data, int keyState, NPoint pt, ref int effect)
    {
        try
        {
            effect = w.DndEnter(data, keyState, pt, effect);
        }
        catch (Exception ex) { App.CrashLog(ex); effect = 0; }
        return 0;
    }
    public int DragOver(int keyState, NPoint pt, ref int effect)
    {
        try
        {
            effect = w.DndOver(keyState, pt, effect, true);
        }
        catch (Exception ex) { App.CrashLog(ex); effect = 0; }
        return 0;
    }
    public int DragLeave()
    {
        try
        {
            w.DndLeave();
        }
        catch (Exception ex) { App.CrashLog(ex); }
        return 0;
    }
    public int Drop(ComIDataObject data, int keyState, NPoint pt, ref int effect)
    {
        try
        {
            effect = w.DndDrop(data, keyState, pt, effect);
        }
        catch (Exception ex) { App.CrashLog(ex); effect = 0; }
        return 0;
    }
}

public sealed partial class MainWindow
{
    // Le shell termine ces créations de fichiers en asynchrone.
    const int ShellCreateMs = 1200;
    // Survol d'un onglet pendant un glisser : délai avant de basculer dessus.
    const int TabHoverMs = 600;
    [DllImport("ole32.dll")] static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll")] static extern int RegisterDragDrop(IntPtr hwnd, IDropTarget target);
    [DllImport("ole32.dll")] static extern int RevokeDragDrop(IntPtr hwnd);
    [DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    [DllImport("shell32.dll")] static extern int SHDoDragDrop(IntPtr hwnd, IntPtr dataObject, IntPtr dropSource, int okEffects,
        out int effect);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint DragQueryFileW(IntPtr hdrop, uint index,
        StringBuilder? buffer,
        uint cch);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out NPoint p);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetPropW(IntPtr hwnd, string name);

    const int DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2, MK_SHIFT = 4, MK_CONTROL = 8;
    const uint OwnTab = 1, OwnPath = 2, OwnExplorer = 3, OwnPin = 4, OwnResetSort = 5;
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

    void WireDnd()
    {
        SourceInitialized += (_, _) => RegisterDrop();
        ContentRendered += (_, _) => { if (!OwnsDropTarget()) RegisterDrop(); };       // WPF a pu enregistrer le sien entre-temps
        Closed += (_, _) => { Safe.Try(() => RevokeDragDrop(Hwnd)); };

        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            dragItem = null;
            swallowedPress = null;
            if (e.ClickCount != 1 || Keyboard.Modifiers != ModifierKeys.None || renaming != null)
                return;
            if (e.OriginalSource is not DependencyObject src || ItemsControl.ContainerFromElement(list, src) is not ListBoxItem {
                DataContext: FileItem item })
                return;
            dragItem = item;
            dragFrom = e.GetPosition(this);
            // Appuyer dans une multi-sélection doit la conserver (WPF la réduirait à l'appui) : la sélection est tranchée au
            // relâchement si aucun glisser n'a démarré.
            if (list.SelectedItems.Count > 1 && list.SelectedItems.Contains(item))
            {
                swallowedPress = item;
                e.Handled = true;
                list.Focus();
            }
        };
        list.PreviewMouseMove += (_, e) =>
        {
            if (dragItem == null)
                return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                dragItem = null;
                swallowedPress = null;
                return;
            }
            var d = e.GetPosition(this) - dragFrom;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            dragItem = null;
            swallowedPress = null;
            Dispatcher.BeginInvoke(StartDrag, DispatcherPriority.Input);      // d'abord hors de l'événement d'entrée
        };
        list.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (swallowedPress != null)
            {
                var item = swallowedPress;
                list.Select(new[] { item });
            }
            dragItem = null;
            swallowedPress = null;
        };
        list.PreviewMouseRightButtonUp += (_, e) =>
        {
            var src = e.OriginalSource as DependencyObject;
            for (var d = src; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : null)
                if (d is ScrollBar)
                    return;
            e.Handled = true;
            Dispatcher.BeginInvoke(() => ShowContextMenu(src), DispatcherPriority.Input);
        };
    }

    // ---- drop target registration ----------------------------------------------------------------------------

    internal bool OwnsDropTarget()
    {
        var p = GetPropW(Hwnd, "OleDropTargetInterface");
        if (p == IntPtr.Zero || dropTarget == null)
            return false;
        var mine = Marshal.GetComInterfaceForObject(dropTarget, typeof(IDropTarget));
        try
        {
            return mine == p;
        }
        finally { Marshal.Release(mine); }
    }

    void RegisterDrop()
    {
        var hwnd = Hwnd;
        if (hwnd == IntPtr.Zero)
            return;
        OleInitialize(IntPtr.Zero);
        dropTarget ??= new OleDropTarget(this);
        int hr = RegisterDragDrop(hwnd, dropTarget);
        if (hr == unchecked((int)0x80040101))
        {
            RevokeDragDrop(hwnd);
            hr = RegisterDragDrop(hwnd, dropTarget);
        }   // DRAGDROP_E_ALREADYREGISTERED: WPF's own
        if (hr != 0)
            Perf.Log("RegisterDragDrop failed: 0x" + hr.ToString("X8"));
    }

    // ---- dépôts entrants (thread UI, appelés par OLE) -----------------------------------------------------------

    internal static string[] ReadFiles(ComIDataObject data)
    {
        var format = new FORMATETC { cfFormat = 15 /* CF_HDROP */, ptd = IntPtr.Zero, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL };
        try
        {
            if (data.QueryGetData(ref format) != 0)
                return Array.Empty<string>();
            data.GetData(ref format, out var medium);
            try
            {
                uint n = DragQueryFileW(medium.unionmember, 0xFFFFFFFF, null, 0);
                var files = new string[n];
                for (uint i = 0; i < n; i++)
                {
                    uint len = DragQueryFileW(medium.unionmember, i, null, 0);
                    var sb = new StringBuilder((int)len + 1);
                    DragQueryFileW(medium.unionmember, i, sb, len + 1);
                    files[i] = sb.ToString();
                }
                return files;
            }
            finally { ReleaseStgMedium(ref medium); }
        }
        catch { return Array.Empty<string>(); }
    }

    internal int DndEnter(ComIDataObject data, int key, NPoint pt, int allowed)
    {
        DndReset();
        dndData = data;
        dndFiles = ReadFiles(data);
        try
        {
            dndHelper = (IDropTargetHelper)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidDragDropHelper)!)!;
        }
        catch { dndHelper = null; }
        try
        {
            dndHelper?.DragEnter(Hwnd, data, ref pt, allowed);
        }
        catch (Exception ex)
        {
            App.LogLine("MainWindow.Dnd.DndEnter : " + ex.GetType().Name + ": " + ex.Message);
        }
        return DndOver(key, pt, allowed, false);
    }

    internal int DndOver(int key, NPoint pt, int allowed, bool helper)
    {
        var spot = dndFiles.Length == 0 ? null : SpotAt(pt);
        int effect = spot == null ? 0 : DecideEffect(dndFiles, spot.Path, key, allowed);
        SetHot(effect != 0 ? spot : null);
        if (helper)
        {
            try
            {
                dndHelper?.DragOver(ref pt, effect);
            }
            catch (Exception)
            {
                // DragOver à chaque mouvement de souris : l'état est resynchronisé à l'appel suivant.
            }
        }
        return effect;
    }

    internal void DndLeave()
    {
        try
        {
            dndHelper?.DragLeave();
        }
        catch (Exception ex)
        {
            App.LogLine("MainWindow.Dnd.DndLeave : " + ex.GetType().Name + ": " + ex.Message);
        }
        DndReset();
    }

    internal int DndDrop(ComIDataObject data, int key, NPoint pt, int allowed)
    {
        var files = ReadFiles(data);
        dndFiles = files;
        var spot = files.Length == 0 ? null : SpotAt(pt);
        int effect = spot == null ? 0 : DecideEffect(files, spot.Path, key, allowed);
        try
        {
            dndHelper?.Drop(data, ref pt, effect);
        }
        catch (Exception ex)
        {
            App.LogLine("MainWindow.Dnd.DndDrop : " + ex.GetType().Name + ": " + ex.Message);
        }
        DndReset();
        if (effect == 0 || spot == null)
            return 0;
        LastDrop = RunDrop(files, spot.Path, effect == DROPEFFECT_MOVE);
        // Le déplacement est effectué ici, par FileOps : on annonce NONE pour qu'une source qui supprime sur MOVE (convention du
        // déplacement optimisé) ne retire jamais rien elle-même.
        return effect == DROPEFFECT_MOVE ? 0 : effect;
    }

    void DndReset()
    {
        SetHot(null);
        if (dndHelper != null)
        {
            Safe.Try(() => Marshal.FinalReleaseComObject(dndHelper));
            dndHelper = null;
        }
        dndData = null;
        dndFiles = Array.Empty<string>();
    }

    async Task RunDrop(string[] files, string dest, bool move)
    {
        try
        {
            await DropFilesAsync(files, dest, move);
        }
        catch (Exception ex) { App.CrashLog(ex); }
    }

    // Copie / déplacement vers un dossier ; les listes qui montrent un dossier concerné sont corrigées sur place.
    internal async Task<OpResult> DropFilesAsync(IReadOnlyList<string> files, string dest, bool move)
    {
        var watch = WatchSet();
        watch.Add(Norm(dest));
        var result = await FileOps.Copy(Hwnd, files, dest, move, watch);
        journal.Record(result);
        ApplyResult(result, string.Equals(Norm(dest), Norm(Active.Location), StringComparison.OrdinalIgnoreCase));
        return result;
    }

    // Même volume -> déplacement, sinon copie ; Ctrl copie, Maj déplace (Alt/lien n'est pas proposé). Rien pour les dépôts sans
    // effet (sur une source, dans soi-même, déplacement dans le même dossier).
    internal static int DecideEffect(string[] files, string dest, int key, int allowed)
    {
        if (files.Length == 0 || dest.Length == 0)
            return 0;
        string target = Norm(dest);
        foreach (var f in files)
        {
            string src = Norm(f);
            if (string.Equals(src, target, StringComparison.OrdinalIgnoreCase) || target.StartsWith(src + "\\",
                StringComparison.OrdinalIgnoreCase))
                return 0;
        }
        bool sameVolume = string.Equals(Path.GetPathRoot(files[0]), Path.GetPathRoot(dest), StringComparison.OrdinalIgnoreCase);
        int want = (key & MK_CONTROL) != 0 ? DROPEFFECT_COPY : (key & MK_SHIFT) != 0 ? DROPEFFECT_MOVE : sameVolume ? DROPEFFECT_MOVE
            : DROPEFFECT_COPY;
        if (want == DROPEFFECT_MOVE && files.All(f => string.Equals(ParentOf(f), target, StringComparison.OrdinalIgnoreCase)))
            return 0;
        if ((allowed & want) == 0)
            want = (allowed & DROPEFFECT_COPY) != 0 ? DROPEFFECT_COPY : (allowed & DROPEFFECT_MOVE) != 0 ? DROPEFFECT_MOVE : 0;
        return want;
    }

    // Ce qui est sous le curseur : une ligne/tuile de dossier, un onglet, une entrée de la barre latérale ou (partout ailleurs sur la
    // carte de liste) le dossier courant.
    DropSpot? SpotAt(NPoint pt)
    {
        if (activeView == null)
            return null;
        var hit = InputHitTest(PointFromScreen(new Point(pt.X, pt.Y))) as DependencyObject;
        bool inList = false;
        for (var d = hit; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d)
            : (d is ContentElement ce ? ContentOperations.GetParent(ce) : null))
        {
            if (d is ListBoxItem { DataContext: FileItem item })
            {
                var sources = new HashSet<string>(dndFiles, StringComparer.OrdinalIgnoreCase);
                if (item.IsDir && !sources.Contains(item.FullPath))
                    return new DropSpot(item.FullPath, item, null, false);
                inList = true;
            }
            var tab = tabs.FirstOrDefault(t => ReferenceEquals(t.Pill, d));
            if (tab != null)
                return tab.Tab.Location.Length > 0 ? new DropSpot(tab.Tab.Location, null, tab, false) : null;
            if (sidebar.PathAt(d) is { } place)
                return new DropSpot(place, null, null, true);
            if (ReferenceEquals(d, content))
                inList = true;
        }
        var cur = Active;
        if (!inList || cur.Location.Length == 0 || cur.State is LoadState.Denied or LoadState.NotFound or LoadState.Failed)
            return null;
        return new DropSpot(cur.Location, null, null, false);
    }

    void SetHot(DropSpot? spot)
    {
        if (Equals(spot, dndSpot))
            return;
        var old = dndSpot;
        dndSpot = spot;
        if (old?.Item != null)
            old.Item.SetDrop(false);
        if (old?.Tab != null)
        {
            old.Tab.Drop = false;
            Paint(old.Tab);
        }
        if (old?.Side == true)
            sidebar.SetDrop(null);
        if (spot?.Item != null)
            spot.Item.SetDrop(true);
        if (spot?.Tab != null)
        {
            spot.Tab.Drop = true;
            Paint(spot.Tab);
        }
        if (spot?.Side == true)
            sidebar.SetDrop(spot.Path);
        // Survoler un autre onglet pendant 600 ms bascule dessus : minuteur ponctuel qui n'existe que pendant le survol.
        tabTimer?.Stop();
        tabTimer = null;
        if (spot?.Tab != null && spot.Tab != activeView)
        {
            var target = spot.Tab;
            var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(TabHoverMs) };
            timer.Tick += (_, _) => { timer.Stop(); if (ReferenceEquals(tabTimer, timer)) tabTimer = null;
                if (tabs.Contains(target)) Activate(target); };
            tabTimer = timer;
            timer.Start();
        }
    }

    // ---- outgoing drag ---------------------------------------------------------------------------------------

    void StartDrag()
    {
        if (renaming != null || (GetAsyncKeyState(0x01) & 0x8000) == 0)
            return;
        var paths = Selection().Select(i => i.FullPath).ToList();
        if (paths.Count == 0)
            return;
        Mouse.Capture(null);
        int hr, effect = 0;
        using (var items = ShellItems.Of(paths))
        {
            if (items == null)
                return;
            var data = items.DataObject(Hwnd);
            if (data == IntPtr.Zero)
                return;
            try
            {
                hr = SHDoDragDrop(Hwnd, data, IntPtr.Zero, DROPEFFECT_COPY | DROPEFFECT_MOVE | 4, out effect);
            }
            finally { Marshal.Release(data); }
        }
        if (hr == 0x40100)
            Fire(() => AfterDrag(paths));        // DRAGDROP_S_DROP
    }

    // La cible (Explorateur, autre dossier, ...) a fait le travail : tout ce qui a quitté son dossier est retiré de la liste. L'étape 5
    // (watcher) rendra cela automatique.
    async Task AfterDrag(List<string> paths)
    {
        var gone = await Task.Run(() => paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList());
        if (gone.Count == 0)
            return;
        var result = new OpResult();
        result.Removed.AddRange(gone);
        ApplyResult(result, false);
    }

    // ---- native context menu ---------------------------------------------------------------------------------

    internal sealed class MenuSession : IDisposable
    {
        internal ShellItems Items = null!; internal ShellMenu Menu = null!; internal List<FileItem> Sel = new(); internal bool Background;
        public void Dispose()
        {
            Menu.Dispose();
            Items.Dispose();
        }
    }

    MenuSession? BuildMenu(List<FileItem> sel, bool background, bool shift)
    {
        ShellMenu.InitMenuTheme(Theme.Current.IsDark);
        var items = background ? ShellItems.OfFolder(Active.Location) : ShellItems.Of(sel.Select(i => i.FullPath).ToList());
        if (items == null)
            return null;
        var menu = ShellMenu.Create(Hwnd, items, shift);
        if (menu == null)
        {
            items.Dispose();
            return null;
        }
        var own = new List<(uint, string)>();
        if (!background && sel.All(i => i.IsDir))
            own.Add((OwnTab, "Ouvrir dans un nouvel onglet"));
        var pinTargets = background ? new List<string> { Active.Location } : sel.Select(i => i.FullPath).ToList();
        own.Add((OwnPin, pinTargets.Count > 0 && pinTargets.All(QuickAccess.IsPinned) ? "Désépingler" : "Épingler dans Accès rapide"));
        own.Add((OwnPath, "Copier le chemin d'accès\tCtrl+Maj+C"));
        own.Add((OwnExplorer, "Ouvrir dans Explorer"));
        if (background)
            own.Add((OwnResetSort, "Rétablir le tri par défaut"));
        menu.Prepend(own);
        return new MenuSession { Items = items, Menu = menu, Sel = sel, Background = background };
    }

    void ShowShellMenu(bool keyboard, DependencyObject? source)
    {
        if (renaming != null || activeView == null)
            return;
        var tab = Active;
        if (tab.Location.Length == 0 || tab.State is LoadState.Denied or LoadState.NotFound or LoadState.Failed)
            return;
        bool onItem;
        if (keyboard)
            onItem = list.SelectedItems.Count > 0;
        else
        {
            onItem = source != null && ItemsControl.ContainerFromElement(list, source) is ListBoxItem;
            if (!onItem)
                list.UnselectAll();
        }
        var sel = onItem ? Selection() : new List<FileItem>();
        NPoint at;
        if (keyboard)
        {
            var box = list.SelectedItem != null ? list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as FrameworkElement
                : null;
            var p = box != null ? box.PointToScreen(new Point(Math.Min(48, box.ActualWidth / 2), box.ActualHeight / 2))
                : list.PointToScreen(new Point(48, 48));
            at = new NPoint((int)p.X, (int)p.Y);
        }
        else
            GetCursorPos(out at);
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        using var session = BuildMenu(sel, !onItem, shift);
        if (session == null)
            return;
        uint cmd = session.Menu.Track(Hwnd, at.X, at.Y);
        if (cmd != 0)
            Fire(() => ExecuteMenuCommand(session, cmd, at, shift));   // tout ce qui a besoin du menu s'exécute avant le premier await
    }

    // Verbes que nous traitons nous-mêmes par cohérence avec le reste de la fenêtre (renommage en ligne, suppression FileOps,
    // presse-papiers qui atténue les éléments coupés, nouveau dossier sur place) ; le reste est invoqué nativement.
    internal Task ExecuteMenuCommand(MenuSession s, uint id, NPoint at, bool shift)
    {
        var sel = s.Sel;
        if (id < ShellMenu.First)
        {
            switch (id)
            {
                case OwnTab:
                    foreach (var item in sel)
                        AddTab(item.FullPath);
                    break;
                case OwnPath:
                    CopyPaths(s.Background ? new List<FileItem>() : sel);
                    break;
                case OwnPin:
                    TogglePin(s.Background ? new List<string> { Active.Location } : sel.Select(i => i.FullPath).ToList());
                    break;
                case OwnResetSort:
                    ResetSort();
                    break;
                case OwnExplorer:
                    if (s.Background)
                        PathRouter.LaunchExplorer(Active.Location);
                    else if (sel.Count == 1 && sel[0].IsDir)
                        PathRouter.LaunchExplorer(sel[0].FullPath);
                    else
                        PathRouter.LaunchExplorer("/select," + sel[0].FullPath);
                    break;
            }
            return Task.CompletedTask;
        }
        string verb = s.Menu.Verb(id).ToLowerInvariant();
        if (!s.Background && sel.Count > 0)
        {
            switch (verb)
            {
                case "open":
                    if (sel.Count == 1)
                        Open(sel[0]);
                    else
                        foreach (var item in sel)
                            Open(item, item.IsDir);
                    return Task.CompletedTask;
                case "rename" when sel.Count == 1:
                    var one = sel[0];
                    Dispatcher.InvokeAsync(() => BeginRename(one), DispatcherPriority.Loaded);
                    return Task.CompletedTask;
                case "delete":
                    return DeleteAsync(sel, shift);
                case "copy":
                    return SetClipboardAsync(sel.Select(i => i.FullPath).ToList(), false);
                case "cut":
                    return SetClipboardAsync(sel.Select(i => i.FullPath).ToList(), true);
                case "paste" when sel.Count == 1 && sel[0].IsDir:
                    return PasteAsync(sel[0].FullPath);
            }
        }
        else if (s.Background)
        {
            switch (verb)
            {
                case "paste":
                    return PasteAsync();
                case "newfolder":
                    return NewFolderAsync();
            }
        }
        bool ok = s.Menu.Invoke(Hwnd, id, at.X, at.Y, shift);
        if (ok && (s.Background || verb is "link" or "createshortcut" || verb.Contains("zip") || verb.Contains("extract")
            || verb.Contains("compress")))
            return ReloadSoon();
        return Task.CompletedTask;
    }

    // « Copier le chemin d'accès » : chemins complets, un par ligne, sans guillemets ; rien de sélectionné = le dossier courant.
    internal static string PathsText(IReadOnlyList<FileItem> sel, string folder) => string.Join("\r\n", sel.Count == 0 ? new[] { folder }
        : sel.Select(i => i.FullPath));

    internal void CopyPaths(IReadOnlyList<FileItem> sel)
    {
        if (renaming != null || activeView == null)
            return;
        string text = PathsText(sel, Active.Location);
        if (text.Length == 0)
            return;
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex) { Perf.Log("copy path: " + ex.Message); }
    }

    // Tous épinglés -> les désépingler, sinon épingler les manquants. Seule la liste d'accès rapide change.
    internal bool TogglePin(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return false;
        bool unpin = paths.All(QuickAccess.IsPinned);
        foreach (var p in paths)
        {
            if (unpin)
                QuickAccess.Unpin(p);
            else
                QuickAccess.Pin(p);
        }
        return !unpin;
    }

    // En attendant le watcher de dossier de l'étape 5 : un rafraîchissement après une commande susceptible de créer des fichiers (le
    // shell les termine en asynchrone).
    async Task ReloadSoon()
    {
        var tab = Active;
        await Task.Delay(ShellCreateMs);
        if (ReferenceEquals(tab, Active))
            tab.Reload();
    }
}
