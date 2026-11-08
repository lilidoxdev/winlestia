// Carte latérale : « Accès rapide » (dossiers connus) et « Lecteurs » (lecteurs). Les deux listes sont lues sur un thread du pool
// (DriveInfo.IsReady peut bloquer sur un lecteur en veille ou réseau). L'emplacement actif est une pastille secondaryContainer.
// Les entrées d'accès rapide sont la liste épinglée de l'utilisateur (QuickAccess / settings.json) : réordonner, renommer,
// désépingler, état de cible cassée. La liste "Lecteurs" est inchangée.
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


internal sealed partial class Sidebar
{
    // Fusionne les rafales de messages d'arrivée de lecteur en une seule vérification.
    const int RecheckDebounceMs = 500;
    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags,
        IntPtr token,
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
            // premier lancement : les dossiers affichés jusqu'ici, dans le même ordre
            QuickAccess.InitIfNew(places.Select(p => p.Path));
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

    // Arrivée / retrait d'un lecteur (WM_DEVICECHANGE) : la liste des lecteurs et l'état "introuvable" des entrées épinglées sont
    // réévalués ensemble.
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
        // sélectionné : visible même quand le conteneur diffère à peine du panneau
        e.Pill.BorderBrush = e.Active && !e.Drop ? UI.T(M3Role.Primary) : Brushes.Transparent;
        e.Text.Foreground = missing && !e.Active ? UI.Label : fg;
        e.Icon.Foreground = missing ? UI.T(M3Role.Error) : fg;
        e.Text.FontWeight = e.Active ? FontWeights.SemiBold : FontWeights.Normal;
        e.Text.FontStyle = missing ? FontStyles.Italic : FontStyles.Normal;
    }

    // Glisser-déposer : l'entrée sous un élément de l'arbre, et la surbrillance de dépôt. Les fichiers et les cibles cassées ne
    // sont pas des cibles de dépôt.
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
