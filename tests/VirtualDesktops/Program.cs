// C#12 / VirtualDesktops probe: reads the registry (read-only), checks the computed key sequences and Start/Stop. Never sends any key.
using System;using System.Linq;using System.Threading;using PanneauxGlass;
class Probe{
static int fails=0;
static void Check(bool ok,string what){Console.WriteLine((ok?"ok   ":"FAIL ")+what);if(!ok)fails++;}
static string Seq(int c,int t,int n)=>string.Join(" ",VirtualDesktops.PlanKeys(c,t,n).Select(k=>k.Vk.ToString("X2")+(k.Up?"^":"v")));
static int Main(){
var s=VirtualDesktops.Read();Console.WriteLine("registry: count="+s.Count+" current="+s.Current+" names=["+string.Join("|",s.Names)+"]");
Check(s.Count>=1&&s.Current>=0&&s.Current<s.Count,"registry read gives a valid count/current");
Check(Seq(0,2,3)=="5Bv 11v 27v 27^ 27v 27^ 11^ 5B^","0->2 of 3: Win+Ctrl, Right x2, release: "+Seq(0,2,3));
Check(Seq(2,1,3)=="5Bv 11v 25v 25^ 11^ 5B^","2->1 of 3: Left x1: "+Seq(2,1,3));
Check(Seq(1,1,3)=="","same desktop: nothing");
Check(Seq(0,5,1)=="","single desktop: nothing");
Check(Seq(0,9,3)==Seq(0,2,3),"target clamped to last desktop");
Check(Seq(5,0,3)=="","invalid current: nothing");
var l=VirtualDesktops.PlanKeys(0,3,4);Check(l.Count>0&&l[^1]==new VirtualDesktops.Key(0x5B,true)&&l[^2]==new VirtualDesktops.Key(0x11,true),"modifiers released last");
Check(VirtualDesktops.ParseIds(new byte[33]).Count==2,"ParseIds ignores trailing partial id");
Check(VirtualDesktops.ParseIds(null).Count==0,"ParseIds null safe");
int fired=0;VirtualDesktops.Changed+=()=>fired++;
VirtualDesktops.Start();VirtualDesktops.Start();Thread.Sleep(1000);
Check(VirtualDesktops.Current.Count==s.Count,"watcher snapshot equals direct read");
VirtualDesktops.Stop();VirtualDesktops.Stop();VirtualDesktops.Start();Thread.Sleep(200);VirtualDesktops.Stop();Check(true,"start/stop cycles clean");
return fails;}
}
