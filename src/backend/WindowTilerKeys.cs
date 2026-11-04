// C#12 / WindowTilerKeys.cs / Keyboard actions, drag-drop swap and process exclusions for WindowTiler. Hotkeys exist only while tiling is enabled.
using System;using System.Collections.Generic;using System.Diagnostics;using System.IO;using System.Linq;using System.Runtime.InteropServices;using System.Windows.Interop;using Forms=System.Windows.Forms;
namespace PanneauxGlass;
internal sealed partial class WindowTiler{
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")]static extern bool GetCursorPos(out Native.Point p);
 readonly TileOrder order=new();readonly HashSet<IntPtr> floating=new();
 Dictionary<IntPtr,(string screen,int index,int count,Native.Rect slot)> tiles=new();
 readonly Dictionary<string,Dictionary<string,double>> ratios=new();   // per screen, per split path; session memory only
 readonly Dictionary<uint,string> processNames=new();
 HashSet<string>? excluded;HwndSource? keySink;readonly List<int> keyIds=new();
 // Ids 41-44 focus, 45-48 swap (Left,Right,Up,Down), 49 float, 50-51 grow, 52-54 shrink, 55 exclude application.
 static readonly (int id,uint mods,uint vk,string label)[] Bindings={
  (41,3,0x25,"Ctrl + Alt + ←"),(42,3,0x27,"Ctrl + Alt + →"),(43,3,0x26,"Ctrl + Alt + ↑"),(44,3,0x28,"Ctrl + Alt + ↓"),
  (45,7,0x25,"Ctrl + Alt + Maj + ←"),(46,7,0x27,"Ctrl + Alt + Maj + →"),(47,7,0x26,"Ctrl + Alt + Maj + ↑"),(48,7,0x28,"Ctrl + Alt + Maj + ↓"),
  (49,7,0x46,"Ctrl + Alt + Maj + F"),(50,7,0xBB,"Ctrl + Alt + Maj + +"),(51,7,0x6B,"Ctrl + Alt + Maj + pavé +"),
  (52,7,0xBD,"Ctrl + Alt + Maj + -"),(53,7,0x6D,"Ctrl + Alt + Maj + pavé -"),(54,7,0x36,"Ctrl + Alt + Maj + 6"),(55,7,0x58,"Ctrl + Alt + Maj + X")};
 double Ratio(string screen,string path)=>ratios.TryGetValue(screen,out var d)&&d.TryGetValue(path,out var r)?r:.5;
 // A refused combination is logged and skipped; the others stay active. Nothing is registered unless tiling is on (and never for test fixtures).
 void Keys(bool on){
  if(!on){if(keySink==null)return;try{foreach(var id in keyIds)Native.UnregisterHotKey(keySink.Handle,id);keySink.RemoveHook(KeyHook);keySink.Dispose();}catch{}keyIds.Clear();keySink=null;return;}
  if(fixtureSource||keySink!=null)return;
  try{keySink=new HwndSource(new HwndSourceParameters("PanneauxTilerKeys"){ParentWindow=new IntPtr(-3),WindowStyle=0,Width=0,Height=0});keySink.AddHook(KeyHook);
   foreach(var b in Bindings){if(Native.RegisterHotKey(keySink.Handle,b.id,b.mods|0x4000,b.vk))keyIds.Add(b.id);else Store.Log("Tuilage · "+b.label+" refusé (erreur "+Marshal.GetLastWin32Error()+"), raccourci ignoré");}
  }catch(Exception ex){Store.Log("Tuilage · raccourcis indisponibles : "+ex.Message);}
 }
 IntPtr KeyHook(IntPtr w,int msg,IntPtr wp,IntPtr lp,ref bool handled){if(msg==0x0312&&keyIds.Contains(wp.ToInt32())){handled=true;try{OnHotkey(wp.ToInt32());}catch(Exception ex){Store.Log("Tuilage · raccourci : "+ex.Message);}}return IntPtr.Zero;}
 void OnHotkey(int id){if(!enabled||disposed)return;
  if(id is >=41 and <=44)Focus((TileDir)(id-41));else if(id is >=45 and <=48)SwapDir((TileDir)(id-45));else if(id==49)ToggleFloat();
  else if(id is 50 or 51)Resize(TileLogic.RatioStep);else if(id is 52 or 53 or 54)Resize(-TileLogic.RatioStep);else if(id==55)ToggleExcludeActive();}
 void RequestTick(){if(enabled&&!coalesce.IsEnabled)coalesce.Start();}
 IntPtr Active(){var fg=GetForegroundWindow();return fg!=IntPtr.Zero&&tiles.ContainsKey(fg)?fg:IntPtr.Zero;}
 int Neighbour(IntPtr a,TileDir dir,out List<KeyValuePair<IntPtr,(string screen,int index,int count,Native.Rect slot)>> list){list=tiles.ToList();int i=list.FindIndex(p=>p.Key==a);return TileLogic.Neighbor(list.Select(p=>p.Value.slot).ToList(),i,dir);}
 void Focus(TileDir dir){var a=Active();if(a==IntPtr.Zero)return;int n=Neighbour(a,dir,out var list);if(n<0)return;if(!SetForegroundWindow(list[n].Key))Store.Log("Tuilage · focus refusé par Windows");}
 void SwapDir(TileDir dir){var a=Active();if(a==IntPtr.Zero)return;int n=Neighbour(a,dir,out var list);if(n>=0){SwapPair(a,list[n].Key);return;}
  // No neighbour that way: an empty adjacent screen receives the window, filling its work area.
  var screens=Forms.Screen.AllScreens;int cur=Array.FindIndex(screens,s=>s.DeviceName==tiles[a].screen);if(cur<0)return;
  int t=TileLogic.Neighbor(screens.Select(s=>new Native.Rect{Left=s.Bounds.Left,Top=s.Bounds.Top,Right=s.Bounds.Right,Bottom=s.Bounds.Bottom}).ToList(),cur,dir);if(t<0||tiles.Any(p=>p.Value.screen==screens[t].DeviceName))return;
  var w=screens[t].WorkingArea;int gap=Math.Max(4,(int)Math.Round(8*TaskbarNative.Scale(screens[t])));motions.Remove(a);
  Apply(new(){(a,new Native.Rect{Left=w.Left+gap,Top=w.Top+gap,Right=w.Right-gap,Bottom=w.Bottom-gap},Shadow(a))});RequestTick();}
 void SwapPair(IntPtr a,IntPtr b){if(!tiles.TryGetValue(a,out var ta)||!tiles.TryGetValue(b,out var tb)||!order.Swap(a,b))return;
  if(ta.screen!=tb.screen){motions.Remove(a);motions.Remove(b);Apply(new(){(a,tb.slot,Shadow(a)),(b,ta.slot,Shadow(b))});}
  RequestTick();}
 void Resize(double delta){var a=Active();if(a==IntPtr.Zero)return;var t=tiles[a];var split=TileLogic.DeepestSplit(t.count,t.index);if(split==null)return;
  if(!ratios.TryGetValue(t.screen,out var d))ratios[t.screen]=d=new();d[split.Value.path]=TileLogic.Grow(Ratio(t.screen,split.Value.path),split.Value.inFirst,delta);RequestTick();}
 // Floating: the window leaves the tiling (restored to its former place) until toggled back or tiling is turned off.
 void ToggleFloat(){var fg=GetForegroundWindow();if(fg==IntPtr.Zero)return;if(floating.Remove(fg)){RequestTick();return;}if(!managed.ContainsKey(fg))return;Release(fg);floating.Add(fg);RequestTick();}
 void Release(IntPtr h){if(managed.TryGetValue(h,out var item)){if(Native.IsWindow(h)&&!TaskbarNative.IsIconic(h)&&!IsZoomed(h)&&item.Placed&&Same(Visible(h),item.Last)){var placement=item.Original;SetWindowPlacement(h,ref placement);}managed.Remove(h);}motions.Remove(h);failing.Remove(h);tiles.Remove(h);}
 // End of a manual move (EVENT_SYSTEM_MOVESIZEEND, already hooked): dropped on another tile = swap, elsewhere = back to place. Manual resizes are left to the existing behaviour.
 void Dropped(IntPtr h){if(!enabled||disposed||!tiles.TryGetValue(h,out var t))return;var cur=Visible(h);
  if(Math.Abs(cur.Right-cur.Left-(t.slot.Right-t.slot.Left))>6||Math.Abs(cur.Bottom-cur.Top-(t.slot.Bottom-t.slot.Top))>6||!GetCursorPos(out var pt))return;
  var others=tiles.Where(p=>p.Key!=h).ToList();int k=TileLogic.TileAt(others.Select(p=>p.Value.slot).ToList(),pt.X,pt.Y);
  if(k>=0)SwapPair(h,others[k].Key);else RequestTick();}
 // ---- User exclusions: process names, one per line, in the Panneaux data folder.
 string ExclusionPath=>Path.Combine(Store.DataDir,"tile-exclusions.txt");
 void LoadExclusions(){if(excluded!=null)return;try{excluded=new(TileLogic.ParseExclusions(File.Exists(ExclusionPath)?File.ReadAllText(ExclusionPath):""));}catch(Exception ex){excluded=new();Store.Log("Tuilage · exclusions illisibles : "+ex.Message);}}
 void SaveExclusions(){try{Directory.CreateDirectory(Store.DataDir);Winlestia.Shared.AtomicFile.WriteAllText(ExclusionPath,string.Join("\n",excluded!.OrderBy(s=>s,StringComparer.Ordinal)));}catch(Exception ex){Store.Log("Tuilage · exclusions non enregistrées : "+ex.Message);}}
 internal IReadOnlyList<string> Exclusions{get{LoadExclusions();return excluded!.OrderBy(s=>s,StringComparer.Ordinal).ToList();}}
 internal void RemoveExclusion(string name){LoadExclusions();if(excluded!.Remove(TileLogic.Normalize(name))){SaveExclusions();RequestTick();}}
 string ProcessNameOf(uint pid){if(processNames.TryGetValue(pid,out var cached))return cached;string name="";try{using var p=Process.GetProcessById((int)pid);name=TileLogic.Normalize(p.ProcessName);}catch{}processNames[pid]=name;return name;}
 bool Excluded(uint pid){LoadExclusions();return excluded!.Count>0&&excluded.Contains(ProcessNameOf(pid));}
 void ToggleExcludeActive(){var fg=GetForegroundWindow();if(fg==IntPtr.Zero)return;GetWindowThreadProcessId(fg,out uint pid);if(pid==ExcludedProcess||pid==(uint)Environment.ProcessId)return;
  var name=ProcessNameOf(pid);if(name.Length==0)return;LoadExclusions();
  if(excluded!.Remove(name))Store.Log("Tuilage · "+name+" réintégrée");else{excluded.Add(name);Store.Log("Tuilage · "+name+" exclue");foreach(var h in managed.Keys.ToArray()){GetWindowThreadProcessId(h,out uint p);if(ProcessNameOf(p)==name)Release(h);}}
  SaveExclusions();RequestTick();}
}
