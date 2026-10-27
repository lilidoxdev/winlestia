// C#12 / WindowTiler.cs / Windows11. Reversible per-monitor tiling; no global input hook.
using System.Diagnostics;using System;using System.Linq;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Windows.Threading;using Forms=System.Windows.Forms;
namespace PanneauxGlass;
internal sealed class WindowTiler:IDisposable{
 [StructLayout(LayoutKind.Sequential)]internal struct Placement{public int Length,Flags,Show;public Native.Point Min,Max;public Native.Rect Normal;}
 sealed class Managed{internal Placement Original;internal uint Pid;internal Native.Rect Last;internal bool Placed;internal Native.Rect? Constrained;}
 [DllImport("user32.dll")]static extern bool GetWindowPlacement(IntPtr h,ref Placement placement);
 [DllImport("user32.dll")]static extern bool SetWindowPlacement(IntPtr h,ref Placement placement);
 [DllImport("user32.dll")]static extern bool ShowWindowAsync(IntPtr h,int command);
 [DllImport("user32.dll")]static extern bool IsZoomed(IntPtr h);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr Style(IntPtr h,int index);
 [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr h,uint command);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr h,out Native.Rect rect);
 [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr h,ref Native.Point point);
 [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(IntPtr h,int attribute,out int value,int size);
 [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")]static extern int ExtendedBounds(IntPtr h,int attribute,out Native.Rect value,int size);
 readonly Dictionary<IntPtr,Managed> managed=new();readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(700)};readonly Func<IEnumerable<IntPtr>> source;readonly bool fixtureSource;bool enabled,disposed;
 sealed record Motion(Native.Rect From,Native.Rect To,long Started);
 readonly Dictionary<IntPtr,Motion> motions=new();
 readonly DispatcherTimer animation=new(DispatcherPriority.Render){Interval=TimeSpan.FromMilliseconds(16)};
 internal uint ExcludedProcess;
 void Move(IntPtr h,Native.Rect wanted){var current=Visible(h);TaskbarNative.GetWindowRect(h,out var outer);
  if(TaskbarNative.SetWindowPos(h,IntPtr.Zero,wanted.Left-current.Left+outer.Left,wanted.Top-current.Top+outer.Top,wanted.Right-wanted.Left+outer.Right-outer.Left-current.Right+current.Left,wanted.Bottom-wanted.Top+outer.Bottom-outer.Top-current.Bottom+current.Top,0x4014)&&managed.TryGetValue(h,out var item)){item.Last=wanted;item.Placed=true;}}
 void Animate(){
  bool dragging=(GetAsyncKeyState(1)&0x8000)!=0;
  foreach(var pair in motions.ToArray()){var h=pair.Key;var motion=pair.Value;
   GetWindowThreadProcessId(h,out uint pid);
   if(dragging||!managed.TryGetValue(h,out var item)||pid!=item.Pid||!Native.IsWindow(h)||TaskbarNative.IsIconic(h)||IsZoomed(h)||Fullscreen(h,Forms.Screen.FromHandle(h))){motions.Remove(h);continue;}
   double t=Math.Clamp(Stopwatch.GetElapsedTime(motion.Started).TotalMilliseconds/240,0,1),ease=1-Math.Pow(1-t,3);
   int Lerp(int a,int b)=>(int)Math.Round(a+(b-a)*ease);
   Move(h,new(){Left=Lerp(motion.From.Left,motion.To.Left),Top=Lerp(motion.From.Top,motion.To.Top),Right=Lerp(motion.From.Right,motion.To.Right),Bottom=Lerp(motion.From.Bottom,motion.To.Bottom)});
   if(t>=1)motions.Remove(h);
  }
  if(motions.Count==0)animation.Stop();
 }

 internal WindowTiler(Func<IEnumerable<IntPtr>>? source=null){fixtureSource=source!=null;this.source=source??Collect;timer.Tick+=(_,_)=>Tick();animation.Tick+=(_,_)=>Animate();}
 internal void SetEnabled(bool value){if(disposed||enabled==value)return;enabled=value;if(value)timer.Start();else{timer.Stop();Restore();}}
 IEnumerable<IntPtr> Collect(){var list=new List<IntPtr>();TaskbarNative.EnumWindows((h,_)=>{if(Eligible(h))list.Add(h);return true;},IntPtr.Zero);return list;}
 bool Eligible(IntPtr h){if(!Native.IsWindow(h)||!TaskbarNative.IsWindowVisible(h)||TaskbarNative.IsIconic(h)||(!fixtureSource&&Native.Owns(h)))return false;GetWindowThreadProcessId(h,out uint pid);if(pid==ExcludedProcess)return false;
  long style=Style(h,-16).ToInt64(),ex=Style(h,-20).ToInt64();if((style&0x40000)==0||(style&0x40000000)!=0||(ex&0x80)!=0||GetWindow(h,4)!=IntPtr.Zero)return false;
  if(DwmGetWindowAttribute(h,14,out int cloaked,4)==0&&cloaked!=0)return false;return !string.IsNullOrWhiteSpace(Native.Title(h))&&TaskbarNative.ClassName(h) is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
 }
 static Native.Rect Visible(IntPtr h){TaskbarNative.GetWindowRect(h,out var rect);return ExtendedBounds(h,9,out var frame,Marshal.SizeOf<Native.Rect>())==0?frame:rect;}
 static bool Same(Native.Rect a,Native.Rect b)=>Math.Abs(a.Left-b.Left)<=2&&Math.Abs(a.Top-b.Top)<=2&&Math.Abs(a.Right-b.Right)<=2&&Math.Abs(a.Bottom-b.Bottom)<=2;
 static bool Fullscreen(IntPtr h,Forms.Screen screen){var point=new Native.Point();return GetClientRect(h,out var client)&&ClientToScreen(h,ref point)&&Math.Abs(point.X-screen.Bounds.Left)<=2&&Math.Abs(point.Y-screen.Bounds.Top)<=2&&Math.Abs(point.X+client.Right-screen.Bounds.Right)<=2&&Math.Abs(point.Y+client.Bottom-screen.Bounds.Bottom)<=2;}
 internal static Native.Rect[] Layout(Native.Rect area,int count,int gap){var result=new List<Native.Rect>();void Split(Native.Rect r,int n){if(n==1){result.Add(r);return;}int first=n/2;bool vertical=r.Right-r.Left>=r.Bottom-r.Top;
  if(vertical){int middle=r.Left+(r.Right-r.Left-gap)/2;Split(new(){Left=r.Left,Top=r.Top,Right=middle,Bottom=r.Bottom},first);Split(new(){Left=middle+gap,Top=r.Top,Right=r.Right,Bottom=r.Bottom},n-first);}else{int middle=r.Top+(r.Bottom-r.Top-gap)/2;Split(new(){Left=r.Left,Top=r.Top,Right=r.Right,Bottom=middle},first);Split(new(){Left=r.Left,Top=middle+gap,Right=r.Right,Bottom=r.Bottom},n-first);}}
  if(count>0&&area.Right>area.Left&&area.Bottom>area.Top)Split(area,count);return result.ToArray();}
 internal void Tick(){if(!enabled||disposed||(GetAsyncKeyState(1)&0x8000)!=0)return;try{
  foreach(var h in managed.Keys.ToArray()){GetWindowThreadProcessId(h,out uint pid);if(!Native.IsWindow(h)||pid!=managed[h].Pid)managed.Remove(h);}
  var handles=source().Where(Eligible).ToArray();var added=new HashSet<IntPtr>();foreach(var h in handles)if(!managed.ContainsKey(h)){var place=new Placement{Length=Marshal.SizeOf<Placement>()};if(GetWindowPlacement(h,ref place)){GetWindowThreadProcessId(h,out uint pid);managed[h]=new(){Original=place,Pid=pid};added.Add(h);}}
  foreach(var group in managed.Keys.Where(handles.Contains).GroupBy(h=>Forms.Screen.FromHandle(h).DeviceName)){
   var windows=group.ToArray();var screen=Forms.Screen.FromHandle(windows[0]);if(windows.Any(h=>Fullscreen(h,screen)))continue;
   bool fresh=windows.Any(added.Contains);if(windows.Any(IsZoomed)){if(fresh)foreach(var h in windows.Where(IsZoomed))ShowWindowAsync(h,9);continue;}
   var work=screen.WorkingArea;int gap=Math.Max(4,(int)Math.Round(8*TaskbarNative.Scale(screen)));int capacity=Math.Min(16,Math.Max(1,work.Width/240)*Math.Max(1,work.Height/140));windows=windows.TakeLast(capacity).ToArray();
   var slots=Layout(new(){Left=work.Left+gap,Top=work.Top+gap,Right=work.Right-gap,Bottom=work.Bottom-gap},windows.Length,gap);
   for(int i=0;i<windows.Length;i++){var h=windows[i];var wanted=slots[i];var current=Visible(h);var item=managed[h];if(motions.TryGetValue(h,out var active)&&Same(active.To,wanted))continue;if(Same(current,wanted))continue;
    if(item.Constrained is Native.Rect constrained&&Same(constrained,wanted))continue;
    if(item.Placed&&Same(item.Last,wanted)&&((current.Right-current.Left)>(wanted.Right-wanted.Left)+2||(current.Bottom-current.Top)>(wanted.Bottom-wanted.Top)+2)){item.Constrained=wanted;item.Last=current;continue;}
    item.Constrained=null;motions[h]=new(current,wanted,Stopwatch.GetTimestamp());animation.Start();
   }
  }
 }catch(Exception ex){Store.Log("MosaÃ¯que: "+ex.Message);}}
 void Restore(){animation.Stop();motions.Clear();foreach(var item in managed){GetWindowThreadProcessId(item.Key,out uint pid);if(!Native.IsWindow(item.Key)||pid!=item.Value.Pid||TaskbarNative.IsIconic(item.Key)||IsZoomed(item.Key))continue;if(item.Value.Placed&&Same(Visible(item.Key),item.Value.Last)){var placement=item.Value.Original;SetWindowPlacement(item.Key,ref placement);}}managed.Clear();}
 public void Dispose(){if(disposed)return;timer.Stop();Restore();disposed=true;}
}
