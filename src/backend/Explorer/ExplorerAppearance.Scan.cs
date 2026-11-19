// ExplorerAppearance : parcours de l'arborescence visible et filtrage des dossiers.

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows.Threading;
using D = System.Drawing;
using G = System.Drawing.Drawing2D;

namespace PanneauxGlass;

internal sealed partial class ExplorerAppearance
{
    // Thème le dossier ouvert (s'il est sous un dossier utilisateur) et ses sous-dossiers jusqu'à la profondeur 3 ; 256 thémés / 1024
    // examinés au plus par emplacement, budget partagé par passe.
    internal void ColourVisibleTree(string parent, int[] budget)
    {
        if (!Directory.Exists(parent) || parent.StartsWith(@"\\"))
            return;
        lock (gate)
        {
            if (disposed || !Store.State.ExplorerTheme)
                return;
            string full = Path.GetFullPath(parent);
            if (!Within(full, false) || Skipped(full))
                return;
            string icon = EnsureIcon("folder");
            var data = Read();
            var pending = new List<(string folder, Backup saved, string icon)>();
            bool flushed = false;
            var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int applied = 0, seen = 0;
            bool stop = false;
            // Les jonctions / liens symboliques (ReparsePoint) sont ignorés volontairement : leur dossier cible est thémé quand il est
            // visité lui-même.
            bool Theme(string folder, string owner)
            {
                try
                {
                    var info = new DirectoryInfo(folder);
                    if (disposed
                        || Skipped(folder)
                        || info.Name.StartsWith(".")
                        || (info.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0
                        || Protected(folder))
                        return false;
                    if (scanned.TryGetValue(folder, out string? previous) && previous == Scheme)
                        return true;
                    if (applied >= 256 || budget[0] <= 0)
                    {
                        stop = true;
                        return false;
                    }
                    budget[0]--;
                    if (AlreadyThemed(folder, icon, data))
                    {
                        scanned[folder] = Scheme;
                        return true;
                    }
                    scanned[folder] = Scheme;
                    var prep = Prepare(folder, data);
                    pending.Add((prep.folder, prep.saved, icon));
                    applied++;
                    if (pending.Count >= 32)
                        Flush();
                    parents.Add(owner);
                    return true;
                }
                catch (Exception ex)
                {
                    Store.Log("Icône dossier ignorée: " + folder + ": " + ex.Message);
                    return true;
                }
            }
            // Les sauvegardes d'un lot sont persistées AVANT les écritures desktop.ini de ce lot ; un Save supplémentaire à la fin
            // enregistre les hashs appliqués.
            void Flush()
            {
                if (pending.Count == 0)
                    return;
                Save(data);
                foreach (var item in pending)
                {
                    if (disposed)
                        break;
                    try
                    {
                        WriteFolder(item.folder, item.icon, item.saved);
                    }
                    catch (Exception ex) { Store.Log("Icône dossier ignorée: " + item.folder + ": " + ex.Message); }
                }
                pending.Clear();
                flushed = true;
            }
            if (Within(full, true) && !Protected(full))
                Theme(full, Path.GetDirectoryName(full) ?? full);
            var level = new List<string> { full };
            for (int depth = 0; depth < 3 && !stop && level.Count > 0; depth++)
            {
                var next = new List<string>();
                foreach (string dir in level)
                {
                    if (stop || disposed || Protected(dir) || Skipped(dir))
                        continue;
                    List<string> children;
                    try
                    {
                        children = Directory.EnumerateDirectories(dir).Take(256).ToList();
                    }
                    catch { continue; }
                    foreach (string child in children)
                    {
                        if (stop || ++seen > 1024)
                        {
                            stop = true;
                            break;
                        }
                        if (Theme(child, dir))
                            next.Add(child);
                    }
                }
                level = next;
            }
            Flush();
            if (flushed)
                Save(data);
            if (flushed)
                Status = data.Count + " dossier(s) assorti(s), y compris les dossiers affichés.";
        }
    }
    string accent = "#afd4f0", background = "#101518", text = "#e5ebef", signature = "";
    volatile bool disposed;
    static string Real(string p)
    {
        p = Path.GetFullPath(p);
        try
        {
            p = new DirectoryInfo(p).ResolveLinkTarget(true)?.FullName ?? p;
        }
        catch (Exception err) when (err is IOException or UnauthorizedAccessException)
        {
            Store.Log("ExplorerAppearance.Real : " + err.GetType().Name + ": " + err.Message);
        }
        return p.TrimEnd(Path.DirectorySeparatorChar);
    }
    // Appartenance à l'un des six dossiers personnels (chemins réels) ; strict exclut le dossier personnel lui-même.
    static bool Within(string full, bool strict)
    {
        string real = Real(full);
        return PersonalFolders().Any(p =>
        {
            if (p.path.Length == 0)
                return false;
            string root = Real(p.path);
            return (!strict && real.Equals(root, StringComparison.OrdinalIgnoreCase))
                || real.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        });
    }
    static readonly HashSet<string> DevDirs =
        new(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj", ".venv", "__pycache__", "target", "build", "dist" };
    static bool Skipped(string folder)
    {
        try
        {
            return DevDirs.Contains(Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)))
                || Directory.Exists(Path.Combine(folder, ".git"))
                || File.Exists(Path.Combine(folder, ".git"));
        }
        catch { return true; }
    }
}
