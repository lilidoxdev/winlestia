// C#12 / FilesPreloadPolicy: pure restart decisions of the resident Winlestia Fichiers (no process, no backend).
using System;using System.Collections.Generic;using PanneauxGlass;
class Test{
static int Main(){var r=new Dictionary<string,bool>();const int M=FilesPreloadPolicy.MinRecycleIntervalMs;
FilesPreloadPolicy P()=>new FilesPreloadPolicy();
r["recycleExitRelaunches"]=P().OnExit(75,1000,false,true)==PreloadAction.Recycle;
{var p=P();p.OnExit(75,0,false,true);r["secondRecycleUnder60sStops"]=p.OnExit(75,M-1,false,true)==PreloadAction.StopTooFast;}
{var p=P();p.OnExit(75,0,false,true);r["secondRecycleAt60sRelaunches"]=p.OnExit(75,M,false,true)==PreloadAction.Recycle;}
{var p=P();bool g=true;long t=0;for(int i=0;i<50;i++){g&=p.OnExit(75,t,false,true)==PreloadAction.Recycle;t+=M+1;}r["recyclesAreUnlimitedWhenSpaced"]=g;}
{var p=P();p.OnExit(75,0,false,true);p.OnExit(75,10,false,true);r["afterStopTheClockStillAdvances"]=p.OnExit(75,10+M,false,true)==PreloadAction.Recycle;}
r["quittingNeverRelaunches"]=P().OnExit(75,0,true,true)==PreloadAction.None&&P().OnExit(1,0,true,true)==PreloadAction.None;
r["prefOffNeverRelaunches"]=P().OnExit(75,0,false,false)==PreloadAction.None&&P().OnExit(1,0,false,false)==PreloadAction.None;
r["crashRestartsOnce"]=P().OnExit(1,0,false,true)==PreloadAction.Crash;
{var p=P();p.Starts=2;r["crashLimitReached"]=p.OnExit(1,0,false,true)==PreloadAction.None&&p.OnExit(0,0,false,true)==PreloadAction.None;}
{var p=P();p.Starts=2;r["recycleNotCountedAgainstCrashLimit"]=p.OnExit(75,0,false,true)==PreloadAction.Recycle&&p.MayLaunch(true,true,false)&&!p.MayLaunch(false,true,false);}
{var p=P();p.Starts=1;p.OnExit(75,0,false,true);r["recycleLeavesStartsAlone"]=p.Starts==1&&p.OnExit(1,5,false,true)==PreloadAction.Crash;}
r["launchGateRespectsPrefAndQuit"]=!P().MayLaunch(true,false,false)&&!P().MayLaunch(true,true,true)&&!P().MayLaunch(false,false,false)&&P().MayLaunch(false,true,false);
int ok=0;foreach(var v in r.Values)if(v)ok++;foreach(var k in r)Console.WriteLine(k.Key+"="+(k.Value?"true":"false"));Console.WriteLine("TOTAL "+ok+"/"+r.Count);return ok==r.Count?0:1;}
}
