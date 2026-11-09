// Modèle d'élément, collection à notification unique et tri en mémoire (ne réénumère jamais le disque).
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace WinlestiaFiles;

internal enum SortColumn
{
    Name, Modified, Type, Size
}
// FoldersFirst false = dossiers et fichiers mélangés, chacun selon sa propre valeur. Par défaut : date de modification, la plus
// récente d'abord.
internal sealed record SortSpec(SortColumn Column, bool Ascending, bool FoldersFirst = false)
{
    internal static readonly SortSpec Default = new(SortColumn.Modified, false, false);
}

public sealed class FileItem : INotifyPropertyChanged
{
    // Chemins actuellement coupés vers le presse-papiers (thread UI seulement) : lus quand un conteneur se lie, levés par élément via
    // RaiseCut().
    internal static HashSet<string> CutPaths = new(StringComparer.OrdinalIgnoreCase);
    public event PropertyChangedEventHandler? PropertyChanged;
    public double CutOpacity => CutPaths.Count > 0 && CutPaths.Contains(FullPath) ? 0.5 : 1.0;
    internal void RaiseCut() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CutOpacity)));
    // Surbrillance de survol du glisser (une ligne / tuile de dossier qui recevrait le dépôt).
    public bool DropHot
    {
        get; private set;
    }
    internal void SetDrop(bool on)
    {
        if (DropHot == on)
            return;
        DropHot = on;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DropHot)));
    }
    // Posé une fois par la fenêtre : points de code Material Symbols quand la police est livrée, Segoe Fluent sinon.
    internal static bool UseMaterial;
    internal FileItem(string name, string fullPath, bool isDir, long size, DateTime modified, string type)
    {
        Name = name;
        FullPath = fullPath;
        IsDir = isDir;
        Size = size;
        Modified = modified;
        TypeLabel = type;
    }
    public string Name
    {
        get;
    }
    public string FullPath
    {
        get;
    }
    public bool IsDir
    {
        get;
    }
    public long Size
    {
        get;
    }
    public DateTime Modified
    {
        get;
    }
    public string TypeLabel
    {
        get;
    }
    // Recherche profonde seulement : dossier du résultat relatif au dossier recherché ("" = le dossier lui-même).
    public string Where { get; internal set; } = "";
    public string Icon => IsDir ? (UseMaterial ? "" : "") : (UseMaterial ? "" : "");
    public string ModifiedText => Modified.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    public string SizeText => IsDir ? "" : Size < 1024 ? Size + " o" : Size < 1024 * 1024 ? Math.Ceiling(Size / 1024.0) + " Ko"
        : Size < 1024L * 1024 * 1024 ? (Size / 1048576.0).ToString("0.#", CultureInfo.GetCultureInfo("fr-FR")) + " Mo"
            : (Size / 1073741824.0).ToString("0.##", CultureInfo.GetCultureInfo("fr-FR")) + " Go";

    // Lit les informations d'un chemin (thread de travail, une lecture find-data) ; null s'il a disparu.
    internal static FileItem? Probe(string path, Dictionary<string, string> types)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.Exists ? From(info, types) : null;
        }
        catch { return null; }
    }

    // Appelé sur le thread d'énumération : un FileSystemInfo issu d'une énumération porte déjà les find-data (pas d'E/S
    // supplémentaire).
    internal static FileItem From(ref System.IO.Enumeration.FileSystemEntry e, Dictionary<string, string> types)
    {
        string name = e.FileName.ToString(), full = e.ToFullPath();
        if (e.IsDirectory)
            return new FileItem(name, full, true, 0, e.LastWriteTimeUtc.LocalDateTime, "Dossier");
        string ext = Path.GetExtension(name);
        if (!types.TryGetValue(ext, out var type))
            types[ext] = type = ext.Length > 1 ? "Fichier " + ext.Substring(1).ToUpperInvariant() : "Fichier";
        return new FileItem(name, full, false, e.Length, e.LastWriteTimeUtc.LocalDateTime, type);
    }

    internal static FileItem From(FileSystemInfo info, Dictionary<string, string> types)
    {
        bool dir = (info.Attributes & FileAttributes.Directory) != 0;
        if (dir)
            return new FileItem(info.Name, info.FullName, true, 0, info.LastWriteTime, "Dossier");
        string ext = Path.GetExtension(info.Name);
        if (!types.TryGetValue(ext, out var type))
            types[ext] = type = ext.Length > 1 ? "Fichier " + ext.Substring(1).ToUpperInvariant() : "Fichier";
        return new FileItem(info.Name, info.FullName, false, info is FileInfo f ? f.Length : 0, info.LastWriteTime, type);
    }
}

// Les changements groupés lèvent un seul Reset (la vue de collection de liste de WPF rejette les notifications Add multi-éléments).
internal sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    internal void AddRange(IEnumerable<T> items)
    {
        CheckReentrancy();
        ((List<T>)Items).AddRange(items);
        Notify();
    }
    // diagnostic : nombre de réécritures complètes de la liste (l'auto-test de rafraîchissement n'en attend aucune pour de petits
    // changements)
    internal static int Resets;
    internal void Reset(IEnumerable<T>? items)
    {
        CheckReentrancy();
        Resets++;
        var list = (List<T>)Items;
        list.Clear();
        if (items != null)
            list.AddRange(items);
        Notify();
    }
    void Notify()
    {
        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

internal static class FileSort
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int StrCmpLogicalW(string a, string b);

    // Ordre naturel des noms en départage ; dossiers groupés d'abord seulement si le spec le demande.
    internal static Comparison<FileItem> Comparer(SortSpec spec)
    {
        int sign = spec.Ascending ? 1 : -1;
        return (a, b) =>
        {
            if (spec.FoldersFirst && a.IsDir != b.IsDir)
                return a.IsDir ? -1 : 1;
            int c = spec.Column switch
            {
                SortColumn.Modified => a.Modified.CompareTo(b.Modified),
                SortColumn.Type => string.Compare(a.TypeLabel, b.TypeLabel, StringComparison.OrdinalIgnoreCase),
                SortColumn.Size => a.Size.CompareTo(b.Size),
                _ => StrCmpLogicalW(a.Name, b.Name)
            };
            return c != 0 ? c * sign : StrCmpLogicalW(a.Name, b.Name);
        };
    }

    internal static List<FileItem> Sorted(IEnumerable<FileItem> source, SortSpec spec)
    {
        var list = new List<FileItem>(source);
        list.Sort(Comparer(spec));
        return list;
    }
}
