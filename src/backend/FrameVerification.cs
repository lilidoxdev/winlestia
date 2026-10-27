// C# 12 / FrameVerification.cs / Windows .NET 8. Real work-area leases, always restored in finally.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
namespace PanneauxGlass;
internal static class FrameVerification {
    [StructLayout(LayoutKind.Sequential)]struct MonitorInfo{public int Size;public Native.Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]static extern IntPtr MonitorFromPoint(Native.Point point,uint flags);
    [DllImport("user32.dll")]static extern bool GetMonitorInfoW(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out Native.Rect rect);
    [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref Native.Point point);
    static Native.Rect Work(Forms.Screen screen){var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};GetMonitorInfoW(MonitorFromPoint(new(){X=screen.Bounds.Left+10,Y=screen.Bounds.Top+10},2),ref info);return info.Work;}
    internal static int Run(){
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var checks=new List<object>();bool passed=true;var screens=Forms.Screen.AllScreens;var before=screens.Select(Work).ToArray();using var frame=new ScreenFrameReservations();
        void Check(string name,bool ok){checks.Add(new{name,pass=ok});passed&=ok;}
        app.Exit+=(_,_)=>File.WriteAllText(Path.Combine(Store.Root,"frame-verification.json"),JsonSerializer.Serialize(new{allPassed=passed,checks},new JsonSerializerOptions{WriteIndented=true}));
        Window? probe=null;
        app.Startup+=(_,_)=>{try{frame.Enable(false);double scale=TaskbarNative.Scale(screens[0]);probe=new Window{Title="Vérification du cadre Panneaux",Width=500,Height=280,Left=(screens[0].Bounds.Left+100)/scale,Top=(screens[0].Bounds.Top+100)/scale,ShowInTaskbar=false,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual};probe.Show();probe.WindowState=WindowState.Maximized;}catch(Exception ex){Check(ex.Message,false);app.Shutdown(1);return;}
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(800)};timer.Tick+=(_,_)=>{timer.Stop();try{
                var after=screens.Select(Work).ToArray();
                for(int i=0;i<screens.Length;i++){int inset=(int)Math.Round(20*TaskbarNative.Scale(screens[i]));var a=after[i];var b=before[i];Check("Native four-edge reservation "+screens[i].DeviceName,a.Left>=b.Left+inset&&a.Top>=b.Top+inset&&a.Right<=b.Right-inset&&a.Bottom<=b.Bottom-inset);}
                var handle=new WindowInteropHelper(probe!).Handle;var origin=new Native.Point();bool available=GetClientRect(handle,out var client)&&ClientToScreen(handle,ref origin);var area=Work(Forms.Screen.FromHandle(handle));Check("Maximized application stays inside native frame",available&&origin.X>=area.Left&&origin.Y>=area.Top&&origin.X+client.Right<=area.Right&&origin.Y+client.Bottom<=area.Bottom);
            }finally{
                probe?.Close();frame.Disable();var settle=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};settle.Tick+=(_,_)=>{settle.Stop();var restored=screens.Select(Work).ToArray();Check("Work areas restored",restored.Select((a,i)=>a.Left==before[i].Left&&a.Top==before[i].Top&&a.Right==before[i].Right&&a.Bottom==before[i].Bottom).All(x=>x));
                File.WriteAllText(Path.Combine(Store.Root,"frame-verification.json"),JsonSerializer.Serialize(new{allPassed=passed,checks},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(passed?0:1);};settle.Start();
            }};timer.Start();};return app.Run();
    }
}
