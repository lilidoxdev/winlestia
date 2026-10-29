// C# 12 / WindowsWallpaper.cs / Windows 8+ IDesktopWallpaper, read-only background discovery. Event-driven, no polling.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
namespace PanneauxGlass;
internal sealed record WallpaperMonitor(string name,int x,int y,int width,int height,string path,long modified,string background);
internal sealed class WindowsWallpaper:IDisposable {
    // Every source of change calls Notify(): one debounced Read() then Revision++. The frontend re-resolves once per revision.
    readonly System.Threading.Timer debounce;readonly object gate=new();volatile bool disposed,forced;int revision;
    readonly List<FileSystemWatcher> watchers=new();readonly ManualResetEvent stop=new(false);readonly System.Collections.Concurrent.ConcurrentDictionary<IntPtr,byte> hostChildren=new();
    Thread? registryThread,hookThread;uint hookThreadId;WinEventProc? hookProc;
    internal volatile WallpaperMonitor[] Monitors=Array.Empty<WallpaperMonitor>();
    internal int Revision=>Volatile.Read(ref revision);
    internal WindowsWallpaper(){
        // Revision moves only when the monitors really differ, or when a live host / wallpaper-app config change was seen: a stray UserPreferenceChanged (for example after explorerPalette) must not retrigger a capture.
        debounce=new System.Threading.Timer(_=>{bool changed=Read();if(forced){forced=false;changed=true;}if(changed)Interlocked.Increment(ref revision);},null,Timeout.Infinite,Timeout.Infinite);
        Notify(0);
        try{StartRegistryWatch();}catch(Exception ex){Store.Log("Fond d’écran registre: "+ex.Message);}
        try{StartFileWatch();}catch(Exception ex){Store.Log("Fond d’écran fichiers: "+ex.Message);}
        try{StartHostHook();}catch(Exception ex){Store.Log("Fond d’écran hôte: "+ex.Message);}
        Microsoft.Win32.SystemEvents.UserPreferenceChanged+=Preference;Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=Display;Microsoft.Win32.SystemEvents.SessionSwitch+=Session;Microsoft.Win32.SystemEvents.PowerModeChanged+=Power;
    }
    internal void Notify(int delay=500,bool force=false){if(force)forced=true;if(!disposed)try{debounce.Change(delay,Timeout.Infinite);}catch(ObjectDisposedException){}}
    // WM_SETTINGCHANGE / SPI_SETDESKWALLPAPER arrive through SystemEvents' hidden message window. Unlock, resume and display change are the only safety re-checks.
    void Preference(object? s,Microsoft.Win32.UserPreferenceChangedEventArgs e){if(e.Category is Microsoft.Win32.UserPreferenceCategory.Desktop or Microsoft.Win32.UserPreferenceCategory.General)Notify(250);}
    void Display(object? s,EventArgs e)=>Notify(250);
    void Session(object? s,Microsoft.Win32.SessionSwitchEventArgs e){if(e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.SessionLogon or Microsoft.Win32.SessionSwitchReason.ConsoleConnect)Notify(1000);}
    void Power(object? s,Microsoft.Win32.PowerModeChangedEventArgs e){if(e.Mode==Microsoft.Win32.PowerModes.Resume)Notify(2000);}

    void StartRegistryWatch(){
        registryThread=new Thread(()=>{
            var keys=new List<IntPtr>();var events=new List<ManualResetEvent>();
            foreach(var sub in new[]{@"Control Panel\Desktop",@"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers"}){
                if(WallpaperNative.RegOpenKeyExW(new IntPtr(unchecked((int)0x80000001)),sub,0,0x0010,out var key)==0){keys.Add(key);events.Add(new ManualResetEvent(false));}
            }
            try{
                if(events.Count==0)return;
                var handles=new WaitHandle[events.Count+1];handles[0]=stop;for(int i=0;i<events.Count;i++)handles[i+1]=events[i];
                while(!disposed){
                    for(int i=0;i<keys.Count;i++){events[i].Reset();WallpaperNative.RegNotifyChangeKeyValue(keys[i],true,0x5,events[i].SafeWaitHandle.DangerousGetHandle(),true);}
                    if(WaitHandle.WaitAny(handles)==0)break;Notify(500);
                }
            }finally{foreach(var k in keys)WallpaperNative.RegCloseKey(k);foreach(var e in events)e.Dispose();}
        }){IsBackground=true,Name="Wallpaper registry watch"};
        registryThread.Start();
    }

    void Watch(string folder,string name){
        if(!Directory.Exists(folder))return;
        var w=new FileSystemWatcher(folder,name){NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size};
        FileSystemEventHandler on=(_,_)=>Notify(500,true);w.Changed+=on;w.Created+=on;w.Renamed+=(_,_)=>Notify(500,true);w.EnableRaisingEvents=true;watchers.Add(w);
    }
    void StartFileWatch(){
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),roaming=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),data=Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string packages=Path.Combine(local,"Packages");
        Watch(Path.Combine(roaming,"motion-photo-editor"),"config.json");
        foreach(var d in Safe(()=>Directory.GetDirectories(packages,"Desktophut.*")))Watch(Path.Combine(d,"LocalCache","Roaming","motion-photo-editor"),"config.json");
        foreach(var d in LivelyFolders(local,packages)){Watch(d,"Settings.json");Watch(d,"WallpaperLayout.json");}
        Watch(Path.Combine(data,"LiveWallpaperConfig"),"ServiceConfig.json");
        foreach(var lib in SteamLibraries())Watch(Path.Combine(lib,"steamapps","common","wallpaper_engine"),"config.json");
    }
    static IEnumerable<string> LivelyFolders(string local,string packages){yield return Path.Combine(local,"Lively Wallpaper");foreach(var d in Safe(()=>Directory.GetDirectories(packages,"12030rocksdanister.LivelyWallpaper_*")))yield return Path.Combine(d,"LocalCache","Local","Lively Wallpaper");}
    static string[] Safe(Func<string[]> f){try{return f();}catch{return Array.Empty<string>();}}
    static List<string> SteamLibraries(){
        var libs=new List<string>();
        try{
            using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");string? root=key?.GetValue("SteamPath") as string;if(string.IsNullOrEmpty(root))return libs;
            root=root.Replace('/','\\');libs.Add(root);string vdf=Path.Combine(root,"steamapps","libraryfolders.vdf");
            if(File.Exists(vdf))foreach(Match m in Regex.Matches(File.ReadAllText(vdf),"\"path\"\\s+\"([^\"]+)\""))libs.Add(m.Groups[1].Value.Replace(@"\\",@"\"));
        }catch{}
        return libs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Live host start/stop without polling: window create/destroy/reparent events filtered to children of Progman / WorkerW (24H2: child of Progman; older builds: WorkerW sibling).
    void StartHostHook(){
        using var ready=new ManualResetEventSlim(false);
        hookThread=new Thread(()=>{
            hookThreadId=WallpaperNative.GetCurrentThreadId();hookProc=OnWinEvent;
            IntPtr a=WallpaperNative.SetWinEventHook(0x8000,0x8001,IntPtr.Zero,hookProc,0,0,0x0002),b=WallpaperNative.SetWinEventHook(0x800F,0x800F,IntPtr.Zero,hookProc,0,0,0x0002);
            ready.Set();
            try{while(WallpaperNative.GetMessage(out _,IntPtr.Zero,0,0)>0){}}finally{if(a!=IntPtr.Zero)WallpaperNative.UnhookWinEvent(a);if(b!=IntPtr.Zero)WallpaperNative.UnhookWinEvent(b);}
        }){IsBackground=true,Name="Wallpaper host hook"};
        hookThread.SetApartmentState(ApartmentState.STA);hookThread.Start();ready.Wait(2000);
    }
    internal static string ClassOf(IntPtr h){var sb=new System.Text.StringBuilder(64);return WallpaperNative.GetClassName(h,sb,64)>0?sb.ToString():"";}
    void OnWinEvent(IntPtr hook,uint ev,IntPtr hwnd,int idObject,int idChild,uint thread,uint time){
        if(disposed||idObject!=0||idChild!=0||hwnd==IntPtr.Zero)return;
        if(ev==0x8001){if(hostChildren.TryRemove(hwnd,out _))Notify(500,true);return;}
        IntPtr parent=WallpaperNative.GetParent(hwnd);   // cheap first: nearly every window has a parent that is neither Progman nor WorkerW
        if(parent==IntPtr.Zero){if(ClassOf(hwnd)=="WorkerW")Notify(500,true);return;}
        string pc=ClassOf(parent);if(pc is not ("Progman" or "WorkerW"))return;
        if(ClassOf(hwnd)=="SHELLDLL_DefView")return;
        hostChildren[hwnd]=1;Notify(500,true);
    }

    // Live-wallpaper host window: visible child of Progman / WorkerW that is not the icon view. With a target (monitor rect) pick the host matching it exactly, else the smallest host containing it
    // (a spanning host: the caller crops), else the largest.
    internal static IntPtr FindHost(Native.Rect? target=null){
        var found=new List<(IntPtr h,Native.Rect r)>();
        void scan(IntPtr parent,int depth){WallpaperNative.EnumChildWindows(parent,(h,_)=>{string c=ClassOf(h);if(c=="SHELLDLL_DefView")return true;if(c=="WorkerW"){if(depth<3)scan(h,depth+1);return true;}
            if(WallpaperNative.IsWindowVisible(h)&&WallpaperNative.GetWindowRect(h,out var r))found.Add((h,r));return true;},IntPtr.Zero);}
        IntPtr progman=WallpaperNative.FindWindow("Progman",null);if(progman!=IntPtr.Zero)scan(progman,0);
        if(found.Count==0)WallpaperNative.EnumWindows((h,_)=>{if(ClassOf(h)=="WorkerW")scan(h,0);return true;},IntPtr.Zero);
        if(found.Count==0)return IntPtr.Zero;
        long Area((IntPtr h,Native.Rect r) f)=>(long)(f.r.Right-f.r.Left)*(f.r.Bottom-f.r.Top);
        if(target is{} t){
            var exact=found.Where(f=>Math.Abs(f.r.Left-t.Left)<=2&&Math.Abs(f.r.Top-t.Top)<=2&&Math.Abs(f.r.Right-t.Right)<=2&&Math.Abs(f.r.Bottom-t.Bottom)<=2).ToList();if(exact.Count>0)return exact[0].h;
            var contain=found.Where(f=>f.r.Left<=t.Left+2&&f.r.Top<=t.Top+2&&f.r.Right>=t.Right-2&&f.r.Bottom>=t.Bottom-2).OrderBy(Area).ToList();if(contain.Count>0)return contain[0].h;
        }
        return found.OrderByDescending(Area).First().h;
    }

    bool Read(){if(disposed)return false;lock(gate){object? instance=null;bool changed=false;
        try{instance=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"))!);var api=(IDesktopWallpaper)instance!;api.GetMonitorDevicePathCount(out uint count);api.GetBackgroundColor(out uint color);string background=$"#{color&255:X2}{(color>>8)&255:X2}{(color>>16)&255:X2}";var result=new List<WallpaperMonitor>();
            for(uint i=0;i<count;i++){if(api.GetMonitorDevicePathAt(i,out string id)!=0||api.GetMonitorRECT(id,out var rect)!=0||rect.Right<=rect.Left||rect.Bottom<=rect.Top)continue;api.GetWallpaper(id,out string path);long modified=0;try{if(File.Exists(path))modified=File.GetLastWriteTimeUtc(path).Ticks;}catch{}string name=System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>s.Bounds.Left==rect.Left&&s.Bounds.Top==rect.Top)?.DeviceName??id;result.Add(new(name,rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top,path??"",modified,background));}
            var next=result.ToArray();changed=!next.SequenceEqual(Monitors);Monitors=next;
        }catch(Exception ex){Store.Log("Fond d’écran: "+ex.Message);}finally{if(instance!=null&&Marshal.IsComObject(instance))Marshal.ReleaseComObject(instance);}
        return changed;
    }}
    public void Dispose(){
        if(disposed)return;disposed=true;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged-=Preference;Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=Display;Microsoft.Win32.SystemEvents.SessionSwitch-=Session;Microsoft.Win32.SystemEvents.PowerModeChanged-=Power;
        debounce.Dispose();stop.Set();foreach(var w in watchers)w.Dispose();
        if(hookThreadId!=0)WallpaperNative.PostThreadMessage(hookThreadId,0x0012,IntPtr.Zero,IntPtr.Zero);
    }
}
internal delegate void WinEventProc(IntPtr hook,uint ev,IntPtr hwnd,int idObject,int idChild,uint thread,uint time);
internal delegate bool EnumProc(IntPtr hwnd,IntPtr lParam);
internal static class WallpaperNative {
    [StructLayout(LayoutKind.Sequential)]internal struct Msg{public IntPtr hwnd;public uint message;public IntPtr wParam,lParam;public uint time;public int x,y;}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode)]internal static extern int RegOpenKeyExW(IntPtr key,string sub,int options,int sam,out IntPtr result);
    [DllImport("advapi32.dll")]internal static extern int RegNotifyChangeKeyValue(IntPtr key,bool subtree,int filter,IntPtr evt,bool async);
    [DllImport("advapi32.dll")]internal static extern int RegCloseKey(IntPtr key);
    [DllImport("user32.dll")]internal static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEventProc proc,uint pid,uint tid,uint flags);
    [DllImport("user32.dll")]internal static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")]internal static extern int GetMessage(out Msg msg,IntPtr hwnd,uint min,uint max);
    [DllImport("user32.dll")]internal static extern bool PostThreadMessage(uint thread,uint msg,IntPtr w,IntPtr l);
    [DllImport("kernel32.dll")]internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]internal static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern int GetClassName(IntPtr hwnd,System.Text.StringBuilder sb,int max);
    [DllImport("user32.dll")]internal static extern bool EnumChildWindows(IntPtr parent,EnumProc proc,IntPtr l);
    [DllImport("user32.dll")]internal static extern bool EnumWindows(EnumProc proc,IntPtr l);
    [DllImport("user32.dll")]internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]internal static extern bool GetWindowRect(IntPtr hwnd,out Native.Rect rect);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern IntPtr FindWindow(string cls,string? title);
}
[ComImport,Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper {
    [PreserveSig]int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)]string monitor,[MarshalAs(UnmanagedType.LPWStr)]string wallpaper);
    [PreserveSig]int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)]string monitor,[MarshalAs(UnmanagedType.LPWStr)]out string wallpaper);
    [PreserveSig]int GetMonitorDevicePathAt(uint index,[MarshalAs(UnmanagedType.LPWStr)]out string monitor);
    [PreserveSig]int GetMonitorDevicePathCount(out uint count);
    [PreserveSig]int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)]string monitor,out Native.Rect rect);
    [PreserveSig]int SetBackgroundColor(uint color);
    [PreserveSig]int GetBackgroundColor(out uint color);
}
