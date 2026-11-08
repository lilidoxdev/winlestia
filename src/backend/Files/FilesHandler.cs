// Enregistre / restaure Winlestia Fichiers comme gestionnaire de dossiers (HKCU uniquement), partagé (lié) par Winlestia.Files
// (propriétaire de la CLI --register / --restore),
// le backend (bascule, garde de démarrage) et tests/FilesHandler. .NET pur : pas de WPF, aucun type du backend.
// Propriétaire = Winlestia.Files.exe : c'est le seul artefact que l'installeur livre et que le désinstalleur / la garde de plantage
// peuvent appeler sans backend ni UI.
//
// Clés écrites (sous <Base>\<ClassesPath>, normalement HKCU\Software\Classes), uniquement pour Directory et Drive, JAMAIS Folder :
//   <T>\shell                      (Default) = winlestia
//   <T>\shell\winlestia            (Default) = MUIVerb = "Ouvrir avec Winlestia Fichiers", Icon = "<exe>",0
//   <T>\shell\winlestia\command    (Default) = "<exe>" "%1"
// Tout est relatif à Options, donc les tests tournent sur un bac à sable comme HKCU\Software\WinlestiaTest\Sandbox\Classes (aucun effet
// sur le shell).
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;

namespace Winlestia.Shared;

internal sealed class HandlerOptions
{
    internal RegistryKey Base = Registry.CurrentUser;
    internal string ClassesPath = @"Software\Classes";
    internal string BackupPath = Path.Combine(FilesHandler.DataDir, "handler-backup.json");
    internal bool Notify = true;           // SHChangeNotify ; jamais pour une racine de bac à sable

    // La surcharge de sauvegarde n'est honorée que sous %TEMP% (tout le reste est ignoré).
    internal static bool UnderTemp(string path)
    {
        try
        {
            return Path.GetFullPath(path).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // HKCU réel sauf si WINLESTIA_FILES_HANDLER_ROOT est défini (tests uniquement). La surcharge doit vivre sous Software\WinlestiaTest\
    // et a son propre fichier de sauvegarde,
    // pour qu'un test ne puisse jamais toucher aux vraies clés Classes ni à la vraie sauvegarde.
    internal static HandlerOptions FromEnvironment()
    {
        var o = new HandlerOptions();
        string? root = Environment.GetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT");
        if (string.IsNullOrWhiteSpace(root))
            return o;
        root = root.Trim().Trim('\\');
        if (!root.StartsWith(@"Software\WinlestiaTest\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("WINLESTIA_FILES_HANDLER_ROOT must be under Software\\WinlestiaTest\\");
        o.ClassesPath = root + @"\Classes";
        o.Notify = false;
        o.BackupPath = Environment.GetEnvironmentVariable("WINLESTIA_FILES_HANDLER_BACKUP") is { Length: > 0 } b && UnderTemp(b)
            ? Path.GetFullPath(b) : Path.Combine(FilesHandler.DataDir, "handler-backup.sandbox.json");
        return o;
    }
}

internal sealed record HandlerResult(bool Ok, string Message);

internal static class FilesHandler
{
    internal const string Verb = "winlestia", Label = "Ouvrir avec Winlestia Fichiers";
    static readonly string[] Targets = { "Directory", "Drive" };

    internal static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winlestia",
        "Files");

    sealed class Backup
    {
        public int Version { get; set; } = 1; public string Exe { get; set; } = ""; public Dictionary<string, Entry> Entries { get; set;
            } = new();
    }
    sealed class Entry
    {
        public bool ParentExisted
        {
            get; set;
        }      // <T>
        public bool ShellExisted
        {
            get; set;
        }       // <T>\shell
        public bool DefaultExisted
        {
            get; set;
        }     // (Default) of <T>\shell
        public string? Default
        {
            get; set;
        }
        public string? DefaultKind
        {
            get; set;
        }     // String | ExpandString
        public bool OurKeyExisted
        {
            get; set;
        }      // <T>\shell\winlestia
    }

    [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    static void NotifyAssoc()
    {
        try
        {
            SHChangeNotify(0x08000000 /*SHCNE_ASSOCCHANGED*/, 0x0000 /*SHCNF_IDLIST*/ | 0x3000 /*SHCNF_FLUSHNOWAIT*/, IntPtr.Zero,
                IntPtr.Zero);
        }
        catch (Exception)
        {
            // Notification shell facultative : l'association sera vue au prochain rafraîchissement.
        }
    }

    static string P(HandlerOptions o, string rel) => o.ClassesPath + "\\" + rel;
    internal static string Command(string exe) => "\"" + exe + "\" \"%1\"";

    // ---- queries -----------------------------------------------------------------------------------------------

    internal static bool BackupExists(HandlerOptions o) => File.Exists(o.BackupPath);

    // Vrai quand Directory et Drive pointent tous deux exactement vers cet exe.
    internal static bool IsRegistered(HandlerOptions o, string exe)
    {
        foreach (var t in Targets)
        {
            using var shell = o.Base.OpenSubKey(P(o, t + @"\shell"));
            using var cmd = o.Base.OpenSubKey(P(o, t + @"\shell\" + Verb + @"\command"));
            if (!string.Equals(shell?.GetValue("") as string, Verb, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(cmd?.GetValue("") as string, Command(exe), StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    // Tout ce qui est à nous est présent (un fichier de sauvegarde, ou un (Default) qui pointe vers notre verbe) : ce que la garde de
    // plantage utilise pour décider s'il y a quelque chose à annuler.
    internal static bool IsActive(HandlerOptions o)
    {
        if (BackupExists(o))
            return true;
        foreach (var t in Targets)
        {
            using var shell = o.Base.OpenSubKey(P(o, t + @"\shell"));
            if (string.Equals(shell?.GetValue("") as string, Verb, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // ---- register ----------------------------------------------------------------------------------------------

    // Injection de panne réservée aux tests (nom de la cible, avant l'écriture de ses clés). Toujours null en production.
    internal static Action<string>? FaultHook;

    // Verrou inter-processus autour de Register / Restore (bascule du backend, CLI, installeur, garde de plantage peuvent se chevaucher).
    // Réentrant sur le même thread.
    static HandlerResult Locked(Func<HandlerResult> body)
    {
        using var mutex = new Mutex(false, @"Local\WinlestiaFilesHandler");
        bool held = false;
        try
        {
            try
            {
                held = mutex.WaitOne(15000);
            }
            catch (AbandonedMutexException) { held = true; }
        }
        catch (Exception ex) { return new(false, "lock: " + ex.Message); }
        if (!held)
            return new(false, "another register/restore is running");
        try
        {
            return body();
        }
        finally { try { mutex.ReleaseMutex(); } catch (Exception) { /* Nettoyage au mieux : le mutex a pu être libéré ailleurs. */ } }
    }

    internal static HandlerResult Register(HandlerOptions o, string exe) => Locked(() => RegisterCore(o, exe));

    static HandlerResult RegisterCore(HandlerOptions o, string exe)
    {
        bool touched = false;
        try
        {
            if (string.IsNullOrWhiteSpace(exe))
                return new(false, "no exe");
            exe = Path.GetFullPath(exe.Trim().Trim('"'));
            if (!File.Exists(exe))
                return new(false, "exe not found: " + exe);
            var backup = LoadBackup(o.BackupPath);
            bool fresh = backup == null;
            if (backup == null)
            {                       // idempotent : une sauvegarde existante contient les VRAIS originaux et n'est jamais écrasée
                backup = new Backup { Exe = exe };
                foreach (var t in Targets)
                    backup.Entries[t] = Capture(o, t);
                touched = true;
                // sauvegarde d'abord, clés ensuite : un plantage entre les deux ne laisse rien à annuler
                WriteBackup(o.BackupPath, backup);
            }
            foreach (var t in Targets)
            {
                FaultHook?.Invoke(t);
                touched = true;
                string verbKey = P(o, t + @"\shell\" + Verb);
                using (var cmd = o.Base.CreateSubKey(verbKey + @"\command"))
                    cmd.SetValue("", Command(exe));
                using (var v = o.Base.CreateSubKey(verbKey))
                {
                    v.SetValue("", Label);
                    v.SetValue("MUIVerb", Label);
                    v.SetValue("Icon", "\"" + exe + "\",0");
                }
                using (var shell = o.Base.CreateSubKey(P(o, t + @"\shell")))
                    shell.SetValue("", Verb);   // en dernier : le défaut ne pointe jamais vers un verbe absent
            }
            if (o.Notify)
                NotifyAssoc();
            return new(true, fresh ? "registered" : "registered (existing backup kept)");
        }
        catch (Exception ex)
        {
            string extra = "";
            if (touched)
            {
                var back = RestoreCore(o);
                extra = back.Ok ? " (rolled back)" : " (rollback incomplete: " + back.Message + ")";
            }
            return new(false, ex.Message + extra);
        }
    }

    // N'enregistre jamais nos propres traces comme « original » : un Default qui dit déjà winlestia, ou un verbe à nous existant,
    // compte comme non préexistant.
    static Entry Capture(HandlerOptions o, string t)
    {
        var e = new Entry();
        using (var parent = o.Base.OpenSubKey(P(o, t)))
            e.ParentExisted = parent != null;
        using (var shell = o.Base.OpenSubKey(P(o, t + @"\shell")))
        {
            e.ShellExisted = shell != null;
            if (shell != null && Array.IndexOf(shell.GetValueNames(), "") >= 0)
            {
                string? text = shell.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
                if (!string.Equals(text, Verb, StringComparison.OrdinalIgnoreCase))
                {
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

    // Par cible : (Default) d'abord, puis notre clé de verbe, pour que le shell n'ait jamais un Default nommant un verbe absent. Un
    // Default qui n'est ni le nôtre ni absent a été posé par quelqu'un d'autre depuis : conservé.
    static HandlerResult RestoreCore(HandlerOptions o)
    {
        var errors = new List<string>();
        bool changed = false;
        Backup? backup = LoadBackup(o.BackupPath);
        foreach (var t in Targets)
        {
            try
            {
                Entry? e = null;
                backup?.Entries.TryGetValue(t, out e);
                string shellPath = P(o, t + @"\shell");
                using (var shell = o.Base.OpenSubKey(shellPath, true))
                {
                    if (shell != null)
                    {
                        bool hasDefault = Array.IndexOf(shell.GetValueNames(), "") >= 0;
                        string? cur = hasDefault ? shell.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString()
                            : null;
                        bool ours = hasDefault && string.Equals(cur, Verb, StringComparison.OrdinalIgnoreCase);
                        if (e is { DefaultExisted: true } && (!hasDefault || ours))
                        {
                            SetDefault(shell, e);
                            changed = true;
                        }
                        else if (ours)
                        {
                            shell.DeleteValue("", false);
                            changed = true;
                        }
                        if (shell.OpenSubKey(Verb) is { } v)
                        {
                            v.Dispose();
                            shell.DeleteSubKeyTree(Verb, false);
                            changed = true;
                        }
                    }
                    else if (e is { DefaultExisted: true })
                    {        // la clé du shell a disparu entre-temps : remettre la valeur d'origine
                        using var recreated = o.Base.CreateSubKey(shellPath);
                        SetDefault(recreated, e);
                        changed = true;
                    }
                }
                if (e != null)
                {                                      // ne supprimer que les clés vides que nous avons créées nous-mêmes
                    if (!e.ShellExisted)
                        DeleteIfEmpty(o, shellPath);
                    if (!e.ParentExisted)
                        DeleteIfEmpty(o, P(o, t));
                }
            }
            catch (Exception ex) { errors.Add(t + ": " + ex.Message); }
        }
        if (errors.Count > 0)
            return new(false, string.Join("; ", errors));   // garder la sauvegarde : une nouvelle tentative connaît encore les originaux
        try
        {
            if (File.Exists(o.BackupPath))
                File.Delete(o.BackupPath);
        }
        catch (Exception)
        {
            // Nettoyage au mieux : la sauvegarde a pu disparaître ou être verrouillée.
        }
        if (changed && o.Notify)
            NotifyAssoc();
        return new(true, backup == null ? (changed ? "restored (no backup: our keys removed)" : "nothing to restore") : "restored");
    }

    static void SetDefault(RegistryKey shell, Entry e) =>
        shell.SetValue("", e.Default ?? "", string.Equals(e.DefaultKind, "ExpandString", StringComparison.OrdinalIgnoreCase)
            ? RegistryValueKind.ExpandString : RegistryValueKind.String);

    static void DeleteIfEmpty(HandlerOptions o, string path)
    {
        using (var k = o.Base.OpenSubKey(path))
        {
            if (k == null || k.SubKeyCount > 0 || k.ValueCount > 0)
                return;
        }
        o.Base.DeleteSubKey(path, false);
    }

    // ---- fichier de sauvegarde -------------------------------------------------------------------------------------

    static Backup? LoadBackup(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Backup>(File.ReadAllText(path, Encoding.UTF8)) : null;
        }
        // sauvegarde illisible : traitée comme absente (Restore ne supprime alors que nos propres clés)
        catch { return null; }
    }

    static void WriteBackup(string path, Backup b)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(b, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temp, path, true);           // remplacement atomique sur le même volume
    }

    // ---- CLI (Winlestia.Files.exe --register "<exe>" | --restore) : code de sortie 0 = ok, 1 = échec. Une ligne ajoutée à
    // handler.log.
    // -----------------

    internal static int RunCli(string[] args)
    {
        HandlerResult r;
        string what = args[0].ToLowerInvariant();
        try
        {
            var o = HandlerOptions.FromEnvironment();
            if (what == "--register")
            {
                string exe = args.Length > 1 ? args[1] : Environment.ProcessPath ?? "";
                r = Register(o, exe);
            }
            else
                r = Restore(o);
        }
        catch (Exception ex) { r = new(false, ex.Message); }
        Log(what + " " + (r.Ok ? "ok" : "FAILED") + ": " + r.Message);
        return r.Ok ? 0 : 1;
    }

    internal static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            string file = Path.Combine(DataDir, "handler.log");
            var lines = new List<string>(File.Exists(file) ? File.ReadAllLines(file) : Array.Empty<string>()) {
                DateTimeOffset.Now.ToString("O") + " " + text };
            if (lines.Count > 100)
                lines.RemoveRange(0, lines.Count - 100);
            File.WriteAllLines(file, lines);
        }
        catch (Exception)
        {
            // Le journal ne peut pas se journaliser lui-même.
        }
    }
}
