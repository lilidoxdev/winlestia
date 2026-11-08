// Side card: "Accès rapide" (known folders) and "Lecteurs" (drives). Both lists are read on a pool thread
// (DriveInfo.IsReady peut bloquer sur un lecteur en veille ou réseau). L'emplacement actif est une pastille secondaryContainer.
// Les entrées d'accès rapide sont la liste épinglée de l'utilisateur (QuickAccess / settings.json) : réordonner, renommer, désépingler,
// état de cible cassée. La liste "Lecteurs" est inchangée.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PanneauxGlass;

namespace WinlestiaFiles;


internal sealed class Sidebar
{
    // Fusionne les rafales de messages d'arrivée de lecteur en une seule vérification.
    const int RecheckDebounceMs = 500;
    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token,
        out IntPtr path);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr pointer);
    static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");
    // Points de code Material Symbols / Segoe Fluent : dossier, fichier, avertissement.
    const string FolderMat = "\uE2C7", FolderFluent = "\uE8B7", FileMat = "\uE873", FileFluent = "\uE8A5", WarnMat = "\uE002",
        WarnFluent = "\uE7BA";

    sealed record Place(string Label, string Path, string Material, string Fluent);
    sealed record Drive(string Root, string Label, long Free, long Total);
    sealed class Entry
    {
        internal string Path = ""; internal Border Pill = null!; internal TextBlock Icon = null!; internal TextBlock Text = null!;
            internal bool Hot, Active, Drop;
        internal Grid Row = null!; internal StackPanel Texts = null!; internal bool Quick; internal TargetKind Kind;
            internal TextBlock? WarnIcon, FileIcon; internal TextBlock BaseIcon = null!;
    }

    readonly Action<string, bool> go;
    readonly Action<string>? openFile;
    readonly StackPanel host = new() { Margin = new Thickness(M3.Sp2) };
    readonly StackPanel quickHost = new();
    readonly StackPanel drivesHost = new();
    // Test hook: replaces DriveInfo.GetDrives() (Root, Label, Free, Total).
    internal static Func<List<(string Root, string Label, long Free, long Total)>>? DriveSource;
    // Clic droit sur un lecteur / l'entrée Ce PC, et "la liste des lecteurs a été reconstruite" (la page Ce PC se redessine).
    internal Action<string>? DriveMenu;
    internal event Action? DrivesChanged;
    internal int DrivesRebuilt;
    readonly List<Entry> entries = new();
    readonly Dictionary<string, Place> known = new(StringComparer.OrdinalIgnoreCase);
    readonly TaskCompletionSource ready = new();
    Entry? active;
    string current = "";
    int generation;
    long lastCheck;
    System.Windows.Threading.DispatcherTimer? recheckTimer;
    internal FrameworkElement Root
    {
        get;
    }
    internal Task Ready => ready.Task;

    internal Sidebar(Action<string, bool> go, Action<string>? openFile = null)
    {
        this.go = go;
        this.openFile = openFile;
        Root = new Border
        {
            Background = UI.Panel,
            CornerRadius = new CornerRadius(M3.Normal),
            BorderBrush = UI.Hairline,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = host }
        };
        Load();
    }

    // Reconstruit la section d'accès rapide depuis settings.json (l'auto-test s'en sert comme "redémarrage").
    internal void ReloadQuick()
    {
        QuickAccess.InitIfNew(known.Values.Select(p => p.Path));
        BuildQuick();
    }

    async void Load()
    {
        try
        {
            var (places, drives) = await Task.Run(Gather);
            foreach (var p in places)
                known[Key(p.Path)] = p;
            QuickAccess.InitIfNew(places.Select(p => p.Path));      // first launch: the folders shown so far, same order
            host.Children.Add(Header("Accès rapide", false));
            host.Children.Add(quickHost);
            BuildQuick();
            QuickAccess.Changed += BuildQuick;
            host.Children.Add(drivesHost);
            BuildDrives(drives);
            Highlight(current);
            ready.TrySetResult();
        }
        catch (Exception ex) { App.CrashLog(ex); }
    }


    // Reconstruite de zéro : arrivée / retrait d'un lecteur, changement d'étiquette ou d'espace libre.
    void BuildDrives(List<Drive> drives)
    {
        active = null;
        dropEntry = null;
        entries.RemoveAll(e => !e.Quick);
        drivesHost.Children.Clear();
        var pc = Add(ThisPc.Path, ThisPc.Label, null, "", "");
        drivesHost.Children.Add(Header("Ce PC", false));
        drivesHost.Children.Add(pc);
        if (drives.Count > 0)
            drivesHost.Children.Add(Header("Lecteurs", true));
        foreach (var d in drives)
        {
            string sub = d.Total > 0 ? $"{Size(d.Free)} libres sur {Size(d.Total)}" : "";
            string name = (d.Label.Length > 0 ? d.Label : "Disque local") + " (" + d.Root.TrimEnd('\\') + ")";
            drivesHost.Children.Add(Add(d.Root, name, sub, "", ""));
        }
        DrivesRebuilt++;
        Highlight(current);
        DrivesChanged?.Invoke();
    }

    // Arrivée / retrait d'un lecteur (WM_DEVICECHANGE) : la liste des lecteurs et l'état "introuvable" des entrées épinglées sont réévalués
    // ensemble.
    internal async Task DevicesChanged()
    {
        try
        {
            var drives = await Task.Run(GatherDrives);
            BuildDrives(drives);
            await Recheck();
        }
        catch (Exception ex) { App.CrashLog(ex); }
    }

    internal List<(string Path, string Label)> DriveView() => entries.Where(e => !e.Quick && !ThisPc.Is(e.Path)).Select(e => (e.Path,
        e.Text.Text)).ToList();

    static List<Drive> GatherDrives()
    {
        var drives = new List<Drive>();
        if (DriveSource != null)
        {
            foreach (var d in DriveSource())
                drives.Add(new Drive(d.Root, d.Label, d.Free, d.Total));
            return drives;
        }
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.IsReady)
                    drives.Add(new Drive(d.Name, d.VolumeLabel, d.AvailableFreeSpace, d.TotalSize));
                else
                    drives.Add(new Drive(d.Name, "", 0, 0));
            }
            catch { drives.Add(new Drive(d.Name, "", 0, 0)); }
        }
        return drives;
    }

    static (List<Place>, List<Drive>) Gather()
    {
        var places = new List<Place>();
        void Known(string label, string path, string material, string fluent)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                places.Add(new Place(label, path, material, fluent));
        }
        Known("Bureau", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "", "");
        Known("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "", "");
        Known("Téléchargements", DownloadsPath(), "", "");
        Known("Images", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "", "");
        Known("Musique", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "", "");
        Known("Vidéos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "", "");
        return (places, GatherDrives());
    }

    static string DownloadsPath()
    {
        try
        {
            if (SHGetKnownFolderPath(Downloads, 0, IntPtr.Zero, out var pointer) != 0)
                return "";
            try
            {
                return Marshal.PtrToStringUni(pointer) ?? "";
            }
            finally { CoTaskMemFree(pointer); }
        }
        catch { return ""; }
    }

    static string Size(long bytes) => bytes >= 1L << 40 ? (bytes / (double)(1L << 40)).ToString("0.#") + " To"
        : (bytes / (double)(1L << 30)).ToString("0") + " Go";

    static TextBlock Header(string text, bool gap)
    {
        var t = UI.Text(text, M3.FontSmall, UI.Label, true);
        t.Margin = new Thickness(M3.Sp2, gap ? M3.Sp4 : M3.Pad1, 0, M3.Pad1);
        return t;
    }

    static string Key(string path) => path.TrimEnd('\\', '/');

    // ---- quick access --------------------------------------------------------------------------------------------

    string DefaultLabel(string path)
    {
        if (known.TryGetValue(Key(path), out var place))
            return place.Label;
        string leaf = System.IO.Path.GetFileName(Key(path));
        return leaf.Length > 0 ? leaf : path;
    }

    void BuildQuick()
    {
        active = null;
        dropEntry = null;
        dragTarget = null;
        dragFrom = null;
        entries.RemoveAll(e => e.Quick);
        quickHost.Children.Clear();
        foreach (var q in QuickAccess.Entries.ToList())
        {
            known.TryGetValue(Key(q.Path), out var place);
            string label = !string.IsNullOrEmpty(q.Label) ? q.Label : DefaultLabel(q.Path);
            quickHost.Children.Add(Add(q.Path, label, null, place?.Material ?? FolderMat, place?.Fluent ?? FolderFluent, true));
        }
        if (quickHost.Children.Count == 0)
        {
            var hint = UI.Text("Clic droit sur un élément : Épingler dans Accès rapide", 11, UI.Label);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(M3.Sp2, M3.Pad1, M3.Sp2, M3.Pad1);
            quickHost.Children.Add(hint);
        }
        Highlight(current);
        _ = Recheck();
    }

    // Existence de chaque cible épinglée, hors du thread UI, chaque sonde avec un délai (un lecteur réseau déconnecté bloquerait
    // Directory.Exists). Pas de scrutation : appelé quand
    // la liste s'affiche, quand un lecteur arrive ou part (message de changement de périphérique de la fenêtre) et quand la fenêtre est
    // réactivée.
    internal async Task Recheck()
    {
        int gen = ++generation;
        lastCheck = Stopwatch.GetTimestamp();
        var targets = entries.Where(e => e.Quick).ToList();
        try
        {
            var kinds = await Task.WhenAll(targets.Select(e => QuickAccess.Probe(e.Path)));
            if (gen != generation)
                return;
            for (int i = 0; i < targets.Count; i++)
                if (entries.Contains(targets[i]) && targets[i].Kind != kinds[i])
                {
                    targets[i].Kind = kinds[i];
                    ApplyState(targets[i]);
                }
        }
        catch (Exception ex) { App.CrashLog(ex); }
    }

    // At most once every 3 s (window activation).
    internal void RecheckThrottled()
    {
        if (Stopwatch.GetTimestamp() - lastCheck > 3 * Stopwatch.Frequency)
            _ = Recheck();
    }

    // Arrivée / retrait de lecteur : les rafales de messages se fondent en une vérification (minuteur à usage unique, vivant seulement
    // pendant qu'un événement est en attente).
    internal void ScheduleRecheck()
    {
        recheckTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RecheckDebounceMs) };
        recheckTimer.Tick -= OnRecheckTick;
        recheckTimer.Tick += OnRecheckTick;
        recheckTimer.Stop();
        recheckTimer.Start();
    }
    void OnRecheckTick(object? sender, EventArgs e)
    {
        recheckTimer?.Stop();
        _ = DevicesChanged();
    }

    void SetIcon(Entry e, TextBlock icon)
    {
        if (ReferenceEquals(e.Icon, icon))
            return;
        e.Row.Children.Remove(e.Icon);
        e.Row.Children.Add(icon);
        e.Icon = icon;
    }

    // Cible cassée : étiquette grisée, icône d'avertissement du rôle d'erreur de la palette, info-bulle avec le chemin. Une cible fichier
    // reçoit l'icône de fichier.
    void ApplyState(Entry e)
    {
        if (e.Kind == TargetKind.Missing)
            SetIcon(e, e.WarnIcon ??= UI.Symbol(WarnMat, WarnFluent, 20));
        else if (e.Kind == TargetKind.File)
            SetIcon(e, e.FileIcon ??= UI.Symbol(FileMat, FileFluent, 20));
        else
            SetIcon(e, e.BaseIcon);
        e.Pill.ToolTip = e.Kind == TargetKind.Missing ? "Introuvable : " + e.Path : e.Path;
        Paint(e);
    }

    // Ce que la liste affiche, pour l'auto-test.
    internal List<(string Path, string Label, TargetKind Kind, string Tip)> QuickView() =>
        entries.Where(e => e.Quick).Select(e => (e.Path, e.Text.Text, e.Kind, e.Pill.ToolTip as string ?? "")).ToList();

    IntPtr Hwnd => Window.GetWindow(Root) is { } w ? new WindowInteropHelper(w).Handle : IntPtr.Zero;

    async void ActivateQuick(Entry e)
    {
        try
        {
            var kind = e.Kind == TargetKind.Folder ? TargetKind.Folder : await QuickAccess.Probe(e.Path);
            if (entries.Contains(e) && e.Kind != kind)
            {
                e.Kind = kind;
                ApplyState(e);
            }
            if (kind == TargetKind.Folder)
                go(e.Path, false);
            else if (kind == TargetKind.File)
                openFile?.Invoke(e.Path);
            else
                ShowBrokenMenu(e.Path);
        }
        catch (Exception ex) { App.CrashLog(ex); }
    }

    const uint CmdRelocate = 1, CmdRemove = 2, CmdUp = 3, CmdDown = 4, CmdRename = 5, CmdLocation = 6;

    void ShowBrokenMenu(string path)
    {
        uint id = NativeMenu.Show(Hwnd, new NativeMenu.Item?[] { new(CmdRemove, "Retirer le raccourci"), new(CmdRelocate,
            "Retrouver la cible…") });
        Perform(path, id);
    }

    void ShowMenu(Entry e)
    {
        string path = e.Path;
        int i = QuickAccess.IndexOf(path);
        if (i < 0)
            return;
        bool missing = e.Kind == TargetKind.Missing;
        int n = QuickAccess.Entries.Count;
        var items = new List<NativeMenu.Item?>();
        if (missing)
        {
            items.Add(new(CmdRemove, "Retirer le raccourci"));
            items.Add(new(CmdRelocate, "Retrouver la cible…"));
            items.Add(null);
        }
        items.Add(new(CmdUp, "Monter", false, i == 0));
        items.Add(new(CmdDown, "Descendre", false, i == n - 1));
        items.Add(new(CmdRename, "Renommer le libellé"));
        if (!missing)
            items.Add(new(CmdRemove, "Retirer de l'Accès rapide"));
        items.Add(new(CmdLocation, "Ouvrir l'emplacement", false, ParentOf(path) == null));
        Perform(path, NativeMenu.Show(Hwnd, items));
    }

    static string? ParentOf(string path)
    {
        try
        {
            return System.IO.Path.GetDirectoryName(Key(path));
        }
        catch { return null; }
    }

    // Chaque action modifie la liste (ou navigue) ; aucune ne touche la cible sur disque.
    void Perform(string path, uint id)
    {
        int i = QuickAccess.IndexOf(path);
        switch (id)
        {
            case CmdUp:
                QuickAccess.Move(i, -1);
                break;
            case CmdDown:
                QuickAccess.Move(i, 1);
                break;
            case CmdRemove:
                QuickAccess.RemoveAt(i);
                break;
            case CmdRename:
                BeginRenameLabel(path);
                break;
            case CmdRelocate:
                Relocate(path);
                break;
            case CmdLocation:
                if (ParentOf(path) is { } parent)
                    go(parent, false);
                break;
        }
    }

    // Boîte de dialogue dossier ou fichier (selon l'allure de l'ancien nom) dont le choix remplace le chemin, en gardant étiquette et
    // position. Déjà épinglé ailleurs : l'entrée cassée est supprimée à la place.
    void Relocate(string path)
    {
        int i = QuickAccess.IndexOf(path);
        if (i < 0)
            return;
        string ext = System.IO.Path.GetExtension(Key(path));
        bool file = ext.Length > 1 && ext.Skip(1).Any(char.IsLetter);
        string title = "Retrouver la cible de « " + DefaultLabel(path) + " »";
        string? chosen = null;
        var owner = Window.GetWindow(Root);
        if (file)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = title, CheckFileExists = true };
            if (d.ShowDialog(owner) == true)
                chosen = d.FileName;
        }
        else
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = title };
            if (d.ShowDialog(owner) == true)
                chosen = d.FolderName;
        }
        if (string.IsNullOrEmpty(chosen))
            return;
        int j = QuickAccess.IndexOf(chosen);
        if (j >= 0 && j != i)
            QuickAccess.RemoveAt(i);
        else
            QuickAccess.Replace(i, chosen);
    }

    // Éditeur d'étiquette en ligne : Entrée ou la perte du focus la conserve, Échap annule, vide = retour au nom par défaut.
    TextBox? labelBox;
    internal bool Editing => labelBox != null && labelBox.IsKeyboardFocusWithin;

    internal void BeginRenameLabel(string path)
    {
        var e = entries.FirstOrDefault(x => x.Quick && string.Equals(Key(x.Path), Key(path), StringComparison.OrdinalIgnoreCase));
        if (e == null)
            return;
        var box = new TextBox
        {
            Text = e.Text.Text,
            FontSize = M3.FontSmall,
            FontFamily = M3.Body,
            Foreground = UI.White,
            CaretBrush = UI.White,
            Background = UI.T(M3Role.SurfaceHigh),
            BorderBrush = UI.T(M3Role.Primary),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Height = 26,
            VerticalAlignment = VerticalAlignment.Center
        };
        e.Texts.Children.Remove(e.Text);
        e.Texts.Children.Insert(0, box);
        labelBox = box;
        bool done = false;
        void Finish(bool commit)
        {
            if (done)
                return;
            done = true;
            labelBox = null;
            string text = box.Text.Trim();
            e.Texts.Children.Remove(box);
            e.Texts.Children.Insert(0, e.Text);
            if (!commit)
                return;
            string? label = text.Length == 0 || text == DefaultLabel(path) ? null : text;
            int i = QuickAccess.IndexOf(path);
            if (i >= 0 && label != QuickAccess.Entries[i].Label)
                QuickAccess.SetLabel(i, label);
        }
        box.KeyDown += (_, k) => { if (k.Key == System.Windows.Input.Key.Enter) { Finish(true); k.Handled = true; }
            else if (k.Key == System.Windows.Input.Key.Escape) { Finish(false); k.Handled = true; } };
        box.LostKeyboardFocus += (_, _) => Finish(true);
        box.MouseDown += (_, m) => m.Handled = true;
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }

    // Réordonnancement au pointeur par capture (pas d'OLE : la cible de dépôt de la fenêtre reste intacte). Un appui sans déplacement est
    // un clic.
    Entry? dragFrom, dragTarget; Point dragStart; bool dragging;

    Entry? QuickAt(Point inHost)
    {
        var quick = entries.Where(x => x.Quick).ToList();
        if (quick.Count == 0)
            return null;
        foreach (var x in quick)
        {
            double top = x.Pill.TranslatePoint(new Point(0, 0), host).Y;
            if (inHost.Y >= top && inHost.Y < top + x.Pill.ActualHeight)
                return x;
        }
        double firstTop = quick[0].Pill.TranslatePoint(new Point(0, 0), host).Y;
        return inHost.Y < firstTop ? quick[0] : quick[^1];
    }

    void SetDragTarget(Entry? target)
    {
        if (ReferenceEquals(target, dragTarget))
            return;
        if (dragTarget != null)
        {
            dragTarget.Drop = false;
            Paint(dragTarget);
        }
        dragTarget = target;
        if (target != null)
        {
            target.Drop = true;
            Paint(target);
        }
    }

    void PointerDown(Entry e, MouseButtonEventArgs a)
    {
        dragFrom = e;
        dragStart = a.GetPosition(host);
        dragging = false;
        e.Pill.CaptureMouse();
    }

    void PointerMove(Entry e, MouseEventArgs a)
    {
        if (!ReferenceEquals(dragFrom, e) || a.LeftButton != MouseButtonState.Pressed)
            return;
        var pos = a.GetPosition(host);
        if (!dragging && Math.Abs(pos.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        dragging = true;
        SetDragTarget(QuickAt(pos));
    }

    void PointerUp(Entry e)
    {
        if (!ReferenceEquals(dragFrom, e))
            return;
        var target = dragTarget;
        bool moved = dragging;
        e.Pill.ReleaseMouseCapture();
        dragFrom = null;
        dragging = false;
        SetDragTarget(null);
        if (moved)
        {
            if (target != null && !ReferenceEquals(target, e))
                QuickAccess.MoveTo(QuickAccess.IndexOf(e.Path), QuickAccess.IndexOf(target.Path));
        }
        else if (e.Pill.IsMouseOver)
            ActivateQuick(e);
    }

    FrameworkElement Add(string path, string label, string? sub, string material, string fluent, bool quick = false)
    {
        var entry = new Entry { Path = path, Quick = quick };
        entry.Icon = entry.BaseIcon = UI.Symbol(material, fluent, 20);
        entry.Text = UI.Text(label, M3.FontSmall, UI.White);
        entry.Text.TextTrimming = TextTrimming.CharacterEllipsis;
        var texts = entry.Texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(entry.Text);
        if (!string.IsNullOrEmpty(sub))
        {
            var s = UI.Text(sub, 11, UI.Label);
            s.TextTrimming = TextTrimming.CharacterEllipsis;
            texts.Children.Add(s);
        }
        var grid = entry.Row = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(entry.Icon);
        grid.Children.Add(texts);
        entry.Pill = new Border
        {
            Child = grid,
            Height = sub == null ? 36 : 48,
            CornerRadius = new CornerRadius(sub == null ? 18 : 16),
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            SnapsToDevicePixels = true,
            Padding = new Thickness(M3.Sp2, 0, M3.Sp2, 0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = path,
            Margin = new Thickness(0, 1, 0, 1)
        };
        entry.Pill.MouseEnter += (_, _) => { entry.Hot = true; Paint(entry); };
        entry.Pill.MouseLeave += (_, _) => { entry.Hot = false; Paint(entry); };
        entry.Pill.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (!quick)
                    go(path, false);
            }
            else if (e.ChangedButton == MouseButton.Middle && (!quick || entry.Kind is TargetKind.Folder or TargetKind.Unknown))
                go(path, true);
        };
        if (quick)
        {
            entry.Pill.MouseLeftButtonDown += (_, e) => PointerDown(entry, e);
            entry.Pill.MouseMove += (_, e) => PointerMove(entry, e);
            entry.Pill.MouseLeftButtonUp += (_, _) => PointerUp(entry);
            entry.Pill.MouseRightButtonUp += (_, e) => { ShowMenu(entry); e.Handled = true; };
        }
        else
        {
            entry.Pill.MouseRightButtonUp += (_, e) => { DriveMenu?.Invoke(path); e.Handled = true; };
        }
        entries.Add(entry);
        return entry.Pill;
    }

    static void Paint(Entry e)
    {
        bool missing = e.Kind == TargetKind.Missing;
        e.Pill.Background = e.Drop ? UI.T(M3Role.PrimaryContainer) : e.Active ? UI.T(M3Role.PrimaryContainer) : e.Hot ? UI.Tint
            : Brushes.Transparent;
        var fg = e.Drop ? UI.T(M3Role.OnPrimaryContainer) : e.Active ? UI.T(M3Role.OnPrimaryContainer) : UI.White;
        // selected: visible even when the container barely differs from the panel
        e.Pill.BorderBrush = e.Active && !e.Drop ? UI.T(M3Role.Primary) : Brushes.Transparent;
        e.Text.Foreground = missing && !e.Active ? UI.Label : fg;
        e.Icon.Foreground = missing ? UI.T(M3Role.Error) : fg;
        e.Text.FontWeight = e.Active ? FontWeights.SemiBold : FontWeights.Normal;
        e.Text.FontStyle = missing ? FontStyles.Italic : FontStyles.Normal;
    }

    // Glisser-déposer : l'entrée sous un élément de l'arbre, et la surbrillance de dépôt. Les fichiers et les cibles cassées ne sont pas
    // des cibles de dépôt.
    internal string? PathAt(DependencyObject d)
    {
        foreach (var e in entries)
            if (ReferenceEquals(e.Pill, d))
                return e.Kind is TargetKind.File or TargetKind.Missing ? null : e.Path;
        return null;
    }
    Entry? dropEntry;
    internal void SetDrop(string? path)
    {
        if (dropEntry != null)
        {
            dropEntry.Drop = false;
            Paint(dropEntry);
            dropEntry = null;
        }
        if (path == null)
            return;
        foreach (var e in entries)
            if (e.Path == path)
            {
                dropEntry = e;
                e.Drop = true;
                Paint(e);
                break;
            }
    }

    static string Slash(string p) => p.EndsWith('\\') ? p : p + "\\";

    // Le plus long préfixe correspondant gagne : dans Documents\x la pastille Documents reste allumée, sinon le lecteur.
    internal void Highlight(string path)
    {
        current = path;
        Entry? best = null;
        int length = -1;
        string target = Slash(path);
        foreach (var e in entries)
        {
            string prefix = Slash(e.Path);
            if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && (prefix.Length > length || (prefix.Length == length
                && e.Quick)))
            {
                best = e;
                length = prefix.Length;
            }
        }
        if (ReferenceEquals(best, active))
            return;
        if (active != null)
        {
            active.Active = false;
            Paint(active);
        }
        active = best;
        if (active != null)
        {
            active.Active = true;
            Paint(active);
        }
    }
}
