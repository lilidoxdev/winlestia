// C#12 / FilesHandler : allers-retours register / restore sur une racine de registre SANDBOX
// (HKCU\Software\WinlestiaTest\Sandbox\<run>\Classes, aucun effet shell), plus les décisions du garde-fou de plantage.
// Le vrai HKCU\Software\Classes n'est jamais écrit ; ses valeurs par défaut Directory\shell et Drive\shell sont seulement lues avant/après
// pour prouver qu'elles n'ont pas bougé. Le bac à sable est supprimé à la fin.
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Winlestia.Shared;
namespace Winlestia.Tests;
public class FilesHandlerTests
{
    const string SandboxRoot = @"Software\WinlestiaTest\Sandbox";
    static string run = Guid.NewGuid().ToString("N").Substring(0, 8);
    static string tmp = Path.Combine(Path.GetTempPath(), "WinlestiaFilesHandlerTest-" + run);
    static int n;
    static string? RealDefault(string t)
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + t + @"\shell");
        return k == null ? "<no key>" : (k.GetValue("") as string) ?? "<no value>";
    }
    static HandlerOptions Fresh()
    {
        n++;
        string root = SandboxRoot + @"\" + run + @"\c" + n;
        return new HandlerOptions { Base = Registry.CurrentUser, ClassesPath = root + @"\Classes",
            BackupPath = Path.Combine(tmp, "b" + n + ".json"), Notify = false };
    }
    static RegistryKey? Open(HandlerOptions o, string rel) => o.Base.OpenSubKey(o.ClassesPath + @"\" + rel);
    static string? Def(HandlerOptions o, string rel)
    {
        using var k = Open(o, rel);
        return k?.GetValue("") as string;
    }
    static bool HasDef(HandlerOptions o, string rel)
    {
        using var k = Open(o, rel);
        return k != null && Array.IndexOf(k.GetValueNames(), "") >= 0;
    }
    static bool Exists(HandlerOptions o, string rel)
    {
        using var k = Open(o, rel);
        return k != null;
    }
    static void Seed(HandlerOptions o, string rel, string? def = null, RegistryValueKind kind = RegistryValueKind.String)
    {
        using var k = o.Base.CreateSubKey(o.ClassesPath + @"\" + rel);
        if (def != null)
            k.SetValue("", def, kind);
    }
    static string Cmd(string exe) => "\"" + exe + "\" \"%1\"";
    static void DeleteIfEmpty(string path)
    {
        bool empty;
        using (var key = Registry.CurrentUser.OpenSubKey(path))
            empty = key != null && key.SubKeyCount == 0 && key.ValueCount == 0;
        if (empty)
            Registry.CurrentUser.DeleteSubKey(path, false);
    }
    [Fact]
    public void AllChecks()
    {
        Directory.CreateDirectory(tmp);
        string exe = Path.Combine(tmp, "Winlestia.Files.exe");
        File.WriteAllText(exe, "x");
        string exe2 = Path.Combine(tmp, "Other.exe");
        File.WriteAllText(exe2, "x");
        string realDirBefore = RealDefault("Directory"), realDriveBefore = RealDefault("Drive");
        try
        {
            // 1. no prior values
            {
                var o = Fresh();
                var res = FilesHandler.Register(o, exe);
                Assert.True(res.Ok, "noPrior_registerOk");
                Assert.True(Def(o, @"Directory\shell") == "winlestia" && Def(o, @"Drive\shell") == "winlestia", "noPrior_directoryDefault");
                Assert.True(Def(o, @"Directory\shell\winlestia\command") == Cmd(exe)
                    && Def(o, @"Drive\shell\winlestia\command") == Cmd(exe), "noPrior_command");
                Assert.True(Def(o, @"Directory\shell\winlestia") == "Ouvrir avec Winlestia Fichiers", "noPrior_label");
                using (var k = Open(o, @"Directory\shell\winlestia"))
                    Assert.True((k?.GetValue("MUIVerb") as string) == "Ouvrir avec Winlestia Fichiers", "noPrior_muiverb");
                Assert.True(!Exists(o, @"Folder"), "noPrior_folderUntouched");
                Assert.True(FilesHandler.IsRegistered(o, exe) && !FilesHandler.IsRegistered(o, exe2), "noPrior_isRegistered");
                Assert.True(File.Exists(o.BackupPath) && !File.Exists(o.BackupPath + ".tmp"), "noPrior_backupWritten");
                var back = FilesHandler.Restore(o);
                Assert.True(back.Ok, "noPrior_restoreOk");
                Assert.True(!Exists(o, "Directory") && !Exists(o, "Drive"), "noPrior_keysGone");
                Assert.True(!File.Exists(o.BackupPath), "noPrior_backupDeleted");
                Assert.True(!FilesHandler.IsRegistered(o, exe) && !FilesHandler.IsActive(o), "noPrior_notRegisteredAfter");
            }
            // 2. valeurs préexistantes + autres verbes
            {
                var o = Fresh();
                Seed(o, @"Directory\shell", "none");
                Seed(o, @"Directory\shell\foo\command", "C:\\foo.exe \"%1\"");
                Seed(o, @"Drive\shell");
                Seed(o, @"Drive\shell\bar\command", "bar.exe");
                Seed(o, @"Directory\Background\shell\baz\command", "baz.exe");
                FilesHandler.Register(o, exe);
                Assert.True(Def(o, @"Directory\shell") == "winlestia" && Def(o, @"Drive\shell") == "winlestia", "prior_registered");
                var back = FilesHandler.Restore(o);
                Assert.True(back.Ok, "prior_restoreOk");
                Assert.True(Def(o, @"Directory\shell") == "none", "prior_directoryDefaultBack");
                Assert.True(!HasDef(o, @"Drive\shell") && Exists(o, @"Drive\shell"), "prior_driveDefaultAbsent");
                Assert.True(!Exists(o, @"Directory\shell\winlestia") && !Exists(o, @"Drive\shell\winlestia"), "prior_ourSubkeysGone");
                Assert.True(Def(o, @"Directory\shell\foo\command") == "C:\\foo.exe \"%1\""
                    && Def(o, @"Drive\shell\bar\command") == "bar.exe" && Def(o, @"Directory\Background\shell\baz\command") == "baz.exe",
                    "prior_otherVerbsIntact");
            }
            // 3. valeur par défaut ExpandString conservée non développée
            {
                var o = Fresh();
                Seed(o, @"Directory\shell", @"%SystemRoot%\x", RegistryValueKind.ExpandString);
                FilesHandler.Register(o, exe);
                FilesHandler.Restore(o);
                using var k = Open(o, @"Directory\shell");
                Assert.True(k != null && k.GetValueKind("") == RegistryValueKind.ExpandString
                    && (k.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string) == @"%SystemRoot%\x",
                    "expand_kindAndTextKept");
            }
            // 4. double register idempotent (le second, avec un autre exe, garde les VRAIS originaux)
            {
                var o = Fresh();
                Seed(o, @"Directory\shell", "none");
                FilesHandler.Register(o, exe);
                string first = File.ReadAllText(o.BackupPath);
                var second = FilesHandler.Register(o, exe2);
                Assert.True(second.Ok, "double_secondOk");
                Assert.True(File.ReadAllText(o.BackupPath) == first, "double_backupUnchanged");
                Assert.True(Def(o, @"Directory\shell\winlestia\command") == Cmd(exe2) && FilesHandler.IsRegistered(o, exe2),
                    "double_commandFollowsNewExe");
                FilesHandler.Restore(o);
                Assert.True(Def(o, @"Directory\shell") == "none" && !Exists(o, @"Directory\shell\winlestia"),
                    "double_restoreGivesOriginal");
                Assert.True(FilesHandler.Restore(o).Ok && Def(o, @"Directory\shell") == "none", "double_restoreTwiceOk");
            }
            // 5. restore sans sauvegarde
            {
                var o = Fresh();
                Seed(o, @"Directory\shell\foo\command", "foo.exe");
                FilesHandler.Register(o, exe);
                File.Delete(o.BackupPath);
                var res = FilesHandler.Restore(o);
                Assert.True(res.Ok, "nobackup_ok");
                Assert.True(!Exists(o, @"Directory\shell\winlestia") && !Exists(o, @"Drive\shell\winlestia")
                    && !HasDef(o, @"Directory\shell") && !HasDef(o, @"Drive\shell"), "nobackup_ourKeysAndDefaultGone");
                Assert.True(Def(o, @"Directory\shell\foo\command") == "foo.exe", "nobackup_foreignVerbKept");
            }
            {
                var o = Fresh();
                Seed(o, @"Directory\shell", "keepme");
                Seed(o, @"Directory\shell\winlestia\command", "x.exe");
                FilesHandler.Restore(o);
                Assert.True(Def(o, @"Directory\shell") == "keepme" && !Exists(o, @"Directory\shell\winlestia"),
                    "nobackup_foreignDefaultKept");
            }
            {
                var o = Fresh();
                var res = FilesHandler.Restore(o);
                Assert.True(res.Ok && !Exists(o, "Directory") && !Exists(o, "Drive"), "nothing_restoreOk");
            }
            // 6. une sauvegarde illisible se comporte comme une sauvegarde absente
            {
                var o = Fresh();
                FilesHandler.Register(o, exe);
                File.WriteAllText(o.BackupPath, "{not json");
                Assert.True(FilesHandler.Restore(o).Ok && !Exists(o, @"Directory\shell\winlestia") && !HasDef(o, @"Directory\shell")
                    && !File.Exists(o.BackupPath), "corruptBackup_restoreOk");
            }
            // 7. register refuse un exe manquant et n'écrit rien
            {
                var o = Fresh();
                var res = FilesHandler.Register(o, Path.Combine(tmp, "nope.exe"));
                Assert.True(!res.Ok && !Exists(o, "Directory") && !File.Exists(o.BackupPath), "missingExe_refused");
            }
            // 8. surcharge d'environnement : uniquement sous Software\WinlestiaTest\, ne notifie jamais, sauvegarde propre
            {
                string? old = Environment.GetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT");
                try
                {
                    Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT", SandboxRoot + @"\" + run + @"\env");
                    var o = HandlerOptions.FromEnvironment();
                    Assert.True(o.ClassesPath == SandboxRoot + @"\" + run + @"\env\Classes" && !o.Notify
                        && o.BackupPath.EndsWith("handler-backup.sandbox.json"), "env_overrideApplied");
                    Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT", @"Software\Classes");
                    bool threw = false;
                    try
                    {
                        HandlerOptions.FromEnvironment();
                    }
                    catch (InvalidOperationException) { threw = true; }
                    Assert.True(threw, "env_realRootRefused");
                    Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT", null);
                    var d = HandlerOptions.FromEnvironment();
                    Assert.True(d.ClassesPath == @"Software\Classes" && d.Notify && d.BackupPath.EndsWith("handler-backup.json"),
                        "env_defaultIsRealClasses");
                }
                finally { Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT", old); }
            }
            // 9. garde-fou de plantage, décisions pures
            {
                long t = 1_000_000;
                Assert.True(!FilesHandlerGuard.ShouldTrip(new long[] { t - 20000, t }, t), "guard_twoNotEnough");
                Assert.True(FilesHandlerGuard.ShouldTrip(new long[] { t - 59000, t - 30000, t }, t), "guard_threeInWindowTrips");
                Assert.True(FilesHandlerGuard.ShouldTrip(new long[] { t - 60000, t - 1000, t }, t), "guard_exactly60sCounts");
                Assert.True(!FilesHandlerGuard.ShouldTrip(new long[] { t - 61000, t - 1000, t }, t), "guard_61sIsOutside");
                Assert.True(FilesHandlerGuard.Prune(new long[] { t - 90000, t - 10, t }, t).Count == 2, "guard_pruneKeepsWindow");
                Assert.True(FilesHandlerGuard.DecideDefaults(false, true, false) == (true, true), "defaults_firstRunEnables");
                Assert.True(FilesHandlerGuard.DecideDefaults(true, true, false) == (false, false)
                    && FilesHandlerGuard.DecideDefaults(true, false, true) == (false, false), "defaults_alreadyDoneNever");
                Assert.True(FilesHandlerGuard.DecideDefaults(false, false, false) == (false, false), "defaults_exeMissingRetries");
                Assert.True(FilesHandlerGuard.DecideDefaults(false, true, true) == (false, true), "defaults_trippedMarksOnly");
                Assert.True(FilesHandlerGuard.DecideStartup(false, true, false) == HandlerStartup.Nothing
                    && FilesHandlerGuard.DecideStartup(false, false, true) == HandlerStartup.Nothing, "decide_prefOffNothing");
                Assert.True(FilesHandlerGuard.DecideStartup(true, false, false) == HandlerStartup.RestoreAndDisable,
                    "decide_exeMissingRestores");
                Assert.True(FilesHandlerGuard.DecideStartup(true, true, true) == HandlerStartup.TrippedDisable, "decide_trippedDisables");
                Assert.True(FilesHandlerGuard.DecideStartup(true, true, false) == HandlerStartup.Ensure, "decide_okEnsures");
            }
            // 10. garde-fou de plantage avec fichiers + registre bac à sable
            {
                var o = Fresh();
                Seed(o, @"Directory\shell", "none");
                FilesHandler.Register(o, exe);
                string dir = Path.Combine(tmp, "guard1");
                long t = 5_000_000;
                bool a = FilesHandlerGuard.RecordStartupCrash(dir, t, o), b = FilesHandlerGuard.RecordStartupCrash(dir, t + 1000, o);
                Assert.True(!a && !b && Def(o, @"Directory\shell") == "winlestia"
                    && !File.Exists(Path.Combine(dir, FilesHandlerGuard.FlagFile)), "guardIO_twoDoNotTrip");
                bool c = FilesHandlerGuard.RecordStartupCrash(dir, t + 2000, o);
                Assert.True(c && Def(o, @"Directory\shell") == "none" && !Exists(o, @"Directory\shell\winlestia")
                    && File.Exists(Path.Combine(dir, FilesHandlerGuard.FlagFile)), "guardIO_thirdTripsAndRestores");
                Assert.True(FilesHandlerGuard.ConsumeFlag(dir) && !FilesHandlerGuard.ConsumeFlag(dir), "guardIO_flagConsumedOnce");
            }
            {
                var o = Fresh();
                string dir = Path.Combine(tmp, "guard2");
                long t = 9_000_000;
                bool any = false;
                for (int i = 0; i < 5; i++)
                    any |= FilesHandlerGuard.RecordStartupCrash(dir, t + i * 1000, o);
                Assert.True(!any && !File.Exists(Path.Combine(dir, FilesHandlerGuard.FlagFile)), "guardIO_notEnabledNoFlag");
            }
            {
                var o = Fresh();
                FilesHandler.Register(o, exe);
                string dir = Path.Combine(tmp, "guard3");
                long t = 7_000_000;
                bool any = false;
                for (int i = 0; i < 3; i++)
                    any |= FilesHandlerGuard.RecordStartupCrash(dir, t + i * 40000, o);
                Assert.True(!any && FilesHandler.IsRegistered(o, exe), "guardIO_spreadCrashesDoNotTrip");
            }
            // 11. (revue 2) notre propre valeur n'est jamais sauvegardée : Default déjà "winlestia" + sauvegarde manquante -> restore
            // laisse Default absent
            {
                var o = Fresh();
                Seed(o, @"Directory\shell", "winlestia");
                Seed(o, @"Directory\shell\winlestia\command", "old.exe");
                Seed(o, @"Directory\shell\foo\command", "foo.exe");
                FilesHandler.Register(o, exe);
                File.Delete(o.BackupPath);   // la sauvegarde capturée au 2e register aurait été fausse ; ici : on la perd, restore sans
                FilesHandler.Restore(o);
                Assert.True(!HasDef(o, @"Directory\shell") && !Exists(o, @"Directory\shell\winlestia")
                    && Def(o, @"Directory\shell\foo\command") == "foo.exe", "ownValue_restoreLeavesDefaultAbsent");
            }
            {
                var o = Fresh();
                Seed(o, @"Directory\shell", "winlestia");
                Seed(o, @"Directory\shell\winlestia\command", "old.exe");
                FilesHandler.Register(o, exe);
                FilesHandler.Restore(o);
                Assert.True(!HasDef(o, @"Directory\shell") && !Exists(o, @"Directory\shell\winlestia"), "ownValue_withBackupAlsoAbsent");
            }
            // 12. (revue 3) un échec pendant l'écriture de Drive annule Directory
            {
                var o = Fresh();
                Seed(o, @"Directory\shell", "none");
                FilesHandler.FaultHook = t => { if (t == "Drive") throw new InvalidOperationException("boom"); };
                HandlerResult res;
                try
                {
                    res = FilesHandler.Register(o, exe);
                }
                finally { FilesHandler.FaultHook = null; }
                Assert.True(!res.Ok && res.Message.Contains("boom") && res.Message.Contains("rolled back"), "rollback_reportsFailure");
                Assert.True(Def(o, @"Directory\shell") == "none" && !Exists(o, @"Directory\shell\winlestia") && !Exists(o, "Drive")
                    && !File.Exists(o.BackupPath), "rollback_directoryRestored");
            }
            // 13. (revue 4) un Default étranger posé après register est conservé, notre clé de verbe part quand même
            {
                var o = Fresh();
                Seed(o, @"Drive\shell");
                FilesHandler.Register(o, exe);
                using (var k = o.Base.CreateSubKey(o.ClassesPath + @"\Drive\shell"))
                    k.SetValue("", "foreign");
                FilesHandler.Restore(o);
                Assert.True(Def(o, @"Drive\shell") == "foreign" && !Exists(o, @"Drive\shell\winlestia"), "foreignDefault_kept");
            }
            {
                var o = Fresh();
                Seed(o, @"Directory\shell", "none");
                FilesHandler.Register(o, exe);
                using (var k = o.Base.CreateSubKey(o.ClassesPath + @"\Directory\shell"))
                    k.SetValue("", "foreign");
                FilesHandler.Restore(o);
                Assert.True(Def(o, @"Directory\shell") == "foreign", "foreignDefault_keptEvenWithPriorValue");
            }
            // 14. (revue 6) seuls les vrais plantages comptent
            Assert.True(FilesHandlerGuard.CountsAsCrash(new NullReferenceException())
                && FilesHandlerGuard.CountsAsCrash(new InvalidOperationException())
                && FilesHandlerGuard.CountsAsCrash(new OutOfMemoryException()), "counts_nullRefCounts");
            Assert.True(!FilesHandlerGuard.CountsAsCrash(new UnauthorizedAccessException())
                && !FilesHandlerGuard.CountsAsCrash(new FileNotFoundException())
                && !FilesHandlerGuard.CountsAsCrash(new DirectoryNotFoundException())
                && !FilesHandlerGuard.CountsAsCrash(new PathTooLongException()) && !FilesHandlerGuard.CountsAsCrash(new ArgumentException())
                && !FilesHandlerGuard.CountsAsCrash(new NotSupportedException()), "counts_pathProblemsDoNot");
            // 15. (revue 8) surcharge de sauvegarde uniquement sous %TEMP%
            {
                string? oldRoot = Environment.GetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT"),
                    oldB = Environment.GetEnvironmentVariable("WINLESTIA_FILES_HANDLER_BACKUP");
                try
                {
                    Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT", SandboxRoot + @"\" + run + @"\env2");
                    Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_BACKUP", @"C:\Windows\evil.json");
                    Assert.True(HandlerOptions.FromEnvironment().BackupPath.EndsWith("handler-backup.sandbox.json"),
                        "backupEnv_outsideTempIgnored");
                    string mine = Path.Combine(tmp, "mine.json");
                    Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_BACKUP", mine);
                    Assert.True(HandlerOptions.FromEnvironment().BackupPath == Path.GetFullPath(mine), "backupEnv_underTempHonoured");
                }
                finally { Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT",
                    oldRoot); Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_BACKUP", oldB); }
            }
        }
        finally
        {
            // Nettoyage de la sandbox HKCU : aucune erreur avalee (DeleteSubKey*(..., false) tolere l'absence).
            using (var k = Registry.CurrentUser.OpenSubKey(SandboxRoot, true))
                k?.DeleteSubKeyTree(run, false);
            DeleteIfEmpty(SandboxRoot);
            DeleteIfEmpty(@"Software\WinlestiaTest");
            TempDir.Delete(tmp);
        }
        using (var chk = Registry.CurrentUser.OpenSubKey(SandboxRoot + @"\" + run))
            Assert.True(chk == null, "sandbox_deleted");
        Assert.True(RealDefault("Directory") == realDirBefore && RealDefault("Drive") == realDriveBefore, "realHkcu_unchanged");
    }
}
