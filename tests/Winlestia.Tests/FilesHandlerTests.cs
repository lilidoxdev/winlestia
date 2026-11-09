// C#12 / FilesHandler: register / restore round-trips on a SANDBOX registry root (HKCU\Software\WinlestiaTest\Sandbox\<run>\Classes, no
// shell effect), plus the crash-guard decisions.
// The real HKCU\Software\Classes is never written; its Directory\shell and Drive\shell defaults are only read before/after to prove they
// did not move. The sandbox is deleted at the end.
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
            // 2. prior values + other verbs
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
            // 3. ExpandString default kept unexpanded
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
            // 4. double register idempotent (the second one, with another exe, keeps the TRUE originals)
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
            // 5. restore without backup
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
            // 6. unreadable backup behaves like a missing one
            {
                var o = Fresh();
                FilesHandler.Register(o, exe);
                File.WriteAllText(o.BackupPath, "{not json");
                Assert.True(FilesHandler.Restore(o).Ok && !Exists(o, @"Directory\shell\winlestia") && !HasDef(o, @"Directory\shell")
                    && !File.Exists(o.BackupPath), "corruptBackup_restoreOk");
            }
            // 7. register refuses a missing exe and writes nothing
            {
                var o = Fresh();
                var res = FilesHandler.Register(o, Path.Combine(tmp, "nope.exe"));
                Assert.True(!res.Ok && !Exists(o, "Directory") && !File.Exists(o.BackupPath), "missingExe_refused");
            }
            // 8. environment override: only under Software\WinlestiaTest\, never notifies, own backup
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
            // 9. crash guard, pure decisions
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
            // 10. crash guard with files + sandbox registry
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
            // 11. (review 2) our own value is never backed up: Default already "winlestia" + missing backup -> restore leaves Default
            // absent
            {
                var o = Fresh();
                Seed(o, @"Directory\shell", "winlestia");
                Seed(o, @"Directory\shell\winlestia\command", "old.exe");
                Seed(o, @"Directory\shell\foo\command", "foo.exe");
                FilesHandler.Register(o, exe);
                File.Delete(o.BackupPath);   // backup captured at the 2nd register would have been wrong; here: lose it, restore without
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
            // 12. (review 3) a failure while writing Drive rolls Directory back
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
            // 13. (review 4) a foreign Default set after register is kept, our verb key still goes
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
            // 14. (review 6) only real crashes count
            Assert.True(FilesHandlerGuard.CountsAsCrash(new NullReferenceException())
                && FilesHandlerGuard.CountsAsCrash(new InvalidOperationException())
                && FilesHandlerGuard.CountsAsCrash(new OutOfMemoryException()), "counts_nullRefCounts");
            Assert.True(!FilesHandlerGuard.CountsAsCrash(new UnauthorizedAccessException())
                && !FilesHandlerGuard.CountsAsCrash(new FileNotFoundException())
                && !FilesHandlerGuard.CountsAsCrash(new DirectoryNotFoundException())
                && !FilesHandlerGuard.CountsAsCrash(new PathTooLongException()) && !FilesHandlerGuard.CountsAsCrash(new ArgumentException())
                && !FilesHandlerGuard.CountsAsCrash(new NotSupportedException()), "counts_pathProblemsDoNot");
            // 15. (review 8) backup override only under %TEMP%
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
