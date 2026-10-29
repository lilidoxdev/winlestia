// C#12 / FilesPinMigration: pin removal on an in-memory Preferences (never the user's state file).
using System;using System.Collections.Generic;using System.Linq;using PanneauxGlass;
// Mirrors the three Preferences members the migration touches (Models.cs itself drags half the backend); Entry is the same shape as Models.cs.
namespace PanneauxGlass{public sealed class Entry{public string Id{get;set;}="";public string Name{get;set;}="";public string Target{get;set;}="";}}
class State{public bool FilesPinRemoved;public List<string> TaskbarPins=new();public List<Entry> Custom=new();public bool Run(string exe,List<string> ids)=>FilesPinMigration.Apply(ref FilesPinRemoved,TaskbarPins,Custom,exe,ids);}
class Test{
static int Main(){var r=new Dictionary<string,bool>();const string exe=@"C:\App\Winlestia.Files.exe";string id="dock:"+exe;
State S(params string[] pins){var s=new State();s.TaskbarPins.AddRange(pins);return s;}
Entry E(string i,string name,string target)=>new Entry{Id=i,Name=name,Target=target};
{var s=S("a",id,"b");s.Custom.Add(E(id,"Winlestia Fichiers",exe));var ids=new List<string>();bool c=s.Run(exe,ids);
 r["removesDockPinOnly"]=c&&s.TaskbarPins.SequenceEqual(new[]{"a","b"});r["removesOwnCustomEntry"]=s.Custom.Count==0&&ids.SequenceEqual(new[]{id});r["setsFlag"]=s.FilesPinRemoved;
 var again=s.Run(exe,new List<string>());r["idempotent"]=!again&&s.TaskbarPins.Count==2;}
{var s=S(exe.ToUpperInvariant(),"x");s.Run(exe,new List<string>());r["removesBareExeCaseInsensitive"]=s.TaskbarPins.SequenceEqual(new[]{"x"});}
{var s=S("a","b");s.Custom.Add(E("c1","Autre",@"C:\x.exe"));var ids=new List<string>();s.Run(exe,ids);r["otherPinsAndEntriesUntouched"]=s.TaskbarPins.SequenceEqual(new[]{"a","b"})&&s.Custom.Count==1&&ids.Count==0&&s.FilesPinRemoved;}
{var s=S(id);s.Custom.Add(E(id,"Renommé par l'utilisateur",exe));var ids=new List<string>();s.Run(exe,ids);r["renamedEntryKept"]=s.Custom.Count==1&&ids.Count==0&&s.TaskbarPins.Count==0;}
{var s=S(id);s.FilesPinRemoved=true;bool c=s.Run(exe,new List<string>());r["flagAlreadySetIsNoop"]=!c&&s.TaskbarPins.Count==1;}
int ok=r.Values.Count(v=>v);foreach(var k in r)Console.WriteLine(k.Key+"="+(k.Value?"true":"false"));Console.WriteLine("TOTAL "+ok+"/"+r.Count);return ok==r.Count?0:1;}
}
