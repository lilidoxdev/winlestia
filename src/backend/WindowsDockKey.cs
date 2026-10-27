// language: C# 12, file: WindowsDockKey.cs, target: Windows 11 / .NET 8, Windows key routing with chord preservation
using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace PanneauxGlass;
internal readonly record struct DockKeyDecision(bool Consume,bool Reveal,byte Down=0,byte Up=0);
internal sealed class DockKeyPolicy {
    int held;bool chord;byte forwarded,first;
    internal DockKeyDecision Feed(byte key,bool up){bool windows=key is 0x5B or 0x5C;if(windows){int bit=key==0x5B?1:2;if(!up){if(held==0){chord=false;first=key;}held|=bit;return new(true,false);}held&=~bit;if(held!=0)return new(true,false);byte release=forwarded;forwarded=0;bool reveal=!chord;chord=false;return new(true,reveal,Up:release);}if(held!=0&&!up){chord=true;if(forwarded==0){forwarded=first;return new(false,false,Down:first);}}return new(false,false);}
    internal byte Reset(){byte key=forwarded;held=0;forwarded=0;chord=false;return key;}
}
internal sealed class WindowsDockKey:IDisposable {
    [StructLayout(LayoutKind.Sequential)]struct KeyData {public uint Key,Scan,Flags,Time;public UIntPtr Extra;}
    delegate IntPtr Callback(int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWindowsHookExW(int id,Callback callback,IntPtr module,uint thread);
    [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("user32.dll")]static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
    readonly Callback callback;readonly DockKeyPolicy policy=new();readonly Action reveal;readonly Dispatcher dispatcher=Dispatcher.CurrentDispatcher;IntPtr hook;
    internal WindowsDockKey(Action action){reveal=action;callback=Handle;hook=SetWindowsHookExW(13,callback,Native.GetModuleHandleW(null),0);if(hook==IntPtr.Zero)throw new InvalidOperationException("La touche Windows ne peut pas être reliée au dock.");}
    IntPtr Handle(int code,IntPtr message,IntPtr data){if(code<0)return CallNextHookEx(hook,code,message,data);var key=Marshal.PtrToStructure<KeyData>(data);if((key.Flags&0x10)!=0)return CallNextHookEx(hook,code,message,data);long kind=message.ToInt64();if(kind is not (0x100 or 0x101 or 0x104 or 0x105))return CallNextHookEx(hook,code,message,data);var result=policy.Feed((byte)key.Key,kind is 0x101 or 0x105);if(result.Down!=0)keybd_event(result.Down,0,0,UIntPtr.Zero);if(result.Up!=0)keybd_event(result.Up,0,2,UIntPtr.Zero);if(result.Reveal)dispatcher.BeginInvoke(DispatcherPriority.Input,reveal);return result.Consume?new IntPtr(1):CallNextHookEx(hook,code,message,data);}
    public void Dispose(){if(hook==IntPtr.Zero)return;byte release=policy.Reset();if(release!=0)keybd_event(release,0,2,UIntPtr.Zero);UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}
}
