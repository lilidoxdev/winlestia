// Side card: "Accès rapide" (known folders) and "Lecteurs" (drives). Both lists are read on a pool thread
// (DriveInfo.IsReady can block on a sleeping or network drive). The active location is a secondaryContainer pill.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PanneauxGlass;

namespace WinlestiaFiles;

internal sealed class Sidebar {
    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr pointer);
    static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    sealed record Place(string Label, string Path, string Material, string Fluent);
    sealed record Drive(string Root, string Label, long Free, long Total);
    sealed class Entry {
        internal string Path = ""; internal Border Pill = null!; internal TextBlock Icon = null!; internal TextBlock Text = null!; internal bool Hot, Active, Drop;
    }

    readonly Action<string, bool> go;
    readonly StackPanel host = new() { Margin = new Thickness(M3.Sp2) };
    readonly List<Entry> entries = new();
    Entry? active;
    string current = "";
    internal FrameworkElement Root { get; }

    internal Sidebar(Action<string, bool> go) {
        this.go = go;
        Root = new Border { Background = UI.Panel, CornerRadius = new CornerRadius(M3.Normal), BorderBrush = UI.Hairline, BorderThickness = new Thickness(1), ClipToBounds = true,
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = host } };
        Load();
    }

    async void Load() {
        try {
            var (places, drives) = await Task.Run(Gather);
            host.Children.Add(Header("Accès rapide", false));
            foreach (var p in places) host.Children.Add(Add(p.Path, p.Label, null, p.Material, p.Fluent));
            host.Children.Add(Header("Lecteurs", true));
            foreach (var d in drives) {
                string sub = d.Total > 0 ? $"{Size(d.Free)} libres sur {Size(d.Total)}" : "";
                string name = (d.Label.Length > 0 ? d.Label : "Disque local") + " (" + d.Root.TrimEnd('\\') + ")";
                host.Children.Add(Add(d.Root, name, sub, "", ""));
            }
            Highlight(current);
        } catch (Exception ex) { App.CrashLog(ex); }
    }

    static (List<Place>, List<Drive>) Gather() {
        var places = new List<Place>();
        void Known(string label, string path, string material, string fluent) { if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) places.Add(new Place(label, path, material, fluent)); }
        Known("Bureau", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "", "");
        Known("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "", "");
        Known("Téléchargements", DownloadsPath(), "", "");
        Known("Images", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "", "");
        Known("Musique", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "", "");
        Known("Vidéos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "", "");
        var drives = new List<Drive>();
        foreach (var d in DriveInfo.GetDrives()) {
            try {
                if (d.IsReady) drives.Add(new Drive(d.Name, d.VolumeLabel, d.AvailableFreeSpace, d.TotalSize));
                else drives.Add(new Drive(d.Name, "", 0, 0));
            } catch { drives.Add(new Drive(d.Name, "", 0, 0)); }
        }
        return (places, drives);
    }

    static string DownloadsPath() {
        try {
            if (SHGetKnownFolderPath(Downloads, 0, IntPtr.Zero, out var pointer) != 0) return "";
            try { return Marshal.PtrToStringUni(pointer) ?? ""; } finally { CoTaskMemFree(pointer); }
        } catch { return ""; }
    }

    static string Size(long bytes) => bytes >= 1L << 40 ? (bytes / (double)(1L << 40)).ToString("0.#") + " To" : (bytes / (double)(1L << 30)).ToString("0") + " Go";

    static TextBlock Header(string text, bool gap) {
        var t = UI.Text(text, M3.FontSmall, UI.Label, true);
        t.Margin = new Thickness(M3.Sp2, gap ? M3.Sp4 : M3.Pad1, 0, M3.Pad1);
        return t;
    }

    FrameworkElement Add(string path, string label, string? sub, string material, string fluent) {
        var entry = new Entry { Path = path };
        entry.Icon = UI.Symbol(material, fluent, 20);
        entry.Text = UI.Text(label, M3.FontSmall, UI.White);
        entry.Text.TextTrimming = TextTrimming.CharacterEllipsis;
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(entry.Text);
        if (!string.IsNullOrEmpty(sub)) { var s = UI.Text(sub, 11, UI.Label); s.TextTrimming = TextTrimming.CharacterEllipsis; texts.Children.Add(s); }
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(entry.Icon); grid.Children.Add(texts);
        entry.Pill = new Border { Child = grid, Height = sub == null ? 36 : 48, CornerRadius = new CornerRadius(sub == null ? 18 : 16), Padding = new Thickness(M3.Sp2, 0, M3.Sp2, 0),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = path, Margin = new Thickness(0, 1, 0, 1) };
        entry.Pill.MouseEnter += (_, _) => { entry.Hot = true; Paint(entry); };
        entry.Pill.MouseLeave += (_, _) => { entry.Hot = false; Paint(entry); };
        entry.Pill.MouseDown += (_, e) => {
            if (e.ChangedButton == MouseButton.Left) go(path, false);
            else if (e.ChangedButton == MouseButton.Middle) go(path, true);
        };
        entries.Add(entry);
        return entry.Pill;
    }

    static void Paint(Entry e) {
        e.Pill.Background = e.Drop ? UI.T(M3Role.PrimaryContainer) : e.Active ? UI.T(M3Role.PrimaryContainer) : e.Hot ? UI.Tint : Brushes.Transparent;
        var fg = e.Drop ? UI.T(M3Role.OnPrimaryContainer) : e.Active ? UI.T(M3Role.OnPrimaryContainer) : UI.White;
        e.Text.Foreground = fg; e.Icon.Foreground = fg;
    }

    // Drag & drop: the entry under an element of the tree, and the drop highlight.
    internal string? PathAt(DependencyObject d) { foreach (var e in entries) if (ReferenceEquals(e.Pill, d)) return e.Path; return null; }
    Entry? dropEntry;
    internal void SetDrop(string? path) {
        if (dropEntry != null) { dropEntry.Drop = false; Paint(dropEntry); dropEntry = null; }
        if (path == null) return;
        foreach (var e in entries) if (e.Path == path) { dropEntry = e; e.Drop = true; Paint(e); break; }
    }

    static string Slash(string p) => p.EndsWith('\\') ? p : p + "\\";

    // Longest matching prefix wins: inside Documents\x the Documents pill stays lit, otherwise the drive.
    internal void Highlight(string path) {
        current = path;
        Entry? best = null; int length = -1;
        string target = Slash(path);
        foreach (var e in entries) {
            string prefix = Slash(e.Path);
            if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && prefix.Length > length) { best = e; length = prefix.Length; }
        }
        if (ReferenceEquals(best, active)) return;
        if (active != null) { active.Active = false; Paint(active); }
        active = best;
        if (active != null) { active.Active = true; Paint(active); }
    }
}
