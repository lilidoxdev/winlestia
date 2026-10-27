// C# 12 / ScreenFrameReservations.cs / Windows appbars. OS-owned, automatically removed if the process exits.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms=System.Windows.Forms;
namespace PanneauxGlass;
internal sealed class ScreenFrameReservations:IDisposable {
    sealed class Edge:IDisposable {
        readonly HwndSource source;readonly Forms.Screen screen;readonly uint edge,message;readonly uint explorerRestart=TaskbarNative.RegisterWindowMessage("TaskbarCreated");int inset;bool placing,closed;Native.Rect last;
        internal Edge(Forms.Screen screen,uint edge,int inset){this.screen=screen;this.edge=edge;this.inset=inset;
            source=new HwndSource(new HwndSourceParameters("Panneaux · marge du cadre"){WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x08000080,Width=1,Height=1});source.AddHook(Hook);message=TaskbarNative.RegisterWindowMessage("PanneauxFrame-"+source.Handle);var data=TaskbarNative.Data(source.Handle);data.Callback=message;
            if(TaskbarNative.SHAppBarMessage(0,ref data)==UIntPtr.Zero)throw new InvalidOperationException("Réservation du cadre refusée.");Place();
        }
        internal void UpdateThickness(){inset=(int)Math.Round((Store.State.FrameThickness+4)*TaskbarNative.Scale(screen));Place();}
        void Place(){if(placing||closed)return;placing=true;try{var b=screen.Bounds;var data=TaskbarNative.Data(source.Handle);data.Edge=edge;data.Rect=new(){Left=b.Left,Top=b.Top,Right=b.Right,Bottom=b.Bottom};TaskbarNative.SHAppBarMessage(2,ref data);
            switch(edge){case 0:data.Rect.Right=data.Rect.Left+inset;break;case 1:data.Rect.Bottom=data.Rect.Top+inset;break;case 2:data.Rect.Left=data.Rect.Right-inset;break;case 3:data.Rect.Top=data.Rect.Bottom-inset;break;}
            if(!Same(data.Rect,last)){TaskbarNative.SHAppBarMessage(3,ref data);last=data.Rect;TaskbarNative.SetWindowPos(source.Handle,IntPtr.Zero,last.Left,last.Top,Math.Max(1,last.Right-last.Left),Math.Max(1,last.Bottom-last.Top),0x14);}
        }finally{placing=false;}}
        IntPtr Hook(IntPtr h,int msg,IntPtr wp,IntPtr lp,ref bool handled){if((uint)msg==message&&wp.ToInt32()==1)source.Dispatcher.BeginInvoke(DispatcherPriority.Background,(Action)Place);else if((uint)msg==explorerRestart&&!closed){var data=TaskbarNative.Data(source.Handle);data.Callback=message;TaskbarNative.SHAppBarMessage(0,ref data);last=default;source.Dispatcher.BeginInvoke((Action)Place);}return IntPtr.Zero;}
        public void Dispose(){if(closed)return;closed=true;var data=TaskbarNative.Data(source.Handle);TaskbarNative.SHAppBarMessage(1,ref data);source.RemoveHook(Hook);source.Dispose();}
    }
    readonly List<Edge> edges=new();readonly Dictionary<IntPtr,(Native.Rect original,Native.Rect placed)> adjusted=new();readonly Dispatcher dispatcher=Dispatcher.CurrentDispatcher;
    readonly DispatcherTimer fullscreenCheck=new(){Interval=TimeSpan.FromMilliseconds(400)};
    readonly DispatcherTimer changes=new(){Interval=TimeSpan.FromMilliseconds(160)};readonly TaskbarNative.WinEvent callback;readonly List<IntPtr> hooks=new();readonly HashSet<IntPtr> pending=new();bool enabled,disposed;
    internal ScreenFrameReservations(){fullscreenCheck.Tick+=(_,_)=>Constrain(Native.GetForegroundWindow());callback=(_,kind,h,obj,child,_,_)=>{if(h==IntPtr.Zero||obj!=0||child!=0||Native.Owns(h))return;dispatcher.BeginInvoke(DispatcherPriority.Background,(Action)(()=>{if(!enabled||disposed)return;pending.Add(h);changes.Stop();changes.Start();}));};changes.Tick+=(_,_)=>{changes.Stop();foreach(var h in pending.ToArray())Constrain(h);pending.Clear();};}
    internal void Enable(bool constrainWindows=true){if(enabled||disposed)return;enabled=true;try{foreach(var screen in Forms.Screen.AllScreens)for(uint edge=0;edge<4;edge++)edges.Add(new Edge(screen,edge,(int)Math.Round((Store.State.FrameThickness+4)*TaskbarNative.Scale(screen))));
            if(!constrainWindows)return;fullscreenCheck.Start();
            foreach(uint kind in new uint[]{3,0x000B,0x8002})hooks.Add(TaskbarNative.SetWinEventHook(kind,kind,IntPtr.Zero,callback,0,0,0));
            foreach(var window in TaskbarNative.Applications())Constrain(window.Handle);
        }catch{Disable();throw;}}
    internal void UpdateThickness(){if(enabled){foreach(var edge in edges)edge.UpdateThickness();foreach(var window in TaskbarNative.Applications())Constrain(window.Handle);}}
    internal void DisplaysChanged(){if(!enabled)return;Disable();Enable();}
    static bool Same(Native.Rect a,Native.Rect b)=>a.Left==b.Left&&a.Top==b.Top&&a.Right==b.Right&&a.Bottom==b.Bottom;
    [DllImport("user32.dll")]static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr Style(IntPtr h,int index);
    [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr h,out Native.Rect rect);
    [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr h,ref Native.Point point);
    readonly Dictionary<IntPtr,(IntPtr original,string shape)> rounded=new();
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")]static extern int ExtendedBounds(IntPtr h,int attribute,out Native.Rect value,int size);
    [DllImport("gdi32.dll")]static extern bool EqualRgn(IntPtr first,IntPtr second);
    [DllImport("gdi32.dll")]static extern IntPtr CreateRoundRectRgn(int l,int t,int r,int b,int ew,int eh);
    [DllImport("gdi32.dll")]static extern IntPtr CreateRectRgn(int l,int t,int r,int b);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr value);
    [DllImport("user32.dll")]static extern int GetWindowRgn(IntPtr h,IntPtr region);
    [DllImport("user32.dll")]static extern int SetWindowRgn(IntPtr h,IntPtr region,bool redraw);
    void RestoreRound(IntPtr h){if(!rounded.Remove(h,out var saved))return;if(!Native.IsWindow(h)||SetWindowRgn(h,saved.original,true)==0){if(saved.original!=IntPtr.Zero)DeleteObject(saved.original);}}
    void RoundMaximized(IntPtr h){
        if(!IsZoomed(h)){RestoreRound(h);return;}
        var origin=new Native.Point();var screenBounds=Forms.Screen.FromHandle(h).Bounds;
        if(GetClientRect(h,out var client)&&ClientToScreen(h,ref origin)&&Math.Abs(origin.X-screenBounds.Left)<=2&&Math.Abs(origin.Y-screenBounds.Top)<=2&&Math.Abs(origin.X+client.Right-screenBounds.Right)<=2&&Math.Abs(origin.Y+client.Bottom-screenBounds.Bottom)<=2){RestoreRound(h);return;}
        if(!TaskbarNative.GetWindowRect(h,out var rect))return;
        var screen=Forms.Screen.FromHandle(h);var area=screen.WorkingArea;int radius=(int)Math.Round(26*TaskbarNative.Scale(screen));
        int left=Math.Max(0,area.Left-rect.Left),top=Math.Max(0,area.Top-rect.Top),right=Math.Min(rect.Right-rect.Left,area.Right-rect.Left),bottom=Math.Min(rect.Bottom-rect.Top,area.Bottom-rect.Top);
        // Native resize borders are outside the visible DWM frame; clip the visible intersection.
        if(ExtendedBounds(h,9,out var visible,Marshal.SizeOf<Native.Rect>())==0){left=Math.Max(left,visible.Left-rect.Left);top=Math.Max(top,visible.Top-rect.Top);right=Math.Min(right,visible.Right-rect.Left);bottom=Math.Min(bottom,visible.Bottom-rect.Top);}
        if(right<=left||bottom<=top)return;
        string shape=$"{left},{top},{right},{bottom},{radius}";rounded.TryGetValue(h,out var previous);
        var region=CreateRoundRectRgn(left,top,right+1,bottom+1,radius*2,radius*2);
        if(region==IntPtr.Zero)return;
        if(previous.shape==shape){var actual=CreateRectRgn(0,0,0,0);bool matches=GetWindowRgn(h,actual)!=0&&EqualRgn(actual,region);DeleteObject(actual);if(matches){DeleteObject(region);return;}}
        IntPtr original=previous.original;
        if(!rounded.ContainsKey(h)){original=CreateRectRgn(0,0,0,0);if(GetWindowRgn(h,original)==0){DeleteObject(original);original=IntPtr.Zero;}}
        if(SetWindowRgn(h,region,true)!=0)rounded[h]=(original,shape);else{DeleteObject(region);if(!rounded.ContainsKey(h)&&original!=IntPtr.Zero)DeleteObject(original);}
    }
    void Constrain(IntPtr h){
        if(!enabled||!Native.IsWindow(h)||!TaskbarNative.IsWindowVisible(h)||TaskbarNative.IsIconic(h)||Native.Owns(h))return;
        if((Style(h,-16).ToInt64()&0x40000000)!=0||(Native.ExtendedStyle(h)&0x80)!=0||string.IsNullOrWhiteSpace(Native.Title(h))||TaskbarNative.ClassName(h) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")return;
        RoundMaximized(h);if(IsZoomed(h))return;
        if(!TaskbarNative.GetWindowRect(h,out var rect))return;var screen=Forms.Screen.FromHandle(h);var bounds=screen.Bounds;var origin=new Native.Point();
        if(GetClientRect(h,out var client)&&ClientToScreen(h,ref origin)&&origin.X<=bounds.Left+1&&origin.Y<=bounds.Top+1&&origin.X+client.Right>=bounds.Right-1&&origin.Y+client.Bottom>=bounds.Bottom-1)return;
        var area=screen.WorkingArea;int w=Math.Min(rect.Right-rect.Left,area.Width),height=Math.Min(rect.Bottom-rect.Top,area.Height);if(w<40||height<40)return;
        int x=Math.Clamp(rect.Left,area.Left,area.Right-w),y=Math.Clamp(rect.Top,area.Top,area.Bottom-height);var next=new Native.Rect{Left=x,Top=y,Right=x+w,Bottom=y+height};if(Same(rect,next))return;
        if(!adjusted.ContainsKey(h))adjusted[h]=(rect,next);else adjusted[h]=(adjusted[h].original,next);
        TaskbarNative.SetWindowPos(h,IntPtr.Zero,x,y,w,height,0x14);
    }
    internal void Disable(){foreach(var h in rounded.Keys.ToArray())RestoreRound(h);enabled=false;fullscreenCheck.Stop();changes.Stop();pending.Clear();foreach(var hook in hooks)if(hook!=IntPtr.Zero)TaskbarNative.UnhookWinEvent(hook);hooks.Clear();foreach(var edge in edges)edge.Dispose();edges.Clear();
        foreach(var item in adjusted)if(Native.IsWindow(item.Key)&&!IsZoomed(item.Key)&&TaskbarNative.GetWindowRect(item.Key,out var rect)&&Same(rect,item.Value.placed)){var old=item.Value.original;TaskbarNative.SetWindowPos(item.Key,IntPtr.Zero,old.Left,old.Top,old.Right-old.Left,old.Bottom-old.Top,0x14);}adjusted.Clear();
    }
    public void Dispose(){if(disposed)return;Disable();disposed=true;}
}
