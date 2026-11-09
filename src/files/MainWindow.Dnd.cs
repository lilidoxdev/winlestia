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
    [DllImport("shell32.dll")]
    static extern int SHDoDragDrop(IntPtr hwnd, IntPtr dataObject, IntPtr dropSource, int okEffects,
        out int effect);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint DragQueryFileW(IntPtr hdrop, uint index,
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
            if (e.OriginalSource is not DependencyObject src || ItemsControl.ContainerFromElement(list, src) is not ListBoxItem
                {
                    DataContext: FileItem item
                })
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
        var format = new FORMATETC
        {
            cfFormat = 15 /* CF_HDROP */,
            ptd = IntPtr.Zero,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL
        };
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
}
