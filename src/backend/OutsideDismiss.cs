// language: C# 12, file: OutsideDismiss.cs, target: Windows 11 / .NET 8, event-driven outside clicks without consuming the original input
using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace PanneauxGlass;
internal sealed class OutsideDismiss:IDisposable {
    delegate IntPtr MouseCallback(int code,IntPtr message,IntPtr data);
    delegate void ForegroundCallback(IntPtr hook,uint kind,IntPtr hwnd,int objectId,int child,uint thread,uint time);
    [StructLayout(LayoutKind.Sequential)] struct MouseData {public Native.Point Point;public uint Mouse,Flags,Time;public UIntPtr Extra;}
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookExW(int kind,MouseCallback callback,IntPtr module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Native.Point point);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,ForegroundCallback callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
    [DllImport("kernel32.dll")] static extern uint GetTickCount();
    readonly Func<IntPtr,bool>? additionalOwnership;readonly MouseCallback mouseCallback;readonly ForegroundCallback foregroundCallback;readonly Action close;readonly Func<bool> enabled;readonly Dispatcher dispatcher;readonly uint processId=GetCurrentProcessId();IntPtr mouseHook,foregroundHook,openingForeground;bool disposed,active;uint generation,armedAt;int pendingReasons;
    internal OutsideDismiss(Action dismiss,Func<bool> canDismiss,Func<IntPtr,bool>? owns=null){additionalOwnership=owns;close=dismiss;enabled=canDismiss;dispatcher=Dispatcher.CurrentDispatcher;mouseCallback=Mouse;clicks.Tick+=(_,_)=>{bool down=(GetAsyncKeyState(1)&0x8000)!=0||(GetAsyncKeyState(2)&0x8000)!=0;if(down&&!wasDown&&enabled()){Native.GetCursorPos(out var p);if(!Own(WindowFromPoint(p)))Queue(false);}wasDown=down;};foregroundCallback=(_,_,hwnd,_,_,_,time)=>{DesktopRecents.Observe(hwnd);if(!active||unchecked((int)(time-armedAt))<0)return;if(Own(hwnd)){openingForeground=IntPtr.Zero;return;}if(hwnd!=openingForeground)Queue(true);};foregroundHook=SetWinEventHook(3,3,IntPtr.Zero,foregroundCallback,0,0,0);}
    readonly DispatcherTimer clicks=new(){Interval=TimeSpan.FromMilliseconds(35)};bool wasDown;
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    internal void SetActive(bool value){if(disposed)return;active=value;generation++;pendingReasons=0;armedAt=GetTickCount();var foreground=Native.GetForegroundWindow();openingForeground=value&&!Own(foreground)?foreground:IntPtr.Zero;if(value){wasDown=false;clicks.Start();}else clicks.Stop();}

    bool Own(IntPtr hwnd){if(hwnd==IntPtr.Zero)return false;GetWindowThreadProcessId(hwnd,out uint pid);return pid==processId||additionalOwnership?.Invoke(hwnd)==true;}
    IntPtr Mouse(int code,IntPtr message,IntPtr data){try{long kind=message.ToInt64();if(code>=0&&(kind is 0x201 or 0x204 or 0x207 or 0x20B)&&enabled()){var point=Marshal.PtrToStructure<MouseData>(data).Point;if(!Own(WindowFromPoint(point)))Queue(false);else openingForeground=IntPtr.Zero;}}catch{}return CallNextHookEx(mouseHook,code,message,data);}
    void Queue(bool foreground){if(disposed||!active||!enabled())return;int reason=foreground?2:1;if(pendingReasons!=0){pendingReasons|=reason;return;}pendingReasons=reason;uint expected=generation;
        dispatcher.BeginInvoke(DispatcherPriority.Input,(Action)(()=>{if(disposed||!active||generation!=expected)return;int reasons=pendingReasons;pendingReasons=0;if(!enabled())return;
            // Hiding the active rail temporarily activates the previous app. Its
            // queued event must not dismiss panels which now have the focus.
            // A shortcut may open without Windows granting foreground focus; ignore
            // the unchanged initial foreground until our app receives focus or a click.
            if((reasons&1)!=0||((reasons&2)!=0&&!Own(Native.GetForegroundWindow())&&Native.GetForegroundWindow()!=openingForeground))close();}));}
    public void Dispose(){if(disposed)return;disposed=true;clicks.Stop();if(mouseHook!=IntPtr.Zero)UnhookWindowsHookEx(mouseHook);if(foregroundHook!=IntPtr.Zero)UnhookWinEvent(foregroundHook);mouseHook=foregroundHook=IntPtr.Zero;}
}
