// C#12 / FilesHandler: register / restore round-trips on a SANDBOX registry root (HKCU\Software\WinlestiaTest\Sandbox\<run>\Classes, no shell effect), plus the crash-guard decisions.
// The real HKCU\Software\Classes is never written; its Directory\shell and Drive\shell defaults are only read before/after to prove they did not move. The sandbox is deleted at the end.
using System;using System.Collections.Generic;using System.IO;using Microsoft.Win32;using Winlestia.Shared;
class Test{
const string SandboxRoot=@"Software\WinlestiaTest\Sandbox";
static string run=Guid.NewGuid().ToString("N").Substring(0,8);
static string tmp=Path.Combine(Path.GetTempPath(),"WinlestiaFilesHandlerTest-"+run);
static int n;
static string? RealDefault(string t){using var k=Registry.CurrentUser.OpenSubKey(@"Software\Classes\"+t+@"\shell");return k==null?"<no key>":(k.GetValue("") as string)??"<no value>";}
static HandlerOptions Fresh(){n++;string root=SandboxRoot+@"\"+run+@"\c"+n;return new HandlerOptions{Base=Registry.CurrentUser,ClassesPath=root+@"\Classes",BackupPath=Path.Combine(tmp,"b"+n+".json"),Notify=false};}
static RegistryKey? Open(HandlerOptions o,string rel)=>o.Base.OpenSubKey(o.ClassesPath+@"\"+rel);
static string? Def(HandlerOptions o,string rel){using var k=Open(o,rel);return k?.GetValue("") as string;}
static bool HasDef(HandlerOptions o,string rel){using var k=Open(o,rel);return k!=null&&Array.IndexOf(k.GetValueNames(),"")>=0;}
static bool Exists(HandlerOptions o,string rel){using var k=Open(o,rel);return k!=null;}
static void Seed(HandlerOptions o,string rel,string? def=null,RegistryValueKind kind=RegistryValueKind.String){using var k=o.Base.CreateSubKey(o.ClassesPath+@"\"+rel);if(def!=null)k.SetValue("",def,kind);}
static string Cmd(string exe)=>"\""+exe+"\" \"%1\"";
static int Main(){var r=new Dictionary<string,bool>();
Directory.CreateDirectory(tmp);string exe=Path.Combine(tmp,"Winlestia.Files.exe");File.WriteAllText(exe,"x");string exe2=Path.Combine(tmp,"Other.exe");File.WriteAllText(exe2,"x");
string realDirBefore=RealDefault("Directory"),realDriveBefore=RealDefault("Drive");
try{
// 1. no prior values
{var o=Fresh();var res=FilesHandler.Register(o,exe);
 r["noPrior_registerOk"]=res.Ok;
 r["noPrior_directoryDefault"]=Def(o,@"Directory\shell")=="winlestia"&&Def(o,@"Drive\shell")=="winlestia";
 r["noPrior_command"]=Def(o,@"Directory\shell\winlestia\command")==Cmd(exe)&&Def(o,@"Drive\shell\winlestia\command")==Cmd(exe);
 r["noPrior_label"]=Def(o,@"Directory\shell\winlestia")=="Ouvrir avec Winlestia Fichiers";
 using(var k=Open(o,@"Directory\shell\winlestia"))r["noPrior_muiverb"]=(k?.GetValue("MUIVerb") as string)=="Ouvrir avec Winlestia Fichiers";
 r["noPrior_folderUntouched"]=!Exists(o,@"Folder");
 r["noPrior_isRegistered"]=FilesHandler.IsRegistered(o,exe)&&!FilesHandler.IsRegistered(o,exe2);
 r["noPrior_backupWritten"]=File.Exists(o.BackupPath)&&!File.Exists(o.BackupPath+".tmp");
 var back=FilesHandler.Restore(o);
 r["noPrior_restoreOk"]=back.Ok;
 r["noPrior_keysGone"]=!Exists(o,"Directory")&&!Exists(o,"Drive");
 r["noPrior_backupDeleted"]=!File.Exists(o.BackupPath);
 r["noPrior_notRegisteredAfter"]=!FilesHandler.IsRegistered(o,exe)&&!FilesHandler.IsActive(o);}
// 2. prior values + other verbs
{var o=Fresh();
 Seed(o,@"Directory\shell","none");Seed(o,@"Directory\shell\foo\command","C:\\foo.exe \"%1\"");
 Seed(o,@"Drive\shell");Seed(o,@"Drive\shell\bar\command","bar.exe");
 Seed(o,@"Directory\Background\shell\baz\command","baz.exe");
 FilesHandler.Register(o,exe);
 r["prior_registered"]=Def(o,@"Directory\shell")=="winlestia"&&Def(o,@"Drive\shell")=="winlestia";
 var back=FilesHandler.Restore(o);
 r["prior_restoreOk"]=back.Ok;
 r["prior_directoryDefaultBack"]=Def(o,@"Directory\shell")=="none";
 r["prior_driveDefaultAbsent"]=!HasDef(o,@"Drive\shell")&&Exists(o,@"Drive\shell");
 r["prior_ourSubkeysGone"]=!Exists(o,@"Directory\shell\winlestia")&&!Exists(o,@"Drive\shell\winlestia");
 r["prior_otherVerbsIntact"]=Def(o,@"Directory\shell\foo\command")=="C:\\foo.exe \"%1\""&&Def(o,@"Drive\shell\bar\command")=="bar.exe"&&Def(o,@"Directory\Background\shell\baz\command")=="baz.exe";}
// 3. ExpandString default kept unexpanded
{var o=Fresh();Seed(o,@"Directory\shell",@"%SystemRoot%\x",RegistryValueKind.ExpandString);
 FilesHandler.Register(o,exe);FilesHandler.Restore(o);
 using var k=Open(o,@"Directory\shell");
 r["expand_kindAndTextKept"]=k!=null&&k.GetValueKind("")==RegistryValueKind.ExpandString&&(k.GetValue("",null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string)==@"%SystemRoot%\x";}
// 4. double register idempotent (the second one, with another exe, keeps the TRUE originals)
{var o=Fresh();Seed(o,@"Directory\shell","none");
 FilesHandler.Register(o,exe);string first=File.ReadAllText(o.BackupPath);
 var second=FilesHandler.Register(o,exe2);
 r["double_secondOk"]=second.Ok;
 r["double_backupUnchanged"]=File.ReadAllText(o.BackupPath)==first;
 r["double_commandFollowsNewExe"]=Def(o,@"Directory\shell\winlestia\command")==Cmd(exe2)&&FilesHandler.IsRegistered(o,exe2);
 FilesHandler.Restore(o);
 r["double_restoreGivesOriginal"]=Def(o,@"Directory\shell")=="none"&&!Exists(o,@"Directory\shell\winlestia");
 r["double_restoreTwiceOk"]=FilesHandler.Restore(o).Ok&&Def(o,@"Directory\shell")=="none";}
// 5. restore without backup
{var o=Fresh();Seed(o,@"Directory\shell\foo\command","foo.exe");
 FilesHandler.Register(o,exe);File.Delete(o.BackupPath);
 var res=FilesHandler.Restore(o);
 r["nobackup_ok"]=res.Ok;
 r["nobackup_ourKeysAndDefaultGone"]=!Exists(o,@"Directory\shell\winlestia")&&!Exists(o,@"Drive\shell\winlestia")&&!HasDef(o,@"Directory\shell")&&!HasDef(o,@"Drive\shell");
 r["nobackup_foreignVerbKept"]=Def(o,@"Directory\shell\foo\command")=="foo.exe";}
{var o=Fresh();Seed(o,@"Directory\shell","keepme");Seed(o,@"Directory\shell\winlestia\command","x.exe");
 FilesHandler.Restore(o);
 r["nobackup_foreignDefaultKept"]=Def(o,@"Directory\shell")=="keepme"&&!Exists(o,@"Directory\shell\winlestia");}
{var o=Fresh();var res=FilesHandler.Restore(o);r["nothing_restoreOk"]=res.Ok&&!Exists(o,"Directory")&&!Exists(o,"Drive");}
// 6. unreadable backup behaves like a missing one
{var o=Fresh();FilesHandler.Register(o,exe);File.WriteAllText(o.BackupPath,"{not json");
 r["corruptBackup_restoreOk"]=FilesHandler.Restore(o).Ok&&!Exists(o,@"Directory\shell\winlestia")&&!HasDef(o,@"Directory\shell")&&!File.Exists(o.BackupPath);}
// 7. register refuses a missing exe and writes nothing
{var o=Fresh();var res=FilesHandler.Register(o,Path.Combine(tmp,"nope.exe"));r["missingExe_refused"]=!res.Ok&&!Exists(o,"Directory")&&!File.Exists(o.BackupPath);}
// 8. environment override: only under Software\WinlestiaTest\, never notifies, own backup
{string? old=Environment.GetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT");
 try{Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT",SandboxRoot+@"\"+run+@"\env");var o=HandlerOptions.FromEnvironment();
  r["env_overrideApplied"]=o.ClassesPath==SandboxRoot+@"\"+run+@"\env\Classes"&&!o.Notify&&o.BackupPath.EndsWith("handler-backup.sandbox.json");
  Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT",@"Software\Classes");bool threw=false;try{HandlerOptions.FromEnvironment();}catch(InvalidOperationException){threw=true;}
  r["env_realRootRefused"]=threw;
  Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT",null);var d=HandlerOptions.FromEnvironment();
  r["env_defaultIsRealClasses"]=d.ClassesPath==@"Software\Classes"&&d.Notify&&d.BackupPath.EndsWith("handler-backup.json");}
 finally{Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT",old);}}
// 9. crash guard, pure decisions
{long t=1_000_000;
 r["guard_twoNotEnough"]=!FilesHandlerGuard.ShouldTrip(new long[]{t-20000,t},t);
 r["guard_threeInWindowTrips"]=FilesHandlerGuard.ShouldTrip(new long[]{t-59000,t-30000,t},t);
 r["guard_exactly60sCounts"]=FilesHandlerGuard.ShouldTrip(new long[]{t-60000,t-1000,t},t);
 r["guard_61sIsOutside"]=!FilesHandlerGuard.ShouldTrip(new long[]{t-61000,t-1000,t},t);
 r["guard_pruneKeepsWindow"]=FilesHandlerGuard.Prune(new long[]{t-90000,t-10,t},t).Count==2;
 r["defaults_firstRunEnables"]=FilesHandlerGuard.DecideDefaults(false,true,false)==(true,true);
 r["defaults_alreadyDoneNever"]=FilesHandlerGuard.DecideDefaults(true,true,false)==(false,false)&&FilesHandlerGuard.DecideDefaults(true,false,true)==(false,false);
 r["defaults_exeMissingRetries"]=FilesHandlerGuard.DecideDefaults(false,false,false)==(false,false);
 r["defaults_trippedMarksOnly"]=FilesHandlerGuard.DecideDefaults(false,true,true)==(false,true);
 r["decide_prefOffNothing"]=FilesHandlerGuard.DecideStartup(false,true,false)==HandlerStartup.Nothing&&FilesHandlerGuard.DecideStartup(false,false,true)==HandlerStartup.Nothing;
 r["decide_exeMissingRestores"]=FilesHandlerGuard.DecideStartup(true,false,false)==HandlerStartup.RestoreAndDisable;
 r["decide_trippedDisables"]=FilesHandlerGuard.DecideStartup(true,true,true)==HandlerStartup.TrippedDisable;
 r["decide_okEnsures"]=FilesHandlerGuard.DecideStartup(true,true,false)==HandlerStartup.Ensure;}
// 10. crash guard with files + sandbox registry
{var o=Fresh();Seed(o,@"Directory\shell","none");FilesHandler.Register(o,exe);string dir=Path.Combine(tmp,"guard1");long t=5_000_000;
 bool a=FilesHandlerGuard.RecordStartupCrash(dir,t,o),b=FilesHandlerGuard.RecordStartupCrash(dir,t+1000,o);
 r["guardIO_twoDoNotTrip"]=!a&&!b&&Def(o,@"Directory\shell")=="winlestia"&&!File.Exists(Path.Combine(dir,FilesHandlerGuard.FlagFile));
 bool c=FilesHandlerGuard.RecordStartupCrash(dir,t+2000,o);
 r["guardIO_thirdTripsAndRestores"]=c&&Def(o,@"Directory\shell")=="none"&&!Exists(o,@"Directory\shell\winlestia")&&File.Exists(Path.Combine(dir,FilesHandlerGuard.FlagFile));
 r["guardIO_flagConsumedOnce"]=FilesHandlerGuard.ConsumeFlag(dir)&&!FilesHandlerGuard.ConsumeFlag(dir);}
{var o=Fresh();string dir=Path.Combine(tmp,"guard2");long t=9_000_000;bool any=false;for(int i=0;i<5;i++)any|=FilesHandlerGuard.RecordStartupCrash(dir,t+i*1000,o);
 r["guardIO_notEnabledNoFlag"]=!any&&!File.Exists(Path.Combine(dir,FilesHandlerGuard.FlagFile));}
{var o=Fresh();FilesHandler.Register(o,exe);string dir=Path.Combine(tmp,"guard3");long t=7_000_000;bool any=false;
 for(int i=0;i<3;i++)any|=FilesHandlerGuard.RecordStartupCrash(dir,t+i*40000,o);
 r["guardIO_spreadCrashesDoNotTrip"]=!any&&FilesHandler.IsRegistered(o,exe);}
// 11. (review 2) our own value is never backed up: Default already "winlestia" + missing backup -> restore leaves Default absent
{var o=Fresh();Seed(o,@"Directory\shell","winlestia");Seed(o,@"Directory\shell\winlestia\command","old.exe");Seed(o,@"Directory\shell\foo\command","foo.exe");
 FilesHandler.Register(o,exe);File.Delete(o.BackupPath);   // backup captured at the 2nd register would have been wrong; here: lose it, restore without
 FilesHandler.Restore(o);
 r["ownValue_restoreLeavesDefaultAbsent"]=!HasDef(o,@"Directory\shell")&&!Exists(o,@"Directory\shell\winlestia")&&Def(o,@"Directory\shell\foo\command")=="foo.exe";}
{var o=Fresh();Seed(o,@"Directory\shell","winlestia");Seed(o,@"Directory\shell\winlestia\command","old.exe");
 FilesHandler.Register(o,exe);FilesHandler.Restore(o);
 r["ownValue_withBackupAlsoAbsent"]=!HasDef(o,@"Directory\shell")&&!Exists(o,@"Directory\shell\winlestia");}
// 12. (review 3) a failure while writing Drive rolls Directory back
{var o=Fresh();Seed(o,@"Directory\shell","none");
 FilesHandler.FaultHook=t=>{if(t=="Drive")throw new InvalidOperationException("boom");};
 HandlerResult res;try{res=FilesHandler.Register(o,exe);}finally{FilesHandler.FaultHook=null;}
 r["rollback_reportsFailure"]=!res.Ok&&res.Message.Contains("boom")&&res.Message.Contains("rolled back");
 r["rollback_directoryRestored"]=Def(o,@"Directory\shell")=="none"&&!Exists(o,@"Directory\shell\winlestia")&&!Exists(o,"Drive")&&!File.Exists(o.BackupPath);}
// 13. (review 4) a foreign Default set after register is kept, our verb key still goes
{var o=Fresh();Seed(o,@"Drive\shell");FilesHandler.Register(o,exe);
 using(var k=o.Base.CreateSubKey(o.ClassesPath+@"\Drive\shell"))k.SetValue("","foreign");
 FilesHandler.Restore(o);
 r["foreignDefault_kept"]=Def(o,@"Drive\shell")=="foreign"&&!Exists(o,@"Drive\shell\winlestia");}
{var o=Fresh();Seed(o,@"Directory\shell","none");FilesHandler.Register(o,exe);
 using(var k=o.Base.CreateSubKey(o.ClassesPath+@"\Directory\shell"))k.SetValue("","foreign");
 FilesHandler.Restore(o);
 r["foreignDefault_keptEvenWithPriorValue"]=Def(o,@"Directory\shell")=="foreign";}
// 14. (review 6) only real crashes count
r["counts_nullRefCounts"]=FilesHandlerGuard.CountsAsCrash(new NullReferenceException())&&FilesHandlerGuard.CountsAsCrash(new InvalidOperationException())&&FilesHandlerGuard.CountsAsCrash(new OutOfMemoryException());
r["counts_pathProblemsDoNot"]=!FilesHandlerGuard.CountsAsCrash(new UnauthorizedAccessException())&&!FilesHandlerGuard.CountsAsCrash(new FileNotFoundException())&&!FilesHandlerGuard.CountsAsCrash(new DirectoryNotFoundException())&&!FilesHandlerGuard.CountsAsCrash(new PathTooLongException())&&!FilesHandlerGuard.CountsAsCrash(new ArgumentException())&&!FilesHandlerGuard.CountsAsCrash(new NotSupportedException());
// 15. (review 8) backup override only under %TEMP%
{string? oldRoot=Environment.GetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT"),oldB=Environment.GetEnvironmentVariable("WINLESTIA_FILES_HANDLER_BACKUP");
 try{Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT",SandboxRoot+@"\"+run+@"\env2");
  Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_BACKUP",@"C:\Windows\evil.json");
  r["backupEnv_outsideTempIgnored"]=HandlerOptions.FromEnvironment().BackupPath.EndsWith("handler-backup.sandbox.json");
  string mine=Path.Combine(tmp,"mine.json");Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_BACKUP",mine);
  r["backupEnv_underTempHonoured"]=HandlerOptions.FromEnvironment().BackupPath==Path.GetFullPath(mine);}
 finally{Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_ROOT",oldRoot);Environment.SetEnvironmentVariable("WINLESTIA_FILES_HANDLER_BACKUP",oldB);}}
}finally{
 try{using var k=Registry.CurrentUser.OpenSubKey(SandboxRoot,true);k?.DeleteSubKeyTree(run,false);}catch{}
 try{using var parent=Registry.CurrentUser.OpenSubKey(SandboxRoot);bool empty=parent!=null&&parent.SubKeyCount==0&&parent.ValueCount==0;parent?.Dispose();if(empty)Registry.CurrentUser.DeleteSubKey(SandboxRoot,false);}catch{}
 try{using var p2=Registry.CurrentUser.OpenSubKey(@"Software\WinlestiaTest");bool empty=p2!=null&&p2.SubKeyCount==0&&p2.ValueCount==0;p2?.Dispose();if(empty)Registry.CurrentUser.DeleteSubKey(@"Software\WinlestiaTest",false);}catch{}
 try{Directory.Delete(tmp,true);}catch{}
}
using(var chk=Registry.CurrentUser.OpenSubKey(SandboxRoot+@"\"+run))r["sandbox_deleted"]=chk==null;
r["realHkcu_unchanged"]=RealDefault("Directory")==realDirBefore&&RealDefault("Drive")==realDriveBefore;
int ok=0;foreach(var v in r.Values)if(v)ok++;foreach(var k in r)Console.WriteLine(k.Key+"="+(k.Value?"true":"false"));Console.WriteLine("real HKCU Directory\\shell default: "+realDirBefore+" | Drive\\shell default: "+realDriveBefore);Console.WriteLine("TOTAL "+ok+"/"+r.Count);return ok==r.Count?0:1;}
}
