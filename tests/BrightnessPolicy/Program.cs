// C#12 / BrightnessPolicy: DDC/CI range conversion, write rate limit, read failure budget, display id (pure functions).
using System;using System.Collections.Generic;using PanneauxGlass;
class Test{
static int Main(){var r=new Dictionary<string,bool>();
r["rawFullDefaultRange"]=BrightnessPolicy.ToRaw(0,0,100)==0&&BrightnessPolicy.ToRaw(50,0,100)==50&&BrightnessPolicy.ToRaw(100,0,100)==100;
r["rawCustomRange"]=BrightnessPolicy.ToRaw(0,10,90)==10&&BrightnessPolicy.ToRaw(50,10,90)==50&&BrightnessPolicy.ToRaw(100,10,90)==90;
r["rawLargeRange"]=BrightnessPolicy.ToRaw(25,0,255)==64;
r["rawClamps"]=BrightnessPolicy.ToRaw(-20,0,100)==0&&BrightnessPolicy.ToRaw(250,0,100)==100&&BrightnessPolicy.ToRaw(double.NaN,5,95)==5;
r["rawDegenerateRange"]=BrightnessPolicy.ToRaw(50,40,40)==40&&BrightnessPolicy.ToRaw(50,60,40)==60;
r["percentCustomRange"]=BrightnessPolicy.ToPercent(10,10,90)==0&&BrightnessPolicy.ToPercent(50,10,90)==50&&BrightnessPolicy.ToPercent(90,10,90)==100;
r["percentClampsOutOfRange"]=BrightnessPolicy.ToPercent(5,10,90)==0&&BrightnessPolicy.ToPercent(200,10,90)==100;
r["percentDegenerateRange"]=BrightnessPolicy.ToPercent(5,7,7)==0;
bool round=true;for(int p=0;p<=100;p++)round&=Math.Round(BrightnessPolicy.ToPercent(BrightnessPolicy.ToRaw(p,0,100),0,100))==p;
r["roundTripDefaultRange"]=round;
r["delayRightAfterWrite"]=BrightnessPolicy.RemainingDelay(1000,1000)==BrightnessPolicy.WriteIntervalMs;
r["delayPartial"]=BrightnessPolicy.RemainingDelay(1000,1050)==BrightnessPolicy.WriteIntervalMs-50;
r["delayElapsed"]=BrightnessPolicy.RemainingDelay(1000,1120)==0&&BrightnessPolicy.RemainingDelay(1000,5000)==0;
r["delayClockBackwardsBounded"]=BrightnessPolicy.RemainingDelay(5000,1000)==BrightnessPolicy.WriteIntervalMs;
r["delayFirstWrite"]=BrightnessPolicy.RemainingDelay(-1_000_000,123456)==0;
r["unavailableAfterThree"]=!BrightnessPolicy.Unavailable(0)&&!BrightnessPolicy.Unavailable(2)&&BrightnessPolicy.Unavailable(3)&&BrightnessPolicy.Unavailable(9);
r["readIdle"]=BrightnessPolicy.CanRead(false,false,false);
r["noReadDuringWrite"]=!BrightnessPolicy.CanRead(true,false,false)&&!BrightnessPolicy.CanRead(false,true,false);
r["noReadWhenUnavailable"]=!BrightnessPolicy.CanRead(false,false,true);
r["idFromDevice"]=BrightnessPolicy.IdFromDevice(@"\.\DISPLAY2")==2&&BrightnessPolicy.IdFromDevice(@"\.\DISPLAY12")==12;
r["idFromBadDevice"]=BrightnessPolicy.IdFromDevice("")==0&&BrightnessPolicy.IdFromDevice(null)==0&&BrightnessPolicy.IdFromDevice(@"\.\DISPLAY")==0&&BrightnessPolicy.IdFromDevice("X12345")==0;
r["matchesCoarseRange"]=BrightnessPolicy.Matches(37,4,0,10)&&!BrightnessPolicy.Matches(37,5,0,10)&&BrightnessPolicy.ToPercent(4,0,10)==40;
int ok=0;foreach(var v in r.Values)if(v)ok++;foreach(var k in r)if(!k.Value)Console.WriteLine("FAIL "+k.Key);Console.WriteLine("TOTAL "+ok+"/"+r.Count);return ok==r.Count?0:1;}
}
