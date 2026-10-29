// C#12 / NetworkKindWatcher probe: prints the classified kind on this machine, then checks Start/Stop (event subscription) is clean.
using System;using System.Net.NetworkInformation;using System.Threading;using PanneauxGlass;
class Probe{
static int Main(){
Console.WriteLine("Classify() = "+NetworkKindWatcher.Classify());
foreach(var n in NetworkInterface.GetAllNetworkInterfaces())if(n.OperationalStatus==OperationalStatus.Up)Console.WriteLine("  up: "+n.NetworkInterfaceType+" | "+n.Name+" | "+n.Description+" | gateways="+n.GetIPProperties().GatewayAddresses.Count+" idx="+(n.Supports(NetworkInterfaceComponent.IPv4)?n.GetIPProperties().GetIPv4Properties().Index:-1));
NetworkKindWatcher.Start();NetworkKindWatcher.Start();Thread.Sleep(1500);Console.WriteLine("Current after Start = "+NetworkKindWatcher.Current);
NetworkKindWatcher.Stop();NetworkKindWatcher.Stop();NetworkKindWatcher.Start();Thread.Sleep(500);NetworkKindWatcher.Stop();Console.WriteLine("start/stop cycles ok");
int fired=0;Action h=()=>fired++;NetworkKindWatcher.Changed+=h;string cur=NetworkKindWatcher.Current,other=cur=="ethernet"?"wifi":"ethernet";NetworkKindWatcher.RaiseChangedForTest(other);NetworkKindWatcher.RaiseChangedForTest(other);NetworkKindWatcher.RaiseChangedForTest(cur);NetworkKindWatcher.Changed-=h;NetworkKindWatcher.RaiseChangedForTest(other);Console.WriteLine("Changed fired "+fired+" (expected 2)");if(fired!=2)return 2;
return NetworkKindWatcher.Current is "ethernet" or "wifi" or "none"?0:1;}
}
