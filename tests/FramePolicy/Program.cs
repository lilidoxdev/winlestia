// C#12 / FramePolicy: which window carries the accent border, per the AccentBorders and ActiveBorderOnly preferences (pure functions).
using System;using System.Collections.Generic;using PanneauxGlass;
class Test{
static int Main(){var r=new Dictionary<string,bool>();IntPtr a=(IntPtr)10,b=(IntPtr)20,z=IntPtr.Zero;
r["allWindowsKeepsEveryWindow"]=FramePolicy.Carries(true,false,a,b)&&FramePolicy.Carries(true,false,b,b);
r["activeOnlyColoursActiveOnly"]=FramePolicy.Carries(true,true,b,b)&&!FramePolicy.Carries(true,true,a,b);
r["bordersOffNeverColours"]=!FramePolicy.Carries(false,false,a,a)&&!FramePolicy.Carries(false,true,a,a);
var t=FramePolicy.Foreground(true,true,true,b,a);
r["switchColoursNewRestoresOld"]=t.colour==b&&t.restore==a&&t.tracked==b;
t=FramePolicy.Foreground(true,true,true,a,z);
r["firstWindowNothingToRestore"]=t.colour==a&&t.restore==z&&t.tracked==a;
t=FramePolicy.Foreground(true,true,true,a,a);
r["sameWindowNoop"]=t.colour==z&&t.restore==z&&t.tracked==a;
t=FramePolicy.Foreground(true,true,false,b,a);
r["ineligibleKeepsLast"]=t.colour==z&&t.restore==z&&t.tracked==a;
t=FramePolicy.Foreground(true,true,true,z,a);
r["zeroForegroundKeepsLast"]=t.colour==z&&t.restore==z&&t.tracked==a;
t=FramePolicy.Foreground(true,false,true,b,a);
r["allWindowsModeIgnoresEvent"]=t.colour==z&&t.restore==z&&t.tracked==z;
t=FramePolicy.Foreground(false,true,true,b,a);
r["bordersOffIgnoresActiveOnly"]=t.colour==z&&t.restore==z&&t.tracked==z;
int ok=0;foreach(var v in r.Values)if(v)ok++;foreach(var k in r)if(!k.Value)Console.WriteLine("FAIL "+k.Key);Console.WriteLine("TOTAL "+ok+"/"+r.Count);return ok==r.Count?0:1;}
}
