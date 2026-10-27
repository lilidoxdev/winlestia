// language: C# 12, file: MonitorBrightness.cs, target: Windows 11 / .NET 8, DDC/CI on the displayed monitor
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PanneauxGlass;
internal sealed record BrightnessSnapshot(bool Available,double Value,string Device,string Error="");
internal static class MonitorBrightness {
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct PhysicalMonitor {public IntPtr Handle;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;}
    [DllImport("user32.dll")]static extern IntPtr MonitorFromPoint(Native.Point point,uint flags);
    [DllImport("dxva2.dll",SetLastError=true)]static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor,out uint count);
    [DllImport("dxva2.dll",SetLastError=true)]static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor,uint count,[Out]PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll")]static extern bool DestroyPhysicalMonitors(uint count,PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll",SetLastError=true)]static extern bool GetMonitorBrightness(IntPtr monitor,out uint min,out uint value,out uint max);
    [DllImport("dxva2.dll",SetLastError=true)]static extern bool SetMonitorBrightness(IntPtr monitor,uint value);
    static readonly SemaphoreSlim gate=new(1,1);
    static PhysicalMonitor[] Open(Native.Point point){IntPtr monitor=MonitorFromPoint(point,2);if(!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor,out uint count)||count is 0 or >16)return Array.Empty<PhysicalMonitor>();var monitors=new PhysicalMonitor[count];return GetPhysicalMonitorsFromHMONITOR(monitor,count,monitors)?monitors:Array.Empty<PhysicalMonitor>();}
    internal static async Task<BrightnessSnapshot> ReadAsync(Native.Point point){await gate.WaitAsync();try{return await Task.Run(()=>{var monitors=Open(point);try{foreach(var monitor in monitors)if(GetMonitorBrightness(monitor.Handle,out uint min,out uint value,out uint max)&&max>min)return new BrightnessSnapshot(true,(value-min)*100d/(max-min),monitor.Description);return new BrightnessSnapshot(false,0,"Luminosité de l’écran","Cet écran ne fournit pas de réglage DDC/CI à Windows.");}finally{if(monitors.Length>0)DestroyPhysicalMonitors((uint)monitors.Length,monitors);}});}finally{gate.Release();}}
    internal static async Task SetAsync(Native.Point point,double percent){await gate.WaitAsync();try{await Task.Run(()=>{var monitors=Open(point);bool changed=false;try{foreach(var monitor in monitors)if(GetMonitorBrightness(monitor.Handle,out uint min,out _,out uint max)&&max>min){uint value=min+(uint)Math.Round(Math.Clamp(percent,0,100)*(max-min)/100d);if(!SetMonitorBrightness(monitor.Handle,value))throw new InvalidOperationException("L’écran a refusé le réglage de luminosité.");changed=true;}if(!changed)throw new InvalidOperationException("Luminosité non réglable sur cet écran.");}finally{if(monitors.Length>0)DestroyPhysicalMonitors((uint)monitors.Length,monitors);}});}finally{gate.Release();}}
}
