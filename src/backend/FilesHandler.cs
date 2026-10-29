// Register / restore Winlestia Fichiers as the folder handler (HKCU only), shared (linked) by Winlestia.Files (owner of the --register / --restore CLI),
// the backend (toggle, startup guard) and tests/FilesHandler. Pure .NET: no WPF, no backend type.
// Owner = Winlestia.Files.exe: it is the one artifact the installer ships and the uninstaller / the crash guard can call without the backend or any UI.
//
// Keys written (under <Base>\<ClassesPath>, normally HKCU\Software\Classes), only for Directory and Drive, NEVER Folder:
//   <T>\shell                      (Default) = winlestia
//   <T>\shell\winlestia            (Default) = MUIVerb = "Ouvrir avec Winlestia Fichiers", Icon = "<exe>",0
//   <T>\shell\winlestia\command    (Default) = "<exe>" "%1"
// Everything is relative to Options, so tests run on a sandbox such as HKCU\Software\WinlestiaTest\Sandbox\Classes (no shell effect).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;

namespace Winlestia.Shared;

internal sealed class HandlerOptions {
    internal RegistryKey Base = Registry.CurrentUser;
    internal string ClassesPath = @"Software\Classes";
    internal string BackupPath = Path.Combine(FilesHandler.DataDir, "handler-backup.json");
    internal bool Notify = true;           // SHChangeNotify; never for a sandbox root

    // The backup override is honoured only under %TEMP% (anything else is ignored).
    internal static bool UnderTemp(string path) {
        try { return Path.GetFullPath(path).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase); } catch { return false; }
    }

    // Real HKCU unless WINLESTIA_FILES_HANDLER_ROOT is set (test only). The override must live under Software\WinlestiaTest\ and gets its own backup file,
    // so a test can never touch the real Classes keys nor the real backup.
    internal static HandlerOptions FromEnvironment() {
        var o = new HandlerOptions();
        string? root = Environment.GetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT");
        if (string.IsNullOrWhiteSpace(root)) return o;
        root = root.Trim().Trim('\\');
        if (!root.StartsWith(@"Software\WinlestiaTest\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("WINLESTIA_FILES_HANDLER_ROOT must be under Software\\WinlestiaTest\\");
        o.ClassesPath = root + @"\Classes";
        o.Notify = false;
        o.BackupPath = Environment.GetEnvironmentVariable("WINLESTIA_FILES_HANDLER_BACKUP") is { Length: > 0 } b && UnderTemp(b) ? Path.GetFullPath(b) : Path.Combine(FilesHandler.DataDir, "handler-backup.sandbox.json");
        return o;
    }
}

internal sealed record HandlerResult(bool Ok, string Message);

internal static class FilesHandler {
    internal const string Verb = "winlestia", Label = "Ouvrir avec Winlestia Fichiers";
    static readonly string[] Targets = { "Directory", "Drive" };

    internal static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winlestia", "Files");

    sealed class Backup { public int Version { get; set; } = 1; public string Exe { get; set; } = ""; public Dictionary<string, Entry> Entries { get; set; } = new(); }
    sealed class Entry {
        public bool ParentExisted { get; set; }      // <T>
        public bool ShellExisted { get; set; }       // <T>\shell
        public bool DefaultExisted { get; set; }     // (Default) of <T>\shell
        public string? Default { get; set; }
        public string? DefaultKind { get; set; }     // String | ExpandString
        public bool OurKeyExisted { get; set; }      // <T>\shell\winlestia
    }

    [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    static void NotifyAssoc() { try { SHChangeNotify(0x08000000 /*SHCNE_ASSOCCHANGED*/, 0x0000 /*SHCNF_IDLIST*/ | 0x3000 /*SHCNF_FLUSHNOWAIT*/, IntPtr.Zero, IntPtr.Zero); } catch { } }

    static string P(HandlerOptions o, string rel) => o.ClassesPath + "\\" + rel;
    internal static string Command(string exe) => "\"" + exe + "\" \"%1\"";

    // ---- queries -----------------------------------------------------------------------------------------------

    internal static bool BackupExists(HandlerOptions o) => File.Exists(o.BackupPath);

    // True when both Directory and Drive point at exactly this exe.
    internal static bool IsRegistered(HandlerOptions o, string exe) {
        foreach (var t in Targets) {
            using var shell = o.Base.OpenSubKey(P(o, t + @"\shell"));
            using var cmd = o.Base.OpenSubKey(P(o, t + @"\shell\" + Verb + @"\command"));
            if (!string.Equals(shell?.GetValue("") as string, Verb, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(cmd?.GetValue("") as string, Command(exe), StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    // Anything of ours present (a backup file, or a (Default) pointing at our verb): what the crash guard uses to decide whether there is something to undo.
    internal static bool IsActive(HandlerOptions o) {
        if (BackupExists(o)) return true;
        foreach (var t in Targets) {
            using var shell = o.Base.OpenSubKey(P(o, t + @"\shell"));
            if (string.Equals(shell?.GetValue("") as string, Verb, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // ---- register ----------------------------------------------------------------------------------------------

    // Test-only fault injection (target name, before its keys are written). Always null in production.
    internal static Action<string>? FaultHook;

    // Cross-process lock around Register / Restore (backend toggle, CLI, installer, crash guard can overlap). Re-entrant on the same thread.
    static HandlerResult Locked(Func<HandlerResult> body) {
        using var mutex = new Mutex(false, @"Local\WinlestiaFilesHandler");
        bool held = false;
        try { try { held = mutex.WaitOne(15000); } catch (AbandonedMutexException) { held = true; } }
        catch (Exception ex) { return new(false, "lock: " + ex.Message); }
        if (!held) return new(false, "another register/restore is running");
        try { return body(); } finally { try { mutex.ReleaseMutex(); } catch { } }
    }

    internal static HandlerResult Register(HandlerOptions o, string exe) => Locked(() => RegisterCore(o, exe));

    static HandlerResult RegisterCore(HandlerOptions o, string exe) {
        bool touched = false;
        try {
            if (string.IsNullOrWhiteSpace(exe)) return new(false, "no exe");
            exe = Path.GetFullPath(exe.Trim().Trim('"'));
            if (!File.Exists(exe)) return new(false, "exe not found: " + exe);
            var backup = LoadBackup(o.BackupPath);
            bool fresh = backup == null;
            if (backup == null) {                       // idempotent: an existing backup holds the TRUE originals and is never overwritten
                backup = new Backup { Exe = exe };
                foreach (var t in Targets) backup.Entries[t] = Capture(o, t);
                touched = true;
                WriteBackup(o.BackupPath, backup);       // backup first, keys second: a crash in between leaves nothing to undo
            }
            foreach (var t in Targets) {
                FaultHook?.Invoke(t);
                touched = true;
                string verbKey = P(o, t + @"\shell\" + Verb);
                using (var cmd = o.Base.CreateSubKey(verbKey + @"\command")) cmd.SetValue("", Command(exe));
                using (var v = o.Base.CreateSubKey(verbKey)) { v.SetValue("", Label); v.SetValue("MUIVerb", Label); v.SetValue("Icon", "\"" + exe + "\",0"); }
                using (var shell = o.Base.CreateSubKey(P(o, t + @"\shell"))) shell.SetValue("", Verb);   // last: the default never points at a missing verb
            }
            if (o.Notify) NotifyAssoc();
            return new(true, fresh ? "registered" : "registered (existing backup kept)");
        } catch (Exception ex) {
            string extra = "";
            if (touched) { var back = RestoreCore(o); extra = back.Ok ? " (rolled back)" : " (rollback incomplete: " + back.Message + ")"; }
            return new(false, ex.Message + extra);
        }
    }

    // Never records our own traces as "original": a Default that already says winlestia, or an existing verb key of ours, count as not pre-existing.
    static Entry Capture(HandlerOptions o, string t) {
        var e = new Entry();
        using (var parent = o.Base.OpenSubKey(P(o, t))) e.ParentExisted = parent != null;
        using (var shell = o.Base.OpenSubKey(P(o, t + @"\shell"))) {
            e.ShellExisted = shell != null;
            if (shell != null && Array.IndexOf(shell.GetValueNames(), "") >= 0) {
                string? text = shell.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
                if (!string.Equals(text, Verb, StringComparison.OrdinalIgnoreCase)) {
                    e.DefaultExisted = true;
                    e.DefaultKind = shell.GetValueKind("").ToString();
                    e.Default = text;
                }
            }
        }
        e.OurKeyExisted = false;
        return e;
    }

    // ---- restore -----------------------------------------------------------------------------------------------

    internal static HandlerResult Restore(HandlerOptions o) => Locked(() => RestoreCore(o));

    // Per target: (Default) first, then our verb key, so the shell never has a Default naming a missing verb. A Default that is neither ours nor absent was set by someone else since: kept.
    static HandlerResult RestoreCore(HandlerOptions o) {
        var errors = new List<string>();
        bool changed = false;
        Backup? backup = LoadBackup(o.BackupPath);
        foreach (var t in Targets) {
            try {
                Entry? e = null; backup?.Entries.TryGetValue(t, out e);
                string shellPath = P(o, t + @"\shell");
                using (var shell = o.Base.OpenSubKey(shellPath, true)) {
                    if (shell != null) {
                        bool hasDefault = Array.IndexOf(shell.GetValueNames(), "") >= 0;
                        string? cur = hasDefault ? shell.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() : null;
                        bool ours = hasDefault && string.Equals(cur, Verb, StringComparison.OrdinalIgnoreCase);
                        if (e is { DefaultExisted: true } && (!hasDefault || ours)) { SetDefault(shell, e); changed = true; }
                        else if (ours) { shell.DeleteValue("", false); changed = true; }
                        if (shell.OpenSubKey(Verb) is { } v) { v.Dispose(); shell.DeleteSubKeyTree(Verb, false); changed = true; }
                    } else if (e is { DefaultExisted: true }) {        // the shell key vanished meanwhile: put the original value back
                        using var recreated = o.Base.CreateSubKey(shellPath); SetDefault(recreated, e); changed = true;
                    }
                }
                if (e != null) {                                      // remove only the empty keys we created ourselves
                    if (!e.ShellExisted) DeleteIfEmpty(o, shellPath);
                    if (!e.ParentExisted) DeleteIfEmpty(o, P(o, t));
                }
            } catch (Exception ex) { errors.Add(t + ": " + ex.Message); }
        }
        if (errors.Count > 0) return new(false, string.Join("; ", errors));   // keep the backup: a retry still knows the originals
        try { if (File.Exists(o.BackupPath)) File.Delete(o.BackupPath); } catch { }
        if (changed && o.Notify) NotifyAssoc();
        return new(true, backup == null ? (changed ? "restored (no backup: our keys removed)" : "nothing to restore") : "restored");
    }

    static void SetDefault(RegistryKey shell, Entry e) =>
        shell.SetValue("", e.Default ?? "", string.Equals(e.DefaultKind, "ExpandString", StringComparison.OrdinalIgnoreCase) ? RegistryValueKind.ExpandString : RegistryValueKind.String);

    static void DeleteIfEmpty(HandlerOptions o, string path) {
        using (var k = o.Base.OpenSubKey(path)) { if (k == null || k.SubKeyCount > 0 || k.ValueCount > 0) return; }
        o.Base.DeleteSubKey(path, false);
    }

    // ---- backup file -------------------------------------------------------------------------------------------

    static Backup? LoadBackup(string path) {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Backup>(File.ReadAllText(path, Encoding.UTF8)) : null; }
        catch { return null; }                 // unreadable backup: treated as missing (Restore then only removes our own keys)
    }

    static void WriteBackup(string path, Backup b) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(b, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temp, path, true);           // atomic replace on the same volume
    }

    // ---- CLI (Winlestia.Files.exe --register "<exe>" | --restore): exit code 0 = ok, 1 = failure. One line appended to handler.log. -----------------

    internal static int RunCli(string[] args) {
        HandlerResult r;
        string what = args[0].ToLowerInvariant();
        try {
            var o = HandlerOptions.FromEnvironment();
            if (what == "--register") {
                string exe = args.Length > 1 ? args[1] : Environment.ProcessPath ?? "";
                r = Register(o, exe);
            } else r = Restore(o);
        } catch (Exception ex) { r = new(false, ex.Message); }
        Log(what + " " + (r.Ok ? "ok" : "FAILED") + ": " + r.Message);
        return r.Ok ? 0 : 1;
    }

    internal static void Log(string text) {
        try {
            Directory.CreateDirectory(DataDir);
            string file = Path.Combine(DataDir, "handler.log");
            var lines = new List<string>(File.Exists(file) ? File.ReadAllLines(file) : Array.Empty<string>()) { DateTimeOffset.Now.ToString("O") + " " + text };
            if (lines.Count > 100) lines.RemoveRange(0, lines.Count - 100);
            File.WriteAllLines(file, lines);
        } catch { }
    }
}
