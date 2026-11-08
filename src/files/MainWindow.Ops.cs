// Step 4a: open, clipboard (CF_HDROP + Preferred DropEffect), rename (inline), delete, new folder, select all.
// Toute partie qui touche au disque s'exécute dans FileOps sur un thread STA dédié ; la liste est corrigée sur place à partir des
// résultats RÉUSSIS.
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

public sealed partial class MainWindow
{
    // Le presse-papiers est parfois verrouillé par une autre application : nouvel essai bref.
    const int ClipboardRetryMs = 100;
    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] static extern int SHOpenWithDialog(IntPtr parent,
        ref OpenAsInfo info);
    const int WM_CLIPBOARDUPDATE = 0x031D;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OpenAsInfo
    {
        public string File; public string? Class; public uint Flags;
    }

    readonly TextBox renameBox = new();
    readonly TextBlock renameError = UI.Text("", M3.FontSmall, UI.T(M3Role.Error));
    readonly StackPanel renamePanel = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        Visibility = Visibility.Collapsed };
    FileItem? renaming;
    int clickArm;
    (FileItem item, bool alone, bool onName)? pressed;
    uint cutSequence;
    HwndSource? clipSource;

    IntPtr Hwnd => new WindowInteropHelper(this).Handle;
    static string Norm(string path) => path.TrimEnd('\\');
    static string? ParentOf(string path) => Path.GetDirectoryName(path) is { } p ? Norm(p) : null;
    HashSet<string> WatchSet() => new(tabs.Select(t => Norm(t.Tab.Location)), StringComparer.OrdinalIgnoreCase);
    static async void Fire(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex) { App.CrashLog(ex); }
    }

    void WireOps()
    {
        journal.Owner = () => Hwnd;
        journal.Watch = WatchSet;
        renameBox.Template = RenameTemplate();
        renameBox.Height = double.NaN;
        renameBox.MinHeight = 28;
        renameBox.FontSize = M3.FontSmall;
        renameBox.FontFamily = M3.Body;
        renameBox.Foreground = UI.White;
        renameBox.CaretBrush = UI.White;
        renameBox.Padding = new Thickness(M3.Pad2, 2, M3.Pad2, 2);
        renameBox.VerticalContentAlignment = VerticalAlignment.Center;
        renameBox.BorderThickness = new Thickness(2);
        renameBox.BorderBrush = UI.T(M3Role.Primary);
        renameBox.Background = UI.T(M3Role.SurfaceHigh);
        renameBox.TextChanged += (_, _) => ValidateRename();
        renameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Fire(CommitRename);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelRename();
            }
        };
        renameBox.LostKeyboardFocus += (_, _) => { if (renaming != null) Fire(CommitRename); };
        renameError.Margin = new Thickness(M3.Pad2, 2, 0, 0);
        renameError.Visibility = Visibility.Collapsed;
        renameError.TextWrapping = TextWrapping.Wrap;
        renamePanel.Children.Add(renameBox);
        renamePanel.Children.Add(renameError);
        Grid.SetRowSpan(renamePanel, 2);
        Panel.SetZIndex(renamePanel, 10);
        listHost.Children.Add(renamePanel);
        list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => { if (renaming != null
            && e.VerticalChange != 0 && !PlaceRename(renaming)) CancelRename(); }));

        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            clickArm++;
            pressed = null;
            if (e.OriginalSource is not DependencyObject src || ItemsControl.ContainerFromElement(list, src) is not ListBoxItem {
                DataContext: FileItem item })
                return;
            if (e.ClickCount == 1 && Keyboard.Modifiers == ModifierKeys.None)
                pressed = (item, list.SelectedItems.Count == 1 && ReferenceEquals(list.SelectedItem, item), e.OriginalSource is TextBlock t
                    && t.Text == item.Name);
        };
        list.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (pressed is not { alone: true, onName: true } p || list.SelectedItems.Count != 1 || !ReferenceEquals(list.SelectedItem,
                p.item))
                return;
            Fire(() => SlowClick(p.item));
        };
        list.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle)
                return;
            if (e.OriginalSource is DependencyObject src && ItemsControl.ContainerFromElement(list, src) is ListBoxItem {
                DataContext: FileItem { IsDir: true } dir })
            {
                AddTab(dir.FullPath);
                e.Handled = true;
            }
        };
        SourceInitialized += (_, _) =>
        {
            clipSource = HwndSource.FromHwnd(Hwnd);
            ShellMenu.InitMenuTheme(Theme.Current.IsDark);
            clipSource?.AddHook(ClipHook);
            AddClipboardFormatListener(Hwnd);
        };
        Closed += (_, _) => { if (clipSource != null) { RemoveClipboardFormatListener(clipSource.Handle); clipSource.RemoveHook(ClipHook);
            clipSource = null; } };
    }

    // La barre d'outils : pas de vol de focus, pour que les raccourcis fonctionnent juste après un clic.
    FrameworkElement OpsBar()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal };
        void Add(string glyph, string tip, Action action)
        {
            var b = UI.Flat(UI.Glyph(glyph, 16), 36, new CornerRadius(18));
            b.Width = 36;
            b.ToolTip = tip;
            b.Focusable = false;
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

    void Open(FileItem item, bool newTab = false)
    {
        if (item.IsDir)
        {
            if (newTab)
                AddTab(item.FullPath);
            else
                Active.Go(item.FullPath);
            return;
        }
        string path = item.FullPath;
        IntPtr owner = Hwnd;
        // Verbe par défaut sur un thread STA éphémère (un gestionnaire lent ne bloque jamais l'UI). Sans association : la boîte «
        // Ouvrir avec » de Windows.
        var thread = new Thread(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode is 1155 or 31 or 1156)
            {
                try
                {
                    var info = new OpenAsInfo { File = path, Class = null, Flags = 0x1 | 0x4 };
                    SHOpenWithDialog(owner, ref info);
                }
                catch (Exception inner) { App.CrashLog(inner); }
            }
            catch (Exception ex) { App.CrashLog(ex); }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    void OpenSelected(bool newTab)
    {
        var sel = Selection();
        if (sel.Count == 1)
            Open(sel[0], newTab);
        else
            foreach (var item in sel)
                Open(item, item.IsDir);
    }

    // ---- result patching -------------------------------------------------------------------------------------

    // Corrige chaque onglet qui montre un dossier concerné, puis sélectionne les nouveaux éléments de l'onglet actif.
    void ApplyResult(OpResult r, bool select)
    {
        if (r.Error != null)
            Perf.Log("fileop: " + r.Error);
        foreach (var view in tabs.ToList())
        {
            string where = Norm(view.Tab.Location);
            var removed = r.Removed.Where(p => view.Tab.DeepActive || string.Equals(ParentOf(p), where,
                StringComparison.OrdinalIgnoreCase)).ToList();
            var added = r.Added.Where(i => string.Equals(ParentOf(i.FullPath), where, StringComparison.OrdinalIgnoreCase)).ToList();
            if (removed.Count > 0 || added.Count > 0)
                view.Tab.ApplyChanges(removed, added);
        }
        if (!select)
            return;
        var shown = r.Added.Select(i => Active.Find(i.FullPath)).Where(i => i != null).Cast<FileItem>().ToList();
        if (shown.Count == 0)
            return;
        list.Select(shown);
        list.ScrollIntoView(shown[0]);
    }

    // ---- clipboard -------------------------------------------------------------------------------------------

    void CopySelected(bool cut) => Fire(() => CopySelectedAsync(cut));

    internal Task CopySelectedAsync(bool cut)
    {
        var sel = Selection();
        if (sel.Count == 0 || renaming != null)
            return Task.CompletedTask;
        return SetClipboardAsync(sel.Select(i => i.FullPath).ToList(), cut);
    }

    // WPF réessaie déjà OpenClipboard ; si un autre processus (historique du presse-papiers, visionneuse) le retient encore, on
    // réessaie ici sans bloquer l'UI.
    internal async Task<bool> SetClipboardAsync(IReadOnlyList<string> paths, bool cut)
    {
        var files = new StringCollection();
        files.AddRange(paths.ToArray());
        for (int attempt = 0; ; attempt++)
        {
            var data = new DataObject();
            data.SetFileDropList(files);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 1)));
            try
            {
                Clipboard.SetDataObject(data, true);
                break;
            }
            catch (Exception ex) when (attempt < 4) { Perf.Log("clipboard set retry: " + ex.Message); await Task.Delay(ClipboardRetryMs); }
            catch (Exception ex) { Perf.Log("clipboard set failed: " + ex.Message); return false; }
        }
        // notre propre écriture ; toute séquence ultérieure signifie que quelqu'un d'autre a pris le presse-papiers
        cutSequence = GetClipboardSequenceNumber();
        SetCut(cut ? new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return true;
    }

    void SetCut(HashSet<string> next)
    {
        var before = FileItem.CutPaths;
        if (before.Count == 0 && next.Count == 0)
            return;
        FileItem.CutPaths = next;
        foreach (var view in tabs)
            view.Tab.RaiseCut(before, next);
    }

    IntPtr ClipHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE && FileItem.CutPaths.Count > 0 && GetClipboardSequenceNumber() != cutSequence)
            SetCut(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return IntPtr.Zero;
    }
}
