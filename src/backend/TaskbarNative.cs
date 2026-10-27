// language: C# 12, file: TaskbarNative.cs, target: Windows 11 / .NET 8, reversible shell appbar
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Forms=System.Windows.Forms;

namespace PanneauxGlass;
internal static class TaskbarNative {
    [StructLayout(LayoutKind.Sequential)] internal struct BarData { public uint Size;public IntPtr Window;public uint Callback,Edge;public Native.Rect Rect;public IntPtr Param; }
    internal sealed class Lease { public int State {get;set;}public List<long> Visible {get;set;}=new();public string Ready {get;set;}="";public string Release {get;set;}="";public long ParentStart {get;set;} }
    internal sealed record AppWindow(IntPtr Handle,string Title,string Path);
    internal delegate bool EnumProc(IntPtr window,IntPtr data);
    internal delegate void WinEvent(IntPtr hook,uint kind,IntPtr window,int obj,int child,uint thread,uint time);
    [DllImport("shell32.dll")] internal static extern UIntPtr SHAppBarMessage(uint message,ref BarData data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindow(string name,string? title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc proc,IntPtr data);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window,out Native.Rect rect);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr window,uint attribute,out int value,int size);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEvent callback,uint pid,uint thread,uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Native.Point point,uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor,int kind,out uint x,out uint y);
    [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    internal static BarData Data(IntPtr window=default)=>new(){Size=(uint)Marshal.SizeOf<BarData>(),Window=window};
    internal static string LeasePath=>Path.Combine(Store.Root,"taskbar-session.json");
    static readonly Dictionary<string,string> linkTargets=new(StringComparer.OrdinalIgnoreCase);
    internal static string LaunchPath(string path){if(!path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase))return path;if(linkTargets.TryGetValue(path,out var cached))return cached;string target=path;object? shell=null,link=null;try{var type=Type.GetTypeFromProgID("WScript.Shell");if(type!=null){shell=Activator.CreateInstance(type);if(shell!=null){link=type.InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,new object[]{path});if(link!=null)target=Convert.ToString(link.GetType().InvokeMember("TargetPath",System.Reflection.BindingFlags.GetProperty,null,link,null))??path;}}}catch{}finally{if(link!=null&&Marshal.IsComObject(link))Marshal.ReleaseComObject(link);if(shell!=null&&Marshal.IsComObject(shell))Marshal.ReleaseComObject(shell);}linkTargets[path]=target;return target;}
    internal static string ClassName(IntPtr window){var text=new StringBuilder(128);GetClassName(window,text,128);return text.ToString();}
    internal static List<IntPtr> ExplorerBars(){var result=new List<IntPtr>();EnumWindows((h,_)=>{if(ClassName(h) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")result.Add(h);return true;},IntPtr.Zero);return result;}
    internal static double Scale(Forms.Screen screen){try{var p=new Native.Point{X=screen.Bounds.Left+10,Y=screen.Bounds.Top+10};if(GetDpiForMonitor(MonitorFromPoint(p,2),0,out var x,out _)==0)return x/96d;}catch{}return 1;}
    internal static Lease Snapshot(){var data=Data(FindWindow("Shell_TrayWnd",null));return new(){State=(int)SHAppBarMessage(4,ref data).ToUInt64(),Visible=ExplorerBars().Where(IsWindowVisible).Select(h=>h.ToInt64()).ToList(),ParentStart=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks};}
    internal static void HideExplorer(){var data=Data(FindWindow("Shell_TrayWnd",null));var state=(int)SHAppBarMessage(4,ref data).ToUInt64();data.Param=new IntPtr(state|1);SHAppBarMessage(10,ref data);foreach(var bar in ExplorerBars())ShowWindow(bar,0);}
    internal static void EnsureHidden(){foreach(var bar in ExplorerBars())if(IsWindowVisible(bar))ShowWindow(bar,0);}
    internal static void Restore(Lease? lease){if(lease==null)return;var data=Data(FindWindow("Shell_TrayWnd",null));data.Param=new IntPtr(lease.State);SHAppBarMessage(10,ref data);foreach(var bar in ExplorerBars())ShowWindow(bar,5);}
    internal static Lease? ReadLease(string path){try{return JsonSerializer.Deserialize<Lease>(File.ReadAllText(path),Store.Json);}catch{return null;}}
    internal static void RestoreSaved(){Restore(ReadLease(LeasePath));}
    sealed class ProcessWait:WaitHandle { internal ProcessWait(Process process){SafeWaitHandle=new SafeWaitHandle(process.Handle,false);} }
    internal static int Guard(int parentId,string path){
        var lease=ReadLease(path);if(lease==null)return 2;
        try{using var parent=Process.GetProcessById(parentId);if(parent.StartTime.ToUniversalTime().Ticks!=lease.ParentStart){Restore(lease);return 0;}using var ready=EventWaitHandle.OpenExisting(lease.Ready);using var release=EventWaitHandle.OpenExisting(lease.Release);using var ended=new ProcessWait(parent);ready.Set();WaitHandle.WaitAny(new WaitHandle[]{ended,release});Restore(lease);return 0;}catch{Restore(lease);return 1;}
    }
    internal static List<AppWindow> Applications(){var result=new List<AppWindow>();EnumWindows((h,_)=>{
        if(!IsWindowVisible(h)||Native.Owns(h)||ClassName(h) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")return true;
        long style=Native.ExtendedStyle(h);if((style&0x80)!=0||GetWindow(h,4)!=IntPtr.Zero&&(style&0x40000)==0)return true;
        if(DwmGetWindowAttribute(h,14,out int cloaked,4)==0&&cloaked!=0)return true;
        string title=Native.Title(h);if(string.IsNullOrWhiteSpace(title))return true;
        string path="";try{GetWindowThreadProcessId(h,out uint pid);using var process=Process.GetProcessById((int)pid);path=process.MainModule?.FileName??"";}catch{}
        result.Add(new(h,title,path));return true;
    },IntPtr.Zero);return result;}
    internal static void Activate(AppWindow app){if(!Native.IsWindow(app.Handle))return;if(Native.GetForegroundWindow()==app.Handle){ShowWindow(app.Handle,6);return;}if(IsIconic(app.Handle))ShowWindow(app.Handle,9);SetForegroundWindow(app.Handle);}
    internal static void WindowsKey(byte key){const byte win=0x5B;keybd_event(win,0,0,UIntPtr.Zero);if(key!=0)keybd_event(key,0,0,UIntPtr.Zero);if(key!=0)keybd_event(key,0,2,UIntPtr.Zero);keybd_event(win,0,2,UIntPtr.Zero);}
}
