// Context menu adapted to what is under the pointer (background / file / folder / several items / drive), shortcuts shown on the right.
// "Afficher plus d'options" (and Shift+F10, Shift+right-click) opens the native shell menu (ShowShellMenu) with every shell extension.
// Also: Properties (Alt+Enter) and "Ouvrir avec...".
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace WinlestiaFiles;

internal enum CtxKind
{
    Background, File, Folder, Multi, Drive
}

public sealed partial class MainWindow
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool SHObjectProperties(IntPtr hwnd, uint type, string obj, string? page);
    [DllImport("shell32.dll")] static extern int SHMultiFileProperties(IntPtr dataObject, uint flags);

    // Test hooks: what Properties was asked for, and "do not open the native window".
    internal static bool PropertiesQuiet;
    internal static string LastProperties = "";
    internal static string LastOpenWith = "";

    const uint CxNewFolder = 1, CxPaste = 2, CxRefresh = 3, CxUndo = 4, CxRedo = 5, CxPath = 6, CxFolderProps = 7, CxMore = 8,
        CxOpen = 10, CxOpenWith = 11, CxCut = 12, CxCopy = 13, CxRename = 14, CxDelete = 15, CxProps = 16, CxNewTab = 17, CxPin = 18,
        CxSortBase = 20, CxViewBase = 40;

    static bool ClipboardHasFiles()
    {
        try
        {
            return Clipboard.ContainsFileDropList();
        }
        catch { return false; }
    }

    internal static CtxKind KindOf(IReadOnlyList<FileItem> sel, bool background) =>
        background || sel.Count == 0 ? CtxKind.Background : sel.Count > 1 ? CtxKind.Multi : sel[0].IsDir ? CtxKind.Folder : CtxKind.File;

    // The menu model (no UI): ids are the Cx* constants, "\tShortcut" text is right-aligned by the system.
    internal List<NativeMenu.Item?> ContextItems(CtxKind kind, IReadOnlyList<FileItem> sel, string? drive = null)
    {
        var items = new List<NativeMenu.Item?>();
        var s = Active.Sort;
        string undo = journal.NextUndo is { } u ? (u.Irreversible ? "Annuler (impossible : " + u.Why + ")" : "Annuler " + u.Label) : "Annuler";
        string redo = journal.NextRedo is { } r ? "Rétablir " + r.Label : "Rétablir";
        bool pinned = kind == CtxKind.Background ? QuickAccess.IsPinned(Active.Location) : sel.Count > 0 && sel.All(i => QuickAccess.IsPinned(i.FullPath));
        void Shell()
        {
            items.Add(null);
            items.Add(new(CxMore, "Afficher plus d'options\tMaj+F10"));
        }
        switch (drive != null ? CtxKind.Drive : kind)
        {
            case CtxKind.Drive:
                items.Add(new(CxOpen, "Ouvrir\tEntrée"));
                items.Add(new(CxNewTab, "Ouvrir dans un nouvel onglet\tCtrl+Entrée"));
                items.Add(null);
                items.Add(new(CxPath, "Copier le chemin"));
                items.Add(new(CxProps, "Propriétés\tAlt+Entrée"));
                Shell();
                break;
            case CtxKind.Background:
                items.Add(new(CxNewFolder, "Nouveau dossier\tCtrl+Maj+N"));
                items.Add(new(CxPaste, "Coller\tCtrl+V", false, !ClipboardHasFiles()));
                items.Add(null);
                items.Add(new(0, "Trier par", false, false, new NativeMenu.Item?[] {
                    new(CxSortBase + 0, "Nom", s.Column == SortColumn.Name), new(CxSortBase + 1, "Date de modification", s.Column == SortColumn.Modified),
                    new(CxSortBase + 2, "Type", s.Column == SortColumn.Type), new(CxSortBase + 3, "Taille", s.Column == SortColumn.Size), null,
                    new(CxSortBase + 4, "Croissant", s.Ascending), new(CxSortBase + 5, "Décroissant", !s.Ascending), null,
                    new(CxSortBase + 6, "Dossiers en premier", s.FoldersFirst), null, new(CxSortBase + 7, "Rétablir le tri par défaut") }));
                items.Add(new(0, "Affichage", false, false, new NativeMenu.Item?[] {
                    new(CxViewBase + 0, "Liste\tCtrl+Maj+5", view == ViewMode.List), new(CxViewBase + 1, "Détails\tCtrl+Maj+6", view == ViewMode.Details),
                    new(CxViewBase + 2, "Icônes moyennes\tCtrl+Maj+3", view == ViewMode.Medium), new(CxViewBase + 3, "Grandes icônes\tCtrl+Maj+2", view == ViewMode.Large) }));
                items.Add(new(CxRefresh, "Actualiser\tF5"));
                items.Add(null);
                items.Add(new(CxUndo, undo + "\tCtrl+Z", false, journal.NextUndo == null));
                items.Add(new(CxRedo, redo + "\tCtrl+Y", false, journal.NextRedo == null));
                items.Add(null);
                items.Add(new(CxPin, pinned ? "Désépingler de l'Accès rapide" : "Épingler dans Accès rapide"));
                items.Add(new(CxPath, "Copier le chemin\tCtrl+Maj+C"));
                items.Add(new(CxFolderProps, "Propriétés du dossier\tAlt+Entrée"));
                Shell();
                break;
            default:
                items.Add(new(CxOpen, "Ouvrir\tEntrée"));
                if (kind == CtxKind.Folder)
                    items.Add(new(CxNewTab, "Ouvrir dans un nouvel onglet\tCtrl+Entrée"));
                if (kind == CtxKind.File)
                    items.Add(new(CxOpenWith, "Ouvrir avec…"));   // never for several items
                items.Add(null);
                items.Add(new(CxCut, "Couper\tCtrl+X"));
                items.Add(new(CxCopy, "Copier\tCtrl+C"));
                if (kind != CtxKind.Multi)
                    items.Add(new(CxRename, "Renommer\tF2"));
                items.Add(new(CxDelete, "Supprimer\tSuppr"));
                items.Add(null);
                if (kind == CtxKind.Folder)
                    items.Add(new(CxPin, pinned ? "Désépingler de l'Accès rapide" : "Épingler dans Accès rapide"));
                items.Add(new(CxPath, "Copier le chemin\tCtrl+Maj+C"));
                items.Add(new(CxProps, "Propriétés\tAlt+Entrée"));
                Shell();
                break;
        }
        return items;
    }

    void ShowContextMenu(DependencyObject? source, string? drive = null)
    {
        if (renaming != null || activeView == null)
            return;
        var tab = Active;
        if (drive == null && (tab.Location.Length == 0 || ThisPc.Is(tab.Location) || tab.State is LoadState.Denied or LoadState.NotFound or LoadState.Failed))
            return;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (shift && drive == null)
        {
            ShowShellMenu(false, source);
            return;
        }
        bool keyboard = source == null;
        bool onItem = keyboard ? list.SelectedItems.Count > 0 : source != null && System.Windows.Controls.ItemsControl.ContainerFromElement(list, source) is System.Windows.Controls.ListBoxItem;
        if (!keyboard && !onItem)
            list.UnselectAll();
        var sel = onItem ? Selection() : new List<FileItem>();
        uint id = NativeMenu.Show(Hwnd, ContextItems(KindOf(sel, !onItem), sel, drive));
        if (id != 0)
            Fire(() => RunContext(id, sel, !onItem, drive));
    }

    // Executes a menu id (also what the self-test calls).
    internal async Task RunContext(uint id, IReadOnlyList<FileItem> sel, bool background, string? drive = null)
    {
        if (id >= CxSortBase && id < CxSortBase + 8)
        {
            var s = Active.Sort;
            switch (id - CxSortBase)
            {
                case 0:
                    ApplySort(s with
                    {
                        Column = SortColumn.Name
                    });
                    break;
                case 1:
                    ApplySort(s with
                    {
                        Column = SortColumn.Modified
                    });
                    break;
                case 2:
                    ApplySort(s with
                    {
                        Column = SortColumn.Type
                    });
                    break;
                case 3:
                    ApplySort(s with
                    {
                        Column = SortColumn.Size
                    });
                    break;
                case 4:
                    ApplySort(s with
                    {
                        Ascending = true
                    });
                    break;
                case 5:
                    ApplySort(s with
                    {
                        Ascending = false
                    });
                    break;
                case 6:
                    ApplySort(s with
                    {
                        FoldersFirst = !s.FoldersFirst
                    });
                    break;
                case 7:
                    ResetSort();
                    break;
            }
            return;
        }
        if (id >= CxViewBase && id < CxViewBase + 4)
        {
            SetView((ViewMode)(id - CxViewBase));
            return;
        }
        var paths = drive != null ? new List<string> { drive } : sel.Select(i => i.FullPath).ToList();
        switch (id)
        {
            case CxNewFolder:
                await NewFolderAsync();
                break;
            case CxPaste:
                await PasteAsync();
                break;
            case CxRefresh:
                Active.Reload();
                break;
            case CxUndo:
                await UndoAsync();
                break;
            case CxRedo:
                await RedoAsync();
                break;
            case CxPath:
                if (drive != null)
                {
                    try
                    {
                        Clipboard.SetText(drive);
                    }
                    catch (Exception ex)
                    {
                        App.LogLine("MainWindow.Menu.RunContext : " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
                else
                    CopyPaths(background ? new List<FileItem>() : sel.ToList());
                break;
            case CxFolderProps:
                ShowProperties(new List<string>());
                break;
            case CxProps:
                ShowProperties(paths);
                break;
            case CxMore:
                ShowShellMenu(!background, null);
                break;
            case CxOpen:
                if (drive != null)
                    Active.Go(drive);
                else
                    foreach (var item in sel)
                        Open(item, sel.Count > 1 && item.IsDir);
                break;
            case CxNewTab:
                foreach (var p in paths)
                    AddTab(p);
                break;
            case CxOpenWith:
                if (sel.Count == 1)
                    OpenWith(sel[0].FullPath);
                break;
            case CxCut:
                await SetClipboardAsync(paths, true);
                break;
            case CxCopy:
                await SetClipboardAsync(paths, false);
                break;
            case CxRename:
                if (sel.Count == 1)
                {
                    var one = sel[0];
                    await Dispatcher.InvokeAsync(() => BeginRename(one), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                break;
            case CxDelete:
                await DeleteAsync(sel.ToList(), false);
                break;
            case CxPin:
                TogglePin(background ? new List<string> { Active.Location } : paths);
                break;
        }
    }

    // ---- properties / open with ----------------------------------------------------------------------------------------

    // Nothing selected = the current folder; one path = its property sheet; several = the shell's multi-file sheet.
    internal void ShowProperties(IReadOnlyList<string>? paths = null)
    {
        paths ??= Selection().Select(i => i.FullPath).ToList();
        if (paths.Count == 0)
            paths = ThisPc.Is(Active.Location) ? new List<string>() : new List<string> { Active.Location };
        if (paths.Count == 0)
            return;
        LastProperties = paths.Count == 1 ? "single:" + paths[0] : "multi:" + paths.Count;
        if (PropertiesQuiet)
            return;
        try
        {
            if (paths.Count == 1)
            {
                SHObjectProperties(Hwnd, 2 /* SHOP_FILEPATH */, paths[0], null);
                return;
            }
            using var items = ShellItems.Of(paths);
            IntPtr data = items?.DataObject(Hwnd) ?? IntPtr.Zero;
            if (data == IntPtr.Zero)
            {
                Toast("Propriétés indisponibles pour cette sélection");
                return;
            }
            try
            {
                SHMultiFileProperties(data, 0);
            }
            finally { Marshal.Release(data); }
        }
        catch (Exception ex) { App.CrashLog(ex); }
    }

    internal void OpenWith(string path)
    {
        LastOpenWith = path;
        if (PropertiesQuiet)
            return;
        IntPtr owner = Hwnd;
        var thread = new Thread(() =>
        {
            try
            {
                var info = new OpenAsInfo { File = path, Class = null, Flags = 0x1 | 0x4 };
                SHOpenWithDialog(owner, ref info);
            }
            catch (Exception ex) { App.CrashLog(ex); }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }
}
