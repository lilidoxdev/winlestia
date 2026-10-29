// Item model, one-shot-notification collection and the in-memory sort (never re-enumerates the disk).
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace WinlestiaFiles;

internal enum SortColumn { Name, Modified, Type, Size }
internal sealed record SortSpec(SortColumn Column, bool Ascending);

public sealed class FileItem : INotifyPropertyChanged {
    // Paths currently cut to the clipboard (UI thread only): read when a container binds, raised per item by RaiseCut().
    internal static HashSet<string> CutPaths = new(StringComparer.OrdinalIgnoreCase);
    public event PropertyChangedEventHandler? PropertyChanged;
    public double CutOpacity => CutPaths.Count > 0 && CutPaths.Contains(FullPath) ? 0.5 : 1.0;
    internal void RaiseCut() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CutOpacity)));
    // Drag-over highlight (a folder row / tile that would receive the drop).
    public bool DropHot { get; private set; }
    internal void SetDrop(bool on) { if (DropHot == on) return; DropHot = on; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropHot))); }
    // Set once by the window: Material Symbols code points when the font ships, Segoe Fluent otherwise.
    internal static bool UseMaterial;
    internal FileItem(string name, string fullPath, bool isDir, long size, DateTime modified, string type) {
        Name = name; FullPath = fullPath; IsDir = isDir; Size = size; Modified = modified; TypeLabel = type;
    }
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDir { get; }
    public long Size { get; }
    public DateTime Modified { get; }
    public string TypeLabel { get; }
    public string Icon => IsDir ? (UseMaterial ? "" : "") : (UseMaterial ? "" : "");
    public string ModifiedText => Modified.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    public string SizeText => IsDir ? "" : Size < 1024 ? Size + " o" : Size < 1024 * 1024 ? Math.Ceiling(Size / 1024.0) + " Ko"
        : Size < 1024L * 1024 * 1024 ? (Size / 1048576.0).ToString("0.#", CultureInfo.InvariantCulture) + " Mo" : (Size / 1073741824.0).ToString("0.##", CultureInfo.InvariantCulture) + " Go";

    // Stats one path (worker thread, one find-data read); null when it vanished.
    internal static FileItem? Probe(string path, Dictionary<string, string> types) {
        try {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.Exists ? From(info, types) : null;
        } catch { return null; }
    }

    // Called on the enumeration thread: FileSystemInfo from an enumeration already carries the find data (no extra I/O).
    internal static FileItem From(FileSystemInfo info, Dictionary<string, string> types) {
        bool dir = (info.Attributes & FileAttributes.Directory) != 0;
        if (dir) return new FileItem(info.Name, info.FullName, true, 0, info.LastWriteTime, "Dossier");
        string ext = Path.GetExtension(info.Name);
        if (!types.TryGetValue(ext, out var type)) types[ext] = type = ext.Length > 1 ? "Fichier " + ext.Substring(1).ToUpperInvariant() : "Fichier";
        return new FileItem(info.Name, info.FullName, false, info is FileInfo f ? f.Length : 0, info.LastWriteTime, type);
    }
}

// Batch changes raise a single Reset (WPF's list collection view rejects multi-item Add notifications).
internal sealed class RangeObservableCollection<T> : ObservableCollection<T> {
    internal void AddRange(IEnumerable<T> items) { CheckReentrancy(); ((List<T>)Items).AddRange(items); Notify(); }
    internal static int Resets;                          // diagnostics: how many whole-list rewrites happened (the refresh selftest asserts none for small changes)
    internal void Reset(IEnumerable<T>? items) {
        CheckReentrancy(); Resets++;
        var list = (List<T>)Items;
        list.Clear();
        if (items != null) list.AddRange(items);
        Notify();
    }
    void Notify() {
        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

internal static class FileSort {
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int StrCmpLogicalW(string a, string b);

    // Folders first whatever the column and direction, natural name order as the tie-break (same as Explorer).
    internal static Comparison<FileItem> Comparer(SortSpec spec) {
        int sign = spec.Ascending ? 1 : -1;
        return (a, b) => {
            if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
            int c = spec.Column switch {
                SortColumn.Modified => a.Modified.CompareTo(b.Modified),
                SortColumn.Type => string.Compare(a.TypeLabel, b.TypeLabel, StringComparison.OrdinalIgnoreCase),
                SortColumn.Size => a.Size.CompareTo(b.Size),
                _ => StrCmpLogicalW(a.Name, b.Name) };
            return c != 0 ? c * sign : StrCmpLogicalW(a.Name, b.Name);
        };
    }

    internal static List<FileItem> Sorted(IEnumerable<FileItem> source, SortSpec spec) {
        var list = new List<FileItem>(source);
        list.Sort(Comparer(spec));
        return list;
    }
}
