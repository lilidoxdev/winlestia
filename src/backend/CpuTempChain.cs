// language: C# 12, file: CpuTempChain.cs, target: Windows 11 / .NET 8, read-only CPU temperature fallbacks (never installs a driver)
using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PanneauxGlass;

// Fallbacks used when LibreHardwareMonitor has no plausible CPU reading (no sensor driver, no admin rights).
// Slow sources (WMI/PDH) run on a separate task at most every 10 s, so the 1 s sampling loop never blocks.
internal sealed class CpuTempChain:IDisposable {
    sealed class Source(string name,Func<double?> read){internal readonly string Name=name;internal readonly Func<double?> Read=read;internal long RetryAt;internal bool Logged;}
    readonly Source[] sources;readonly Func<long> clock;readonly Action<string> log;
    sealed record Reading(double Value,string Source,long At);
    Reading? cached;int busy,disposed,generation,probing=-1;long nextProbe,busySince;int working=-1; // cached: immutable, published via Volatile; probing: index of the source being read
    internal CpuTempChain(Func<long>? clock=null,Action<string>? log=null){this.clock=clock??(()=>Environment.TickCount64);this.log=log??Store.Log;
        sources=new[]{new Source("LHM-WMI",()=>WmiSensor(@"root\LibreHardwareMonitor")),new Source("OHM-WMI",()=>WmiSensor(@"root\OpenHardwareMonitor")),new Source("ACPI",ReadAcpi),new Source("PDH",ReadPdh)};}
    internal static double? Plausible(double? v)=>v is >10 and <=125&&double.IsFinite(v.Value)?v:null;
    // ACPI/PDH stubs report a constant 3000 dK (26.85 C) or 3010 dK (27.85 C) on machines without a real thermal zone.
    internal static double? Real(double? v)=>Plausible(v) is {} t&&Math.Abs(t-26.85)>0.06&&Math.Abs(t-27.85)>0.06?t:null;
    internal (double? value,string? source) Resolve(double? lhm){var now=clock();if(Plausible(lhm) is {} direct)return(direct,"LHM");
        if(now>=nextProbe&&Volatile.Read(ref disposed)==0){
            // A probe stuck for >30 s (hung WMI call): mark its source failed (back-off) and let a new probe run; the stuck task's late result is ignored via the generation.
            if(Volatile.Read(ref busy)==1&&now-Volatile.Read(ref busySince)>30000){int gen=Interlocked.Increment(ref generation);int p=Volatile.Read(ref probing);if(p>=0&&p<sources.Length){sources[p].RetryAt=now+60000;if(working==p)working=-1;log("Température CPU "+sources[p].Name+": sonde bloquée");}Volatile.Write(ref busy,0);}
            if(Interlocked.CompareExchange(ref busy,1,0)==0){nextProbe=now+10000;Volatile.Write(ref busySince,now);int gen=Volatile.Read(ref generation);Task.Run(()=>{try{Probe(gen);}catch(Exception ex){log("Température CPU: "+ex.Message);}finally{if(gen==Volatile.Read(ref generation))Volatile.Write(ref busy,0);}});}}
        var c=Volatile.Read(ref cached);return c is {} v&&now-v.At<30000?(v.Value,v.Source):(null,null);}
    void Publish(int gen,Reading? r){if(gen==Volatile.Read(ref generation))Volatile.Write(ref cached,r);}
    void Probe(int gen){var now=clock();if(working>=0){var s=sources[working];var v=Safe(s,now,working);if(v.HasValue){Publish(gen,new(v.Value,s.Name,now));return;}working=-1;}
        for(int i=0;i<sources.Length;i++){var s=sources[i];if(now<s.RetryAt)continue;var v=Safe(s,now,i);if(v.HasValue){working=i;Publish(gen,new(v.Value,s.Name,now));return;}}
        Publish(gen,null);}
    double? Safe(Source s,long now,int index){Volatile.Write(ref probing,index);try{var v=s.Read();if(v.HasValue){s.Logged=false;return v;}throw new InvalidOperationException("aucune valeur plausible");}
        catch(Exception ex){s.RetryAt=now+60000;if(!s.Logged){s.Logged=true;log("Température CPU "+s.Name+": "+ex.Message);}return null;}}
    public void Dispose(){Volatile.Write(ref disposed,1);}

    internal static double? WmiSensor(string ns){
        var scope=new ManagementScope(ns,new ConnectionOptions{Timeout=TimeSpan.FromSeconds(2)});scope.Connect();
        using var searcher=new ManagementObjectSearcher(scope,new ObjectQuery("SELECT Name,Value,Identifier FROM Sensor WHERE SensorType='Temperature'"),new EnumerationOptions{Timeout=TimeSpan.FromSeconds(2),ReturnImmediately=true});
        double? best=null;int rank=9;
        using var results=searcher.Get();
        foreach(ManagementBaseObject o in results){using(o){string id=o["Identifier"] as string??"",name=o["Name"] as string??"";if(id.IndexOf("cpu",StringComparison.OrdinalIgnoreCase)<0)continue;
            double? v=o["Value"] is {} raw?Plausible(Convert.ToDouble(raw)):null;if(v==null)continue;
            int r=name.Contains("Tctl",StringComparison.OrdinalIgnoreCase)||name.Contains("Tdie",StringComparison.OrdinalIgnoreCase)?0:name.Contains("Package",StringComparison.OrdinalIgnoreCase)?1:name.Contains("Average",StringComparison.OrdinalIgnoreCase)?2:3;
            if(r<rank||r==rank&&v>best){rank=r;best=v;}}}
        return best;}
    internal static double? ReadAcpi(){
        var scope=new ManagementScope(@"root\wmi",new ConnectionOptions{Timeout=TimeSpan.FromSeconds(2)});scope.Connect();
        using var searcher=new ManagementObjectSearcher(scope,new ObjectQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"),new EnumerationOptions{Timeout=TimeSpan.FromSeconds(2),ReturnImmediately=true});
        double? best=null;using var results=searcher.Get();
        foreach(ManagementBaseObject o in results){using(o){if(o["CurrentTemperature"] is {} raw&&Real(Convert.ToDouble(raw)/10-273.15) is {} t&&(best==null||t>best))best=t;}}
        return best;}
    internal static double? ReadPdh(){
        double? best=null;
        foreach(var(counter,divisor) in new[]{(@"\Thermal Zone Information(*)\High Precision Temperature",10.0),(@"\Thermal Zone Information(*)\Temperature",1.0)}){
            foreach(var raw in PdhArray(counter))if(Real(raw/divisor-273.15) is {} t&&(best==null||t>best))best=t;
            if(best.HasValue)break;}
        return best;}
    static List<double> PdhArray(string path){var list=new List<double>();IntPtr query=IntPtr.Zero;
        try{if(PdhOpenQueryW(null,UIntPtr.Zero,out query)!=0)return list;if(PdhAddEnglishCounterW(query,path,UIntPtr.Zero,out var counter)!=0)return list;
            if(PdhCollectQueryData(query)!=0)return list;Thread.Sleep(50);if(PdhCollectQueryData(query)!=0)return list;
            uint size=0,count=0;uint rc=PdhGetFormattedCounterArrayW(counter,0x200,ref size,out count,IntPtr.Zero);if(rc!=0x800007D2||size==0)return list;
            IntPtr buffer=Marshal.AllocHGlobal((int)size);
            try{rc=PdhGetFormattedCounterArrayW(counter,0x200,ref size,out count,buffer);if(rc!=0)return list;int stride=Marshal.SizeOf<PdhItem>();
                for(int i=0;i<count;i++){var item=Marshal.PtrToStructure<PdhItem>(buffer+i*stride);if(item.Status<=1&&double.IsFinite(item.Value))list.Add(item.Value);}}
            finally{Marshal.FreeHGlobal(buffer);}
            return list;}
        finally{if(query!=IntPtr.Zero)PdhCloseQuery(query);}}
    [StructLayout(LayoutKind.Sequential)]struct PdhItem {internal IntPtr Name;internal uint Status;internal uint Pad;internal double Value;}
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]static extern uint PdhOpenQueryW(string? source,UIntPtr data,out IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]static extern uint PdhAddEnglishCounterW(IntPtr query,string path,UIntPtr data,out IntPtr counter);
    [DllImport("pdh.dll")]static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")]static extern uint PdhGetFormattedCounterArrayW(IntPtr counter,uint format,ref uint size,out uint count,IntPtr buffer);
    [DllImport("pdh.dll")]static extern uint PdhCloseQuery(IntPtr query);
}
