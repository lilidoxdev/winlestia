// C# 12 / WindowsWallpaper.cs / Windows 8+ IDesktopWallpaper, read-only background discovery.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
namespace PanneauxGlass;
internal sealed record WallpaperMonitor(string name,int x,int y,int width,int height,string path,long modified,string background);
internal sealed class WindowsWallpaper:IDisposable {
    readonly Timer timer;int busy;volatile bool disposed;
    internal volatile WallpaperMonitor[] Monitors=Array.Empty<WallpaperMonitor>();
    internal WindowsWallpaper(){timer=new Timer(_=>Read(),null,0,5000);}
    void Read(){if(disposed||Interlocked.Exchange(ref busy,1)!=0)return;object? instance=null;
        try{instance=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"))!);var api=(IDesktopWallpaper)instance!;api.GetMonitorDevicePathCount(out uint count);api.GetBackgroundColor(out uint color);string background=$"#{color&255:X2}{(color>>8)&255:X2}{(color>>16)&255:X2}";var result=new List<WallpaperMonitor>();
            for(uint i=0;i<count;i++){if(api.GetMonitorDevicePathAt(i,out string id)!=0||api.GetMonitorRECT(id,out var rect)!=0||rect.Right<=rect.Left||rect.Bottom<=rect.Top)continue;api.GetWallpaper(id,out string path);long modified=0;try{if(File.Exists(path))modified=File.GetLastWriteTimeUtc(path).Ticks;}catch{}string name=System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>s.Bounds.Left==rect.Left&&s.Bounds.Top==rect.Top)?.DeviceName??id;result.Add(new(name,rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top,path??"",modified,background));}
            Monitors=result.ToArray();
        }catch(Exception ex){Store.Log("Fond d’écran: "+ex.Message);}finally{if(instance!=null&&Marshal.IsComObject(instance))Marshal.ReleaseComObject(instance);Volatile.Write(ref busy,0);}
    }
    public void Dispose(){disposed=true;timer.Dispose();}
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
