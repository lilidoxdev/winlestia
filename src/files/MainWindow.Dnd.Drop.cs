// Étape 4b : menu contextuel natif du shell (clic droit / touche Menu / Maj+F10) et glisser-déposer.
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




public sealed partial class MainWindow
{
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
