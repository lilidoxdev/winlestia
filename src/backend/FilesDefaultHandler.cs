// Backend side of "Ouvrir les dossiers avec Winlestia Fichiers" (Preferences.FilesDefaultHandler, default false). The registry work is FilesHandler (shared with Winlestia.Files --register / --restore).
// Guards: exe missing at startup -> restore + pref off; crash flag written by Winlestia.Files (3 startup crashes in 60 s) -> pref off + toast. No polling: startup check + FileSystemWatcher on the flag.
// Threading: registry work runs under `gate` (plus the cross-process mutex inside FilesHandler). Store.State is only written and saved on the UI dispatcher, like every other preference.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Winlestia.Shared;

namespace PanneauxGlass;

internal static class FilesDefaultHandler {
    static FileSystemWatcher? watcher;
    static readonly object gate = new();
    internal static string ExePath => Path.Combine(AppContext.BaseDirectory, "Winlestia.Files.exe");

    // Toggle from the settings bridge (called off the UI thread). Returns the preference value to store (the caller assigns it on the UI thread and saves) and a notice (null when fine).
    // Turning off keeps the preference true when the restore failed, so the user still sees the state and can retry.
    internal static (bool pref, string? notice) Set(bool on) {
        lock (gate) {
            var o = HandlerOptions.FromEnvironment();
            if (on) {
                if (!File.Exists(ExePath)) return (false, "Winlestia Fichiers est introuvable : option non activée.");
                var r = FilesHandler.Register(o, ExePath);
                if (!r.Ok) Store.Log("FilesDefaultHandler register: " + r.Message);
                return r.Ok ? (true, null) : (false, "Activation impossible : " + r.Message);
            }
            var back = FilesHandler.Restore(o);
            if (!back.Ok) Store.Log("FilesDefaultHandler restore: " + back.Message);
            return back.Ok ? (false, null) : (true, "Restauration incomplète, réessaie : " + back.Message);
        }
    }

    internal static void Start() {
        Task.Run(() => {
            try { bool tripped = Check(); Migrate(tripped); RemovePin(); Watch(); } catch (Exception ex) { Store.Log("FilesDefaultHandler: " + ex.Message); }
        });
    }

    static bool Check() {
        lock (gate) {
            bool tripped = FilesHandlerGuard.ConsumeFlag(FilesHandler.DataDir);
            var o = HandlerOptions.FromEnvironment();
            bool pref = Store.State.FilesDefaultHandler;
            switch (FilesHandlerGuard.DecideStartup(pref, File.Exists(ExePath), tripped)) {
                case HandlerStartup.Ensure:
                    if (!FilesHandler.IsRegistered(o, ExePath)) FilesHandler.Register(o, ExePath);      // idempotent; follows a moved install
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

    // One-time default-on migration (handler, preload; the dock pin is gone, see RemovePin), off the UI thread; Store.State is only written on the dispatcher. A later opt-out is never undone (flag stays set).
    static void Migrate(bool tripped) {
        lock (gate) {
            var plan = FilesHandlerGuard.DecideDefaults(Store.State.FilesDefaultsMigrated, File.Exists(ExePath), tripped);
            if (!plan.Enable && !plan.MarkMigrated) return;
            bool registered = plan.Enable && Set(true).pref;
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            string exe = ExePath;
            dispatcher.BeginInvoke(() => {
                if (plan.Enable) {
                    Store.State.FilesPreload = true;
                    if (registered) Store.State.FilesDefaultHandler = true;
                }
                Store.State.FilesDefaultsMigrated = true; Store.Save();
            });
        }
    }

    // One-time cleanup of the old dock pin (FilesPinRemoved); state is written on the dispatcher like every other preference.
    static void RemovePin() {
        if (Store.State.FilesPinRemoved) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        string exe = ExePath;
        dispatcher.BeginInvoke(() => {
            var ids = new System.Collections.Generic.List<string>();
            bool done = Store.State.FilesPinRemoved;
            bool changed = FilesPinMigration.Apply(ref done, Store.State.TaskbarPins, Store.State.Custom, exe, ids);
            Store.State.FilesPinRemoved = done;
            if (!changed) return;
            foreach (var id in ids) Store.Entries.RemoveAll(e => e.Id == id);
            Store.Save();
        });
    }

    // Dock "Fichiers" button: activate / open a window in the resident instance, else start Winlestia.Files.exe, else Explorer.
    // Returns immediately (may be called on the dispatcher): the pipe connect and Process.Start run on the thread pool.
    internal static void Open() {
        Task.Run(() => {
            try {
                try {
                    if (FilesPipe.TrySend(FilesPipe.Build(Array.Empty<string>(), "", null), 300)) return;
                    if (File.Exists(ExePath)) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ExePath) { UseShellExecute = false })?.Dispose(); return; }
                } catch (Exception ex) { Store.Log("FilesDefaultHandler open: " + ex.Message); }
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose(); } catch (Exception ex) { Store.Log("FilesDefaultHandler explorer: " + ex.Message); }
            } catch (Exception ex) { Store.Log("FilesDefaultHandler open: " + ex.Message); }
        });
    }

    // Preference off only when the restore worked; otherwise it stays on and the toast says so.
    static void Disable(HandlerResult restored, string toast) {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;
        dispatcher.BeginInvoke(() => {
            if (restored.Ok) { Store.State.FilesDefaultHandler = false; Store.Save(); Toast.Show(toast); }
            else { Store.Log("FilesDefaultHandler restore: " + restored.Message); Toast.Show("Restauration des dossiers incomplète : " + restored.Message); }
        });
    }

    static void Watch() {
        string dir = FilesHandler.DataDir;
        Directory.CreateDirectory(dir);
        watcher = new FileSystemWatcher(dir, FilesHandlerGuard.FlagFile) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
        FileSystemEventHandler h = (_, _) => Task.Run(async () => { await Task.Delay(300); try { Check(); } catch (Exception ex) { Store.Log("FilesDefaultHandler: " + ex.Message); } });
        watcher.Created += h; watcher.Changed += h;
        watcher.EnableRaisingEvents = true;
    }
}
