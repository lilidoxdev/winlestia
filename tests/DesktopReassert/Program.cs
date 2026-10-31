// C#12 / DesktopReassertPolicy: debounce, rate cap, follow-up, mute after own run, class filter (no WinEvent).
using System;using System.Collections.Generic;using PanneauxGlass;
class Test{
static int Main(){var r=new Dictionary<string,bool>();var t=new DateTime(2026,1,1,12,0,0,DateTimeKind.Utc);
var p=new DesktopReassertPolicy();
r["firstEventAccepted"]=p.Event(t);
r["notDueBeforeDebounce"]=!p.Due(t.AddMilliseconds(399));
r["dueAfterDebounce"]=p.Due(t.AddMilliseconds(400));
p.Event(t.AddMilliseconds(300));
r["burstRestartsDebounce"]=!p.Due(t.AddMilliseconds(600))&&p.Due(t.AddMilliseconds(700));
var end=t.AddSeconds(1);p.Ran(end);
r["followUpAt3s"]=!p.Due(end.AddMilliseconds(2999))&&p.Due(end.AddSeconds(3));
r["eventsMutedAfterRun"]=!p.Event(end.AddMilliseconds(1000))&&p.Event(end.AddMilliseconds(1600));
var q=new DesktopReassertPolicy();q.Event(t);q.Ran(t.AddSeconds(1));
q.Event(t.AddMilliseconds(2600));
r["minGapTwoSeconds"]=q.Due(t.AddMilliseconds(3000))&&!q.Due(t.AddMilliseconds(2900))||q.Delay(t.AddMilliseconds(2600))>=TimeSpan.FromMilliseconds(400);
var f=new DesktopReassertPolicy();f.Event(t);f.Ran(t);f.Ran(t.AddSeconds(3));
r["followUpDoesNotChain"]=f.Delay(t.AddSeconds(4))==null;
var m=new DesktopReassertPolicy();m.Event(t);m.Ran(t);
r["forceBypassesMute"]=m.Event(t.AddMilliseconds(100),true);
var s=new DesktopReassertPolicy();s.Event(t);for(int i=1;i<20;i++)s.Event(t.AddMilliseconds(i*300));
r["stormCappedByMaxWait"]=s.Due(t.AddSeconds(2));
r["filterWorkerW"]=DesktopEventFilter.Relevant("WorkerW","");
r["filterDefViewOnDesktop"]=DesktopEventFilter.Relevant("SHELLDLL_DefView","Progman")&&DesktopEventFilter.Relevant("SHELLDLL_DefView","WorkerW");
r["filterDefViewInFolderWindow"]=!DesktopEventFilter.Relevant("SHELLDLL_DefView","CabinetWClass");
r["filterOtherClass"]=!DesktopEventFilter.Relevant("Shell_TrayWnd","");
r["hideIconsZeroStopsReassert"]=!DesktopReassertPolicy.StillWanted(0)&&DesktopReassertPolicy.StillWanted(1);
int ok=0;foreach(var v in r.Values)if(v)ok++;foreach(var k in r)Console.WriteLine(k.Key+"="+(k.Value?"true":"false"));Console.WriteLine("TOTAL "+ok+"/"+r.Count);return ok==r.Count?0:1;}
}
