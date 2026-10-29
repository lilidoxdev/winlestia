// Crash guard of the folder handler: pure decisions plus tiny file helpers (a directory is always passed in, so tests use a temp dir). Shared by Winlestia.Files, the backend and tests/FilesHandler.
// Winlestia.Files records every startup crash (timestamps in startup-crashes.txt). Three within 60 s: it restores the folder handler itself and drops handler-tripped.flag;
// the backend sees the flag (startup + FileSystemWatcher), turns the preference off and shows a toast.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Winlestia.Shared;

internal enum HandlerStartup { Nothing, Ensure, RestoreAndDisable, TrippedDisable }

internal static class FilesHandlerGuard {
    internal const int Limit = 3, WindowMs = 60000;
    internal const string StampsFile = "startup-crashes.txt", FlagFile = "handler-tripped.flag";

    // Pure: crashes inside the window that ends at nowMs (the one just recorded included).
    internal static bool ShouldTrip(IEnumerable<long> stampsMs, long nowMs) => stampsMs.Count(s => nowMs - s >= 0 && nowMs - s <= WindowMs) >= Limit;

    internal static List<long> Prune(IEnumerable<long> stampsMs, long nowMs) => stampsMs.Where(s => nowMs - s >= 0 && nowMs - s <= WindowMs).OrderBy(s => s).TakeLast(16).ToList();

    // Pure backend startup decision. tripped = the flag file was found.
    internal static HandlerStartup DecideStartup(bool prefOn, bool exeExists, bool tripped) =>
        !prefOn ? HandlerStartup.Nothing
        : tripped ? HandlerStartup.TrippedDisable
        : !exeExists ? HandlerStartup.RestoreAndDisable
        : HandlerStartup.Ensure;

    // Pure one-time "Winlestia Fichiers by default" migration decision (handler on, preload on, dock pin).
    // Already done: nothing. Crash guard tripped: never force it on, but record the migration so it does not retry. Exe missing: try again next start.
    internal static (bool Enable, bool MarkMigrated) DecideDefaults(bool migrated, bool exeExists, bool tripped) =>
        migrated ? (false, false) : tripped ? (false, true) : !exeExists ? (false, false) : (true, true);

    // Pure: only a real crash counts. Per-path problems (access denied, missing / invalid path, I/O) fall back to explorer.exe and never feed the counter.
    internal static bool CountsAsCrash(Exception ex) =>
        ex is not (UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException or System.Security.SecurityException);

    internal static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    static List<long> Read(string dir) {
        try {
            string f = Path.Combine(dir, StampsFile);
            return File.Exists(f) ? File.ReadAllLines(f).Select(l => long.TryParse(l.Trim(), out var v) ? v : -1).Where(v => v >= 0).ToList() : new List<long>();
        } catch { return new List<long>(); }
    }

    // Records one startup crash. Returns true when the limit is reached AND there was something to undo: the handler was then restored and the flag written.
    internal static bool RecordStartupCrash(string dir, long nowMs, HandlerOptions? options = null) {
        try {
            Directory.CreateDirectory(dir);
            var stamps = Prune(Read(dir).Append(nowMs), nowMs);
            string file = Path.Combine(dir, StampsFile);
            if (!ShouldTrip(stamps, nowMs)) { File.WriteAllLines(file, stamps.Select(s => s.ToString())); return false; }
            try { File.Delete(file); } catch { }
            var o = options ?? HandlerOptions.FromEnvironment();
            if (!FilesHandler.IsActive(o)) return false;          // handler not enabled: nothing to undo, no flag, no misleading toast
            var r = FilesHandler.Restore(o);
            FilesHandler.Log("crash guard: 3 startup crashes in 60 s, restore " + (r.Ok ? "ok" : "FAILED: " + r.Message));
            File.WriteAllText(Path.Combine(dir, FlagFile), nowMs.ToString());
            return true;
        } catch { return false; }
    }

    // True once if the flag exists (and removes it).
    internal static bool ConsumeFlag(string dir) {
        try { string f = Path.Combine(dir, FlagFile); if (!File.Exists(f)) return false; File.Delete(f); return true; }
        catch { return false; }
    }
}
