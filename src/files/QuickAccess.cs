// Modèle de la liste d'accès rapide : les chemins épinglés (dossiers et fichiers) avec une étiquette facultative, dans l'ordre de
// l'utilisateur, persistés dans settings.json.
// Chaque opération modifie la LISTE uniquement, jamais la cible sur disque. Chacune enregistre et déclenche Changed (la barre latérale
// reconstruit sa section).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WinlestiaFiles;

internal enum TargetKind
{
    Unknown, Folder, File, Missing
}

internal static class QuickAccess
{
    internal static event Action? Changed;

    internal static List<QuickEntry> Entries => FilesSettings.Data.QuickAccess ??= new List<QuickEntry>();
    internal static bool Initialized => FilesSettings.Data.QuickAccess != null;

    static string Key(string path) => path.TrimEnd('\\', '/');
    internal static int IndexOf(string path)
    {
        var list = FilesSettings.Data.QuickAccess;
        if (list == null)
            return -1;
        string key = Key(path);
        return list.FindIndex(q => string.Equals(Key(q.Path), key, StringComparison.OrdinalIgnoreCase));
    }
    internal static bool IsPinned(string path) => IndexOf(path) >= 0;

    static void Commit()
    {
        FilesSettings.Save();
        Changed?.Invoke();
    }

    // Premier lancement (pas de clé "quickAccess") : les dossiers que la barre latérale montrait jusque-là, dans le même ordre.
    internal static void InitIfNew(IEnumerable<string> paths)
    {
        if (Initialized)
            return;
        FilesSettings.Data.QuickAccess = paths.Select(p => new QuickEntry { Path = p }).ToList();
        FilesSettings.Save();
    }

    internal static bool Pin(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || IsPinned(path))
            return false;
        Entries.Add(new QuickEntry { Path = path });
        Commit();
        return true;
    }
    internal static bool Unpin(string path)
    {
        int i = IndexOf(path);
        return i >= 0 && RemoveAt(i);
    }
    internal static bool RemoveAt(int i)
    {
        if (i < 0 || i >= Entries.Count)
            return false;
        Entries.RemoveAt(i);
        Commit();
        return true;
    }

    internal static bool MoveTo(int from, int to)
    {
        var list = Entries;
        if (from < 0 || from >= list.Count || to < 0 || to >= list.Count || from == to)
            return false;
        var q = list[from];
        list.RemoveAt(from);
        list.Insert(to, q);
        Commit();
        return true;
    }
    internal static bool Move(int i, int delta) => MoveTo(i, i + delta);

    // Vide / blanc = retour au nom par défaut.
    internal static void SetLabel(int i, string? label)
    {
        if (i < 0 || i >= Entries.Count)
            return;
        label = label?.Trim();
        Entries[i].Label = string.IsNullOrEmpty(label) ? null : label;
        Commit();
    }

    // "Retrouver la cible" : le chemin change, l'étiquette et la position restent.
    internal static void Replace(int i, string newPath)
    {
        if (i < 0 || i >= Entries.Count || string.IsNullOrWhiteSpace(newPath))
            return;
        Entries[i].Path = newPath;
        Commit();
    }

    // Vérification d'existence pour l'interface : exécutée sur le pool et abandonnée après le délai (Directory.Exists peut bloquer
    // longtemps sur un lecteur réseau déconnecté) ; un délai dépassé compte comme absent.
    internal static async Task<TargetKind> Probe(string path, int timeoutMs = 2500)
    {
        var work = Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(path))
                    return TargetKind.Folder;
                if (File.Exists(path))
                    return TargetKind.File;
            }
            catch (Exception)
            {
                // Chemin inaccessible : type de cible inconnu.
            }
            return TargetKind.Missing;
        });
        var first = await Task.WhenAny(work, Task.Delay(timeoutMs));
        return first == work ? work.Result : TargetKind.Missing;
    }
}
