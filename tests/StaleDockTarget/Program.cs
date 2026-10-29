// Self-heal of stale dock: exe paths: Squirrel app-* only. Fixtures live under %TEMP%\WinlestiaStaleDock and are deleted at the end.
using System;using System.IO;using System.Linq;using PanneauxGlass;
static class Program{
 static int fails;
 static void Check(bool ok,string name){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)fails++;}
 static void Exe(string dir,string name="Discord.exe"){Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,name),"x");}
 static int Main(){
  string d=Path.Combine(Path.GetTempPath(),"WinlestiaStaleDock");if(Directory.Exists(d))Directory.Delete(d,true);
  try{
   string app=Path.Combine(d,"Discord");foreach(string v in new[]{"app-1.0.9003","app-1.0.9266","app-1.0.10"})Exe(Path.Combine(app,v));
   Directory.CreateDirectory(Path.Combine(app,"app-1.0.9267"));// newest folder without the exe must be skipped
   Check(Store.HealStaleExe(Path.Combine(app,"app-1.0.9265","Discord.exe"))==Path.Combine(app,"app-1.0.9266","Discord.exe"),"Squirrel: newest sibling app-* with the exe (numeric order)");
   string sfx=Path.Combine(d,"Sfx");Exe(Path.Combine(sfx,"app-1.0.9266-beta"));Exe(Path.Combine(sfx,"app-1.0.9265"));
   Check(Store.HealStaleExe(Path.Combine(sfx,"app-1.0.1","Discord.exe"))==Path.Combine(sfx,"app-1.0.9266-beta","Discord.exe"),"Suffix stripped: 9266-beta > 9265");
   Exe(Path.Combine(sfx,"app-1.0.9266"));
   Check(Store.HealStaleExe(Path.Combine(sfx,"app-1.0.1","Discord.exe"))==Path.Combine(sfx,"app-1.0.9266","Discord.exe"),"Tie: build without suffix preferred");
   Exe(Path.Combine(d,"Tools","Thing"),"Thing.exe");
   Check(Store.HealStaleExe(Path.Combine(d,"Gone","Thing.exe"))==null,"Generic folder, no Squirrel layout: no heal (no catalogue fallback)");
   Check(Store.HealStaleExe(Path.Combine(app,"bin","Discord.exe"))==null,"Parent not app-*: no heal");
   Check(Store.HealStaleExe(@"\server\share\Discord\app-1.0.1\Discord.exe")==null,"UNC path: no heal, no probe");
   char free=Enumerable.Range('D','Z'-'D'+1).Select(c=>(char)c).First(c=>!Directory.Exists(c+@":\"));
   Check(Store.HealStaleExe(free+@":\Discord\app-1.0.1\Discord.exe")==null,"Missing drive "+free+": no heal");
   Check(Store.HealStaleExe(Path.Combine("Discord","app-1.0.1","Discord.exe"))==null,"Relative path: no heal");
  }finally{try{Directory.Delete(d,true);}catch{}}
  Console.WriteLine(fails==0?"ALL PASS":"FAILED");return fails==0?0:1;}
}
