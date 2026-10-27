// C#12 / DesktopRecents.cs / Windows11. Bounded chronological history, one background STA, no Shell COM on UI.
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Collections.Concurrent;using System.Threading;using System.Text.Json;
namespace PanneauxGlass;
internal static class DesktopRecents{
 internal sealed record Recent(Entry Entry,DateTime At);
 internal static readonly Dictionary<string,Entry> Entries=new();static readonly Dictionary<string,Recent> history=new(StringComparer.OrdinalIgnoreCase);
 static readonly ConcurrentQueue<(uint pid,DateTime at)> queue=new();static readonly AutoResetEvent wake=new(false);static readonly string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Panneaux","recent-launches.json");
 static readonly Thread worker;static uint lastPid;static object[] values=Array.Empty<object>();static DateTime nextScan;
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 static DesktopRecents(){worker=new Thread(Loop){IsBackground=true,Name="Winlestia recent items"};worker.SetApartmentState(ApartmentState.STA);worker.Start();}
 internal static object[] Values{get{lock(Entries)return values;}}
 internal static void Observe(IntPtr hwnd){GetWindowThreadProcessId(hwnd,out uint pid);if(pid==0||pid==Environment.ProcessId||pid==lastPid)return;lastPid=pid;if(queue.Count<32){queue.Enqueue((pid,DateTime.UtcNow));wake.Set();}}
 internal static void RefreshSoon(){nextScan=DateTime.MinValue;wake.Set();}
 internal static Recent[] Chronological(IEnumerable<Recent> source)=>RecentOrdering.Sort(source);
 static void Put(Entry e,DateTime at){if(history.TryGetValue(e.Target,out var old)&&old.At>=at)return;history[e.Target]=new(e,at);}
 static void Loop(){try{foreach(var item in JsonSerializer.Deserialize<Recent[]>(File.ReadAllText(file))??Array.Empty<Recent>())Put(item.Entry,item.At);}catch{}
  object? shell=null;var shortcuts=new Dictionary<string,(DateTime time,string target)>();try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);}catch{}
  while(true){bool changed=false;while(queue.TryDequeue(out var item))try{using var process=System.Diagnostics.Process.GetProcessById((int)item.pid);string path=process.MainModule?.FileName??"";if(path.StartsWith(Path.GetFullPath(Path.Combine(Store.Root,".."))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(path)||path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;Put(new(){Id="recent:"+path,Name=System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileDescription??process.ProcessName,Target=path},item.at);changed=true;}catch{}
   if(DateTime.UtcNow>=nextScan){nextScan=DateTime.UtcNow.AddSeconds(15);try{foreach(string linkPath in Directory.EnumerateFiles(Environment.GetFolderPath(Environment.SpecialFolder.Recent),"*.lnk").OrderByDescending(File.GetLastWriteTimeUtc).Take(64)){DateTime at=File.GetLastWriteTimeUtc(linkPath);string target="";if(shortcuts.TryGetValue(linkPath,out var cached)&&cached.time==at)target=cached.target;else if(shell!=null){object? link=null;try{link=((dynamic)shell).CreateShortcut(linkPath);target=((dynamic)link).TargetPath;shortcuts[linkPath]=(at,target);}catch{}finally{if(link!=null)Marshal.FinalReleaseComObject(link);}}if(!string.IsNullOrEmpty(target)&&(File.Exists(target)||Directory.Exists(target))){Put(new(){Id="recent:"+target,Name=Path.GetFileNameWithoutExtension(linkPath),Target=target},at);changed=true;}}if(shortcuts.Count>128)shortcuts.Clear();}catch{}}
   if(changed){var ordered=Chronological(history.Values);history.Clear();foreach(var item in ordered)history[item.Entry.Target]=item;lock(Entries){Entries.Clear();foreach(var item in ordered)Entries[item.Entry.Id]=item.Entry;values=ordered.Take(24).Select(r=>new{id=r.Entry.Id,name=r.Entry.Name,kind="systemRecent",icon=Directory.Exists(r.Entry.Target)?"folder":"apps",comment=r.Entry.Target,at=r.At}).ToArray();}try{Directory.CreateDirectory(Path.GetDirectoryName(file)!);File.WriteAllText(file,JsonSerializer.Serialize(ordered));}catch{}}
   wake.WaitOne(2000);
  }
 }
}

internal static class RecentOrdering{internal static DesktopRecents.Recent[] Sort(IEnumerable<DesktopRecents.Recent> source)=>source.GroupBy(r=>r.Entry.Target,StringComparer.OrdinalIgnoreCase).Select(g=>g.MaxBy(r=>r.At)!).OrderByDescending(r=>r.At).Take(64).ToArray();}
