// language: C# 12, file: WinjetArchive.cs, target: Windows 11 / .NET 8, copie bornée d'un ZIP ou d'un dossier vers une zone de préparation
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
namespace PanneauxGlass.Winjets;

// Un paquet n'est jamais lu en place : il est d'abord copié/extrait dans un dossier temporaire neuf, en vérifiant chaque chemin
// et en comptant les octets réellement écrits (pas seulement les tailles annoncées, qu'un ZIP piégé peut falsifier).
internal static class WinjetArchive
{
    static readonly string[] RootFiles = { "manifest.json", "widget.json", "preview.png" };

    // Faux si au moins une erreur a été signalée ; `dest` doit exister et être vide.
    internal static bool Stage(string source, string dest, WinjetIssues issues)
    {
        issues.Scope = "";
        try
        {
            if (Directory.Exists(source))
                return CopyFolder(source, dest, issues);
            if (File.Exists(source))
                return Extract(source, dest, issues);
            issues.Add("", "chemin introuvable : " + source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            issues.Add("", "archive illisible ou corrompue (" + ex.Message + ").");
        }
        return false;
    }

    // Vérifie un nom d'entrée et renvoie son chemin relatif normalisé (séparateur /), ou null avec une erreur signalée.
    internal static string? CheckName(string raw, bool isDir, WinjetIssues issues)
    {
        string name = raw.Replace('\\', '/');
        string label = "entrée « " + raw + " »";
        if (name.Length == 0 || name.StartsWith('/') || (name.Length > 1 && name[1] == ':') || name.Contains(':'))
        {
            issues.Add("", label + " : chemin absolu ou lecteur interdit.");
            return null;
        }
        var parts = name.TrimEnd('/').Split('/');
        if (parts.Length > WinjetLimits.MaxDepth)
        {
            issues.Add("", label + " : profondeur " + parts.Length + " (maximum " + WinjetLimits.MaxDepth + ").");
            return null;
        }
        foreach (string part in parts)
        {
            if (part.Length == 0 || part == "." || part == "..")
            {
                issues.Add("", label + " : segment de chemin interdit (vide, « . » ou « .. »).");
                return null;
            }
            if (part.Any(c => c < 32 || "<>\"|?*".Contains(c)) || part.EndsWith('.') || part.EndsWith(' '))
            {
                issues.Add("", label + " : caractère ou terminaison interdits dans « " + part + " ».");
                return null;
            }
            if (WinjetLimits.ReservedNames.Contains(part.Split('.')[0].TrimEnd(' ', '.')))
            {
                issues.Add("", label + " : nom réservé de Windows « " + part + " ».");
                return null;
            }
        }
        if (parts.Length == 1 && !isDir)
        {
            if (!RootFiles.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
            {
                issues.Add("", label + " : fichier non prévu à la racine (seuls manifest.json, widget.json, preview.png et assets/ sont permis).");
                return null;
            }
        }
        else if (!parts[0].Equals("assets", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("", label + " : tout contenu doit se trouver sous assets/.");
            return null;
        }
        if (!isDir)
        {
            string ext = Path.GetExtension(parts[^1]);
            if (!WinjetLimits.Extensions.Contains(ext))
            {
                issues.Add("", label + " : extension « " + ext + " » non autorisée (" + string.Join(" ", WinjetLimits.Extensions.Order())
                    + ").");
                return null;
            }
        }
        return string.Join('/', parts);
    }

    static long CapFor(string rel, long remaining)
    {
        if (rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return Math.Min(WinjetLimits.MaxJsonBytes, remaining);
        if (rel.Equals("preview.png", StringComparison.OrdinalIgnoreCase))
            return Math.Min(WinjetLimits.MaxPreviewBytes, remaining);
        return remaining;
    }

    static string Target(string dest, string rel)
    {
        string full = Path.GetFullPath(Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar)));
        string root = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("chemin hors de la zone de préparation : " + rel);
        return full;
    }

    // Copie `input` dans `target` en comptant les octets ; renvoie le nombre d'octets écrits ou -1 si le plafond est dépassé.
    static long CopyCapped(Stream input, string target, long cap)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > cap)
                return -1;
            output.Write(buffer, 0, read);
        }
        return total;
    }

    static bool Extract(string zipPath, string dest, WinjetIssues issues)
    {
        long size = new FileInfo(zipPath).Length;
        if (size > WinjetLimits.MaxZipBytes)
        {
            issues.Add("", "archive trop grosse (" + size / 1024 + " Kio, maximum " + WinjetLimits.MaxZipBytes / 1024 + " Kio).");
            return false;
        }
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > WinjetLimits.MaxEntries)
        {
            issues.Add("", "trop d'entrées (" + zip.Entries.Count + ", maximum " + WinjetLimits.MaxEntries + ").");
            return false;
        }
        int before = issues.Count;
        long declared = 0;
        foreach (var e in zip.Entries)
            declared += Math.Max(0, e.Length);
        if (declared > WinjetLimits.MaxUnpackedBytes)
        {
            issues.Add("", "contenu décompressé trop gros (" + declared / 1024 / 1024 + " Mio annoncés, maximum "
                + WinjetLimits.MaxUnpackedBytes / 1024 / 1024 + " Mio).");
            return false;
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long written = 0;
        foreach (var e in zip.Entries)
        {
            bool isDir = e.FullName.EndsWith('/') || e.FullName.EndsWith('\\');
            // Bits UNIX du ZIP : un lien symbolique (S_IFLNK) n'est jamais accepté.
            if (((e.ExternalAttributes >> 16) & 0xF000) == 0xA000)
            {
                issues.Add("", "entrée « " + e.FullName + " » : lien symbolique interdit.");
                continue;
            }
            string? rel = CheckName(e.FullName, isDir, issues);
            if (rel == null)
                continue;
            if (!seen.Add(rel))
            {
                issues.Add("", "entrée « " + e.FullName + " » : nom en double.");
                continue;
            }
            if (isDir)
            {
                Directory.CreateDirectory(Target(dest, rel));
                continue;
            }
            long cap = CapFor(rel, WinjetLimits.MaxUnpackedBytes - written);
            if (e.Length > cap)
            {
                issues.Add("", "entrée « " + e.FullName + " » : trop grosse (" + e.Length / 1024 + " Kio annoncés).");
                continue;
            }
            using var input = e.Open();
            long n = CopyCapped(input, Target(dest, rel), cap);
            if (n < 0 || n != e.Length)
            {
                issues.Add("", "entrée « " + e.FullName + " » : taille réelle différente de la taille annoncée ou supérieure aux limites"
                    + " (archive piégée ?).");
                return false;
            }
            written += n;
        }
        return issues.Count == before;
    }

    static bool CopyFolder(string source, string dest, WinjetIssues issues)
    {
        int before = issues.Count;
        var root = new DirectoryInfo(source);
        if (root.Attributes.HasFlag(FileAttributes.ReparsePoint) || root.LinkTarget != null)
        {
            issues.Add("", "le dossier est un lien : interdit.");
            return false;
        }
        int count = 0;
        long written = 0;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var info in dir.EnumerateFileSystemInfos())
            {
                string rel = Path.GetRelativePath(source, info.FullName);
                if (++count > WinjetLimits.MaxEntries)
                {
                    issues.Add("", "trop d'entrées (maximum " + WinjetLimits.MaxEntries + ").");
                    return false;
                }
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget != null)
                {
                    issues.Add("", "entrée « " + rel + " » : lien ou point d'analyse interdit.");
                    continue;
                }
                bool isDir = info is DirectoryInfo;
                string? clean = CheckName(rel, isDir, issues);
                if (clean == null)
                    continue;
                if (info is DirectoryInfo sub)
                {
                    Directory.CreateDirectory(Target(dest, clean));
                    pending.Push(sub);
                    continue;
                }
                var file = (FileInfo)info;
                long cap = CapFor(clean, WinjetLimits.MaxUnpackedBytes - written);
                if (file.Length > cap)
                {
                    issues.Add("", "entrée « " + rel + " » : trop grosse (" + file.Length / 1024 + " Kio).");
                    continue;
                }
                using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                long n = CopyCapped(input, Target(dest, clean), cap);
                if (n < 0)
                {
                    issues.Add("", "entrée « " + rel + " » : dépasse les limites de taille.");
                    return false;
                }
                written += n;
            }
        }
        return issues.Count == before;
    }
}
