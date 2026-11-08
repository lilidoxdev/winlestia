// Côté backend de « Ouvrir les dossiers avec Winlestia Fichiers » (Preferences.FilesDefaultHandler, false par défaut). Le travail de
// registre est dans FilesHandler (partagé avec Winlestia.Files --register / --restore).
// Garde-fous : exe absent au démarrage -> restauration + préférence désactivée ; drapeau de plantage écrit par Winlestia.Files (3
// plantages au démarrage en 60 s) -> préférence désactivée + toast. Pas de sondage : vérification au démarrage + FileSystemWatcher
// sur le drapeau. Threads : le travail de registre s'exécute sous `gate` (plus le mutex inter-processus dans FilesHandler). Store.State
// n'est écrit et sauvé que sur le dispatcher UI, comme toute autre préférence.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Winlestia.Shared;

namespace PanneauxGlass;

internal static class FilesDefaultHandler
{
    // Laisse l'écriture du fichier drapeau se terminer avant de relire l'état.
    const int FlagDebounceMs = 300;
    static FileSystemWatcher? watcher;
    static readonly object gate = new();
    internal static string ExePath => Path.Combine(AppContext.BaseDirectory, "Winlestia.Files.exe");

    // Bascule depuis le pont des réglages (appelée hors du thread UI). Renvoie la valeur de préférence à stocker (l'appelant
    // l'assigne sur le thread UI et sauve) et un avis (null si tout va bien).
    // Désactiver garde la préférence à true quand la restauration a échoué, pour que l'utilisateur voie l'état et puisse
    // réessayer.
    internal static (bool pref, string? notice) Set(bool on)
    {
        lock (gate)
        {
            var o = HandlerOptions.FromEnvironment();
            if (on)
            {
                if (!File.Exists(ExePath))
                    return (false, "Winlestia Fichiers est introuvable : option non activée.");
                var r = FilesHandler.Register(o, ExePath);
                if (!r.Ok)
                    Store.Log("FilesDefaultHandler register: " + r.Message);
                return r.Ok ? (true, null) : (false, "Activation impossible : " + r.Message);
            }
            var back = FilesHandler.Restore(o);
            if (!back.Ok)
                Store.Log("FilesDefaultHandler restore: " + back.Message);
            return back.Ok ? (false, null) : (true, "Restauration incomplète, réessaie : " + back.Message);
        }
    }

    internal static void Start()
    {
        Task.Run(() =>
        {
            try
            {
                bool tripped = Check();
                Migrate(tripped);
                RemovePin();
                Watch();
            }
            catch (Exception ex) { Store.Log("FilesDefaultHandler: " + ex.Message); }
        });
    }

    static bool Check()
    {
        lock (gate)
        {
            bool tripped = FilesHandlerGuard.ConsumeFlag(FilesHandler.DataDir);
            var o = HandlerOptions.FromEnvironment();
            bool pref = Store.State.FilesDefaultHandler;
            switch (FilesHandlerGuard.DecideStartup(pref, File.Exists(ExePath), tripped))
            {
                case HandlerStartup.Ensure:
                    if (!FilesHandler.IsRegistered(o, ExePath))
                        FilesHandler.Register(o, ExePath);      // idempotent; follows a moved install
                    break;
                case HandlerStartup.RestoreAndDisable:
                    Disable(FilesHandler.Restore(o), "Winlestia Fichiers est introuvable : Explorer rouvre à nouveau les dossiers.");
                    break;
                case HandlerStartup.TrippedDisable:
                    Disable(FilesHandler.Restore(o), "Winlestia Fichiers a planté trois fois : Explorer rouvre à nouveau les dossiers.");
                    break;
            }
            return tripped;
        }
    }

    // Migration unique « activé par défaut » (handler, préchargement ; l'épingle du dock a disparu, voir RemovePin), hors du thread
    // UI ; Store.State n'est écrit que sur le dispatcher. Un refus ultérieur n'est jamais annulé (le drapeau reste posé).
    static void Migrate(bool tripped)
    {
        lock (gate)
        {
            var plan = FilesHandlerGuard.DecideDefaults(Store.State.FilesDefaultsMigrated, File.Exists(ExePath), tripped);
            if (!plan.Enable && !plan.MarkMigrated)
                return;
            bool registered = plan.Enable && Set(true).pref;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;
            string exe = ExePath;
            dispatcher.BeginInvoke(() =>
            {
                if (plan.Enable)
                {
                    Store.State.FilesPreload = true;
                    if (registered)
                        Store.State.FilesDefaultHandler = true;
                }
                Store.State.FilesDefaultsMigrated = true;
                Store.Save();
            });
        }
    }

    // Nettoyage unique de l'ancienne épingle du dock (FilesPinRemoved) ; l'état est écrit sur le dispatcher comme toute autre
    // préférence.
    static void RemovePin()
    {
        if (Store.State.FilesPinRemoved)
            return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;
        string exe = ExePath;
        dispatcher.BeginInvoke(() =>
        {
            var ids = new System.Collections.Generic.List<string>();
            bool done = Store.State.FilesPinRemoved;
            bool changed = FilesPinMigration.Apply(ref done, Store.State.TaskbarPins, Store.State.Custom, exe, ids);
            Store.State.FilesPinRemoved = done;
            if (!changed)
                return;
            foreach (var id in ids)
                Store.Entries.RemoveAll(e => e.Id == id);
            Store.Save();
        });
    }

    // Bouton « Fichiers » du dock : active / ouvre une fenêtre dans l'instance résidente, sinon lance Winlestia.Files.exe, sinon
    // l'Explorateur. Retourne immédiatement (peut être appelé sur le dispatcher) : la connexion au canal et Process.Start s'exécutent
    // sur le pool de threads.
    internal static void Open()
    {
        Task.Run(() =>
        {
            try
            {
                try
                {
                    if (FilesPipe.TrySend(FilesPipe.Build(Array.Empty<string>(), "", null), 300))
                        return;
                    if (File.Exists(ExePath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ExePath) { UseShellExecute = false
                            })?.Dispose();
                        return;
                    }
                }
                catch (Exception ex) { Store.Log("FilesDefaultHandler open: " + ex.Message); }
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true
                        })?.Dispose();
                }
                catch (Exception ex) { Store.Log("FilesDefaultHandler explorer: " + ex.Message); }
            }
            catch (Exception ex) { Store.Log("FilesDefaultHandler open: " + ex.Message); }
        });
    }

    // Préférence désactivée seulement si la restauration a fonctionné ; sinon elle reste activée et le toast le dit.
    static void Disable(HandlerResult restored, string toast)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;
        dispatcher.BeginInvoke(() =>
        {
            if (restored.Ok)
            {
                Store.State.FilesDefaultHandler = false;
                Store.Save();
                Toast.Show(toast);
            }
            else
            {
                Store.Log("FilesDefaultHandler restore: " + restored.Message);
                Toast.Show("Restauration des dossiers incomplète : " + restored.Message);
            }
        });
    }

    static void Watch()
    {
        string dir = FilesHandler.DataDir;
        Directory.CreateDirectory(dir);
        watcher = new FileSystemWatcher(dir, FilesHandlerGuard.FlagFile) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
            };
        FileSystemEventHandler h = (_, _) => Task.Run(async () => { await Task.Delay(FlagDebounceMs); try { Check(); }
            catch (Exception ex) { Store.Log("FilesDefaultHandler: " + ex.Message); } });
        watcher.Created += h;
        watcher.Changed += h;
        watcher.EnableRaisingEvents = true;
    }
}
