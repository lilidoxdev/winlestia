// Garde de plantage du gestionnaire de dossiers : décisions pures et petits utilitaires de fichiers (le dossier est toujours passé en
// paramètre, donc les tests utilisent un dossier temporaire). Partagée par Winlestia.Files, le backend et tests/FilesHandler.
// Winlestia.Files consigne chaque plantage au démarrage (horodatages dans startup-crashes.txt). Trois en 60 s : il restaure lui-même le
// gestionnaire de dossiers et dépose handler-tripped.flag ;
// le backend voit le drapeau (démarrage + FileSystemWatcher), désactive la préférence et affiche un toast.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Winlestia.Shared;

internal enum HandlerStartup
{
    Nothing, Ensure, RestoreAndDisable, TrippedDisable
}

internal static class FilesHandlerGuard
{
    internal const int Limit = 3, WindowMs = 60000;
    internal const string StampsFile = "startup-crashes.txt", FlagFile = "handler-tripped.flag";

    // Pur : plantages dans la fenêtre qui se termine à nowMs (celui qui vient d'être consigné inclus).
    internal static bool ShouldTrip(IEnumerable<long> stampsMs, long nowMs) => stampsMs.Count(s => nowMs - s >= 0
        && nowMs - s <= WindowMs) >= Limit;

    internal static List<long> Prune(IEnumerable<long> stampsMs, long nowMs) => stampsMs.Where(s => nowMs - s >= 0
        && nowMs - s <= WindowMs).OrderBy(s => s).TakeLast(16).ToList();

    // Décision pure au démarrage du backend. tripped = le fichier drapeau a été trouvé.
    internal static HandlerStartup DecideStartup(bool prefOn, bool exeExists, bool tripped) =>
        !prefOn ? HandlerStartup.Nothing
        : tripped ? HandlerStartup.TrippedDisable
        : !exeExists ? HandlerStartup.RestoreAndDisable
        : HandlerStartup.Ensure;

    // Décision pure de la migration unique « Winlestia Fichiers par défaut » (gestionnaire activé, préchargement activé, épingle du dock).
    // Déjà faite : rien. Garde de plantage déclenchée : on ne force jamais l'activation, mais on enregistre la migration pour ne pas
    // réessayer. Exe absent : on réessaie au prochain démarrage.
    internal static (bool Enable, bool MarkMigrated) DecideDefaults(bool migrated, bool exeExists, bool tripped) =>
        migrated ? (false, false) : tripped ? (false, true) : !exeExists ? (false, false) : (true, true);

    // Pur : seul un vrai plantage compte. Les problèmes propres à un chemin (accès refusé, chemin absent / invalide, E/S) retombent sur
    // explorer.exe et n'alimentent jamais le compteur.
    internal static bool CountsAsCrash(Exception ex) =>
        ex is not (UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException or System.Security
            .SecurityException);

    internal static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    static List<long> Read(string dir)
    {
        try
        {
            string f = Path.Combine(dir, StampsFile);
            return File.Exists(f) ? File.ReadAllLines(f).Select(l => long.TryParse(l.Trim(), out var v) ? v : -1).Where(v
                => v >= 0).ToList() : new List<long>();
        }
        catch { return new List<long>(); }
    }

    // Consigne un plantage au démarrage. Renvoie true quand la limite est atteinte ET qu'il y avait quelque chose à annuler : le
    // gestionnaire a alors été restauré et le drapeau écrit.
    internal static bool RecordStartupCrash(string dir, long nowMs, HandlerOptions? options = null)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var stamps = Prune(Read(dir).Append(nowMs), nowMs);
            string file = Path.Combine(dir, StampsFile);
            if (!ShouldTrip(stamps, nowMs))
            {
                File.WriteAllLines(file, stamps.Select(s => s.ToString()));
                return false;
            }
            try
            {
                File.Delete(file);
            }
            catch (Exception)
            {
                // Nettoyage au mieux : fichier déjà supprimé ou verrouillé.
            }
            var o = options ?? HandlerOptions.FromEnvironment();
            if (!FilesHandler.IsActive(o))
                return false;          // handler not enabled: nothing to undo, no flag, no misleading toast
            var r = FilesHandler.Restore(o);
            FilesHandler.Log("crash guard: 3 startup crashes in 60 s, restore " + (r.Ok ? "ok" : "FAILED: " + r.Message));
            File.WriteAllText(Path.Combine(dir, FlagFile), nowMs.ToString());
            return true;
        }
        catch { return false; }
    }

    // Vrai une seule fois si le drapeau existe (et le supprime).
    internal static bool ConsumeFlag(string dir)
    {
        try
        {
            string f = Path.Combine(dir, FlagFile);
            if (!File.Exists(f))
                return false;
            File.Delete(f);
            return true;
        }
        catch { return false; }
    }
}
