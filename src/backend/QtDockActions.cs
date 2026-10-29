// C# 12 / QtDockActions.cs / Windows 11. Dock state and validated desktop actions.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace PanneauxGlass;
internal sealed partial class QtShellBridge {
    object[] runningApps=Array.Empty<object>();
    // Window-preview bubble (DockPreviewPopup.cs): hwnd/title per dock item id, refreshed with every RefreshDockApps.
    readonly DockPreviewHost dockPreview=new();readonly Dictionary<string,PreviewWindow[]> dockWindows=new();
    void DockPreviewOpen(JsonElement args){
        string id=Text(args,"id");double dpr=Number(args,"dpr")>0?Number(args,"dpr"):1,x=Number(args,"x"),y=Number(args,"y"),cx=Number(args,"cx"),cy=Number(args,"cy");bool light=LightRendering;
        dispatcher.BeginInvoke((Action)(()=>{
            if(disposed||!owner.TaskbarEnabled||!dockWindows.TryGetValue(id,out var list)||list.Length==0){dockPreview.Close();return;}
            Native.GetCursorPos(out var cursor);   // physical pixels; the icon centre / dock top are offsets from the cursor in logical px * devicePixelRatio (per-monitor safe)
            dockPreview.Open(id,list,(int)Math.Round(cursor.X+(x-cx)*dpr),(int)Math.Round(cursor.Y+(y-cy)*dpr),light);
        }));
    }
    void DockPreviewEnd(JsonElement args){bool now=Flag(args,"now");dispatcher.BeginInvoke((Action)(()=>{if(now)dockPreview.Close();else dockPreview.Retire(150);}));}
    void SyncDockPreview(){if(!dockPreview.IsOpen)return;if(dockWindows.TryGetValue(dockPreview.OwnerId,out var list)&&list.Length>0)dockPreview.Update(list);else dockPreview.Close();}
    [DllImport("user32.dll",SetLastError=true)]static extern bool PostMessageW(IntPtr window,uint message,IntPtr wp,IntPtr lp);
    static bool SamePath(string a,string b)=>!string.IsNullOrWhiteSpace(a)&&string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    static bool CanStop(string path)=>File.Exists(path)&&!path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&!SamePath(path,Environment.ProcessPath??"");
    void RefreshDockApps(){
        var windows=TaskbarNative.Applications().Where(w=>!w.Title.Contains("Caelestia")&&!w.Title.StartsWith("Panneaux ·",StringComparison.Ordinal)).ToArray();
        var result=new List<object>();var running=new List<object>();var pinnedEntries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);dockEntries.Clear();dockWindows.Clear();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups=windows.Where(w=>!string.IsNullOrEmpty(w.Path)).GroupBy(w=>w.Path,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.OrdinalIgnoreCase);
        var entries=Store.Entries.Where(e=>!string.IsNullOrEmpty(e.Target)).ToArray();
        object Value(Entry entry,TaskbarNative.AppWindow[] matches,bool pinned,bool background=false){
            string path=TaskbarNative.LaunchPath(entry.Target);dockEntries[entry.Id]=entry;LoadIcon(entry);
            dockWindows[entry.Id]=matches.Select(w=>new PreviewWindow(w.Handle.ToInt64(),w.Title)).ToArray();
            return new{id=entry.Id,name=entry.Name,path,hwnd=matches.FirstOrDefault()?.Handle.ToInt64()??0,pinned,favorite=Store.State.Favorites.Contains(entry.Id),background,canStop=CanStop(path),count=matches.Length,active=matches.Any(w=>w.Handle==Native.GetForegroundWindow()),windows=matches.Select(w=>new{hwnd=w.Handle.ToInt64(),title=w.Title}).ToArray()};
        }
        bool pinsChanged=false;
        for(int i=0;i<Store.State.TaskbarPins.Count;i++){
            string old=Store.State.TaskbarPins[i];var source=entries.FirstOrDefault(e=>e.Id==old)??Store.State.Custom.FirstOrDefault(e=>e.Id==old);
            if(source==null&&File.Exists(old))source=new(){Id=old,Name=Path.GetFileNameWithoutExtension(old),Target=old,IconPath=old};
            if(source==null)continue;var stable=PersistDockEntry(source);
            if(old!=stable.Id){Store.State.TaskbarPins[i]=stable.Id;pinsChanged=true;}
        }
        if(pinsChanged)Store.Save();
        entries=Store.Entries.Where(e=>!string.IsNullOrEmpty(e.Target)).ToArray();
        foreach(string id in Store.State.TaskbarPins.Take(24)){
            var e=entries.FirstOrDefault(e=>e.Id==id);if(e==null&&File.Exists(id))e=new(){Id=id,Name=Path.GetFileNameWithoutExtension(id),Target=id,IconPath=id};if(e==null)continue;
            string path=TaskbarNative.LaunchPath(e.Target);if(!seen.Add(path))continue;
            var matches=groups.GetValueOrDefault(path)??windows.Where(w=>Store.Fold(Path.GetFileNameWithoutExtension(w.Path)).Replace(" ","")==Store.Fold(e.Name).Replace(" ","")).ToArray();
            pinnedEntries[path]=e;foreach(var match in matches){seen.Add(match.Path);pinnedEntries[match.Path]=e;}result.Add(Value(e,matches,true));
        }
        foreach(var group in groups){
            var e=pinnedEntries.GetValueOrDefault(group.Key)??entries.FirstOrDefault(e=>SamePath(e.Target,group.Key))??new Entry{Id="task:"+group.Key,Name=Path.GetFileNameWithoutExtension(group.Key),Target=group.Key,IconPath=group.Key};
            bool pinned=Store.State.TaskbarPins.Contains(e.Id)||Store.State.TaskbarPins.Contains(group.Key);
            var value=Value(e,group.Value,pinned);running.Add(value);if(seen.Add(group.Key))result.Add(value);
        }
        foreach(var e in backgroundEntries){if(groups.ContainsKey(e.Target)||!seen.Add(e.Target))continue;running.Add(Value(e,Array.Empty<TaskbarNative.AppWindow>(),Store.State.TaskbarPins.Contains(e.Id),true));}
        tasks=result.Take(48).ToArray();runningApps=running.ToArray();SyncDockPreview();
    }
    Entry[] backgroundEntries=Array.Empty<Entry>();bool scanningBackground;DateTime backgroundAt;
    void RefreshBackgroundApps(){if(scanningBackground||DateTime.UtcNow<backgroundAt)return;scanningBackground=true;backgroundAt=DateTime.UtcNow.AddSeconds(5);var known=Store.Entries.ToArray();
        _=System.Threading.Tasks.Task.Run(()=>{var result=new List<Entry>();var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int session=Process.GetCurrentProcess().SessionId;
            foreach(var process in Process.GetProcesses())using(process)try{if(process.Id==Environment.ProcessId||process.SessionId!=session)continue;string path=process.MainModule?.FileName??"";if(!CanStop(path)||!paths.Add(path))continue;result.Add(known.FirstOrDefault(e=>SamePath(e.Target,path))??new Entry{Id="background:"+path,Name=FileVersionInfo.GetVersionInfo(path).FileDescription??process.ProcessName,Target=path,IconPath=path});}catch{}
            dispatcher.BeginInvoke((Action)(()=>{scanningBackground=false;if(!disposed){backgroundEntries=result.ToArray();nextTasks=DateTime.MinValue;}}));
        });
    }
    Entry PersistDockEntry(Entry entry){
        var saved=Store.State.Custom.FirstOrDefault(e=>e.Id.StartsWith("dock:")&&SamePath(e.Target,entry.Target));if(saved!=null)return saved;
        saved=new(){Id="dock:"+entry.Target,Name=entry.Name,Target=entry.Target,IconPath=entry.IconPath,Section="Applications",Category="Applications"};Store.State.Custom.Add(saved);Store.Entries.Add(saved);return saved;
    }
    string? DockAction(JsonElement args){
        string id=Text(args,"id"),action=Text(args,"action");long handle=(long)Number(args,"hwnd");
        if(!dockEntries.TryGetValue(id,out var entry)){entry=Store.Entries.FirstOrDefault(e=>e.Id==id);if(entry==null)return "Cette application n’est plus disponible.";}
        string path=TaskbarNative.LaunchPath(entry.Target);var matches=TaskbarNative.Applications().Where(w=>SamePath(w.Path,path)).ToArray();var selected=matches.FirstOrDefault(w=>w.Handle.ToInt64()==handle)??matches.FirstOrDefault();
        switch(action){
            case "reorder":int from=Store.State.TaskbarPins.IndexOf(id),to=(int)Number(args,"index");if(from>=0){Store.State.TaskbarPins.RemoveAt(from);Store.State.TaskbarPins.Insert(Math.Clamp(to,0,Store.State.TaskbarPins.Count),id);Store.Save();}break;
            case "open":owner.Launch(entry);break;
            case "admin":if(File.Exists(path))Process.Start(new ProcessStartInfo(path){UseShellExecute=true,Verb="runas"});break;
            case "activate":if(selected!=null){if(TaskbarNative.IsIconic(selected.Handle))TaskbarNative.ShowWindow(selected.Handle,9);TaskbarNative.SetForegroundWindow(selected.Handle);}break;
            case "minimize":if(selected!=null)TaskbarNative.ShowWindow(selected.Handle,6);break;
            case "maximize":if(selected!=null)TaskbarNative.ShowWindow(selected.Handle,3);break;
            case "close":if(selected!=null&&!PostMessageW(selected.Handle,0x10,IntPtr.Zero,IntPtr.Zero))return "Windows a refusé la fermeture de cette fenêtre.";break;
            case "closeAll":foreach(var window in matches)if(!PostMessageW(window.Handle,0x10,IntPtr.Zero,IntPtr.Zero))return "Certaines fenêtres n’ont pas pu être fermées.";break;
            case "pin":var pin=PersistDockEntry(entry);bool wasPinned=Store.State.TaskbarPins.Contains(pin.Id)||Store.State.TaskbarPins.Contains(entry.Id)||Store.State.TaskbarPins.Contains(entry.Target);Store.State.TaskbarPins.Remove(pin.Id);Store.State.TaskbarPins.Remove(entry.Id);Store.State.TaskbarPins.Remove(entry.Target);if(!wasPinned)Store.State.TaskbarPins.Add(pin.Id);Store.Save();break;
            case "favorite":var favorite=PersistDockEntry(entry);if(!Store.State.Favorites.Remove(favorite.Id)){Store.State.Favorites.Add(favorite.Id);if(!Store.State.TaskbarPins.Contains(favorite.Id))Store.State.TaskbarPins.Add(favorite.Id);}Store.Save();break;
            case "reveal":owner.Reveal(entry);break;
            case "stop":
                if(!Flag(args,"confirmed")||!CanStop(path))return "Arrêt refusé pour ce processus.";
                int session=Process.GetCurrentProcess().SessionId;
                foreach(var p in Process.GetProcesses())using(p){try{if(p.Id!=Environment.ProcessId&&p.SessionId==session&&SamePath(p.MainModule?.FileName??"",path))p.Kill();}catch{}}
                backgroundEntries=backgroundEntries.Where(e=>!SamePath(e.Target,path)).ToArray();backgroundAt=DateTime.MinValue;RefreshBackgroundApps();break;
        }
        return null;
    }
    void DockSystem(string id){
        string target,arguments="";bool admin=false;
        switch(id){
            case "shutdown":target="shutdown.exe";arguments="/s /t 0";break;
            case "restart":target="shutdown.exe";arguments="/r /t 0";break;
            case "sleep":System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend,false,false);return;
            case "terminal":target="wt.exe";break;case "terminalAdmin":target="wt.exe";admin=true;break;
            case "taskmgr":target="taskmgr.exe";break;case "settings":target="ms-settings:";break;
            case "apps":target="ms-settings:appsfeatures";break;case "power":target="ms-settings:powersleep";break;
            case "system":target="ms-settings:about";break;case "network":target="ms-settings:network-status";break;
            case "devices":target="devmgmt.msc";break;case "events":target="eventvwr.msc";break;
            case "disks":target="diskmgmt.msc";break;case "computer":target="compmgmt.msc";break;
            case "explorer":target="explorer.exe";break;case "control":target="control.exe";break;
            case "desktop":TaskbarNative.WindowsKey(0x44);return;
            default:return;
        }
        Process.Start(new ProcessStartInfo(target,arguments){UseShellExecute=true,Verb=admin?"runas":"open"});
    }
}
