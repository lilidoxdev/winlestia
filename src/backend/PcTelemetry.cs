// language: C# 12, file: PcTelemetry.cs, target: Windows 11 / .NET 8, local read-only hardware sampling
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace PanneauxGlass;
internal sealed record GpuReading(string Id,string Name,double? Load,double? Temperature,double? MemoryUsedMb,double? MemoryTotalMb);
internal sealed record DriveReading(string Name,long Total,long Free);
internal sealed record HeatReading(string Name,double? Temperature);
internal sealed record PcReading(DateTimeOffset At,double? Cpu,double? CpuTemperature,string CpuName,ulong MemoryTotal,ulong MemoryAvailable,GpuReading[] Gpus,DriveReading[] Drives,HeatReading[] StorageHeat,HeatReading[] MemoryHeat,double? DiskBusy,double? DiskRead,double? DiskWrite,double? Download,double? Upload,string Network,string SensorNote,double SampleMilliseconds,string? CpuTempSource=null);

// A single sequential worker owns every native/library handle. No timers or sensor reads survive StopAsync.
internal sealed class PcTelemetry:IDisposable {
    CancellationTokenSource? cancellation;Task? worker;internal long Samples=>Interlocked.Read(ref samples);long samples;
    internal bool Running=>worker is {IsCompleted:false};
    internal void Start(Action<PcReading> publish,int interval=1000,Action<string>? error=null,bool quick=false){if(Running)return;cancellation?.Dispose();cancellation=new();var token=cancellation.Token;worker=Task.Run(async()=>{try{using var source=new PcTelemetrySource(quick);while(!token.IsCancellationRequested){long start=Environment.TickCount64;var reading=source.Read();if(token.IsCancellationRequested)break;Interlocked.Increment(ref samples);publish(reading);await Task.Delay((int)Math.Max(1,Math.Max(1000,interval)-(Environment.TickCount64-start)),token).ConfigureAwait(false);}}catch(OperationCanceledException){}catch(Exception ex){Store.Log("État PC: "+ex);if(!token.IsCancellationRequested)error?.Invoke(ex.Message);}},token);}
    internal async Task StopAsync(){cancellation?.Cancel();var pending=worker;try{if(pending!=null)await pending.ConfigureAwait(false);}catch(OperationCanceledException){}finally{if(ReferenceEquals(worker,pending))worker=null;}}
    public void Dispose(){cancellation?.Cancel();}
}

internal sealed class PcTelemetrySource:IDisposable {
    readonly Computer computer=new(){IsCpuEnabled=true,IsStorageEnabled=true,IsMemoryEnabled=true};readonly NvidiaTelemetry nvidia=new();
    readonly DiskCounters disk=new();readonly string cpuName;string sensorNote="";bool hardwareOpen;
    IHardware[] cpus=Array.Empty<IHardware>(),gpus=Array.Empty<IHardware>(),storage=Array.Empty<IHardware>();
    HeatReading[] heat=Array.Empty<HeatReading>(),memoryHeat=Array.Empty<HeatReading>();DriveReading[] drives=Array.Empty<DriveReading>();double? cpuTemperature;string? cpuTempSource;readonly CpuTempChain tempChain=new();
    readonly Dictionary<string,(long received,long sent)> previousNetwork=new();NetworkInterface[] adapters=Array.Empty<NetworkInterface>();
    ulong previousIdle,previousKernel,previousUser;long lastNetwork;long nextSlow,nextCpu,nextAdapters;
    internal PcTelemetrySource(bool quick=false){computer.IsMemoryEnabled=!quick;computer.IsStorageEnabled=!quick;using(var key=Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))cpuName=(key?.GetValue("ProcessorNameString") as string??"Processeur").Trim();
        try{computer.IsGpuEnabled=!nvidia.Available;computer.Open();hardwareOpen=true;cpus=computer.Hardware.Where(h=>h.HardwareType==HardwareType.Cpu).ToArray();gpus=computer.Hardware.Where(h=>h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel).ToArray();storage=computer.Hardware.Where(h=>h.HardwareType==HardwareType.Storage).ToArray();}catch(Exception ex){sensorNote="Certaines sondes sont inaccessibles";Store.Log("Sondes PC: "+ex.Message);}ReadCpu();ReadNetwork(Environment.TickCount64);}
    internal PcReading Read(){var watch=Stopwatch.StartNew();long now=Environment.TickCount64;double? cpu=ReadCpu();var memory=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};bool memoryOk=GlobalMemoryStatusEx(ref memory);
        if(now>=nextCpu){nextCpu=now+3000;foreach(var hardware in cpus)TryUpdate(hardware);cpuTemperature=cpus.SelectMany(Flatten).SelectMany(h=>h.Sensors).Where(s=>s.SensorType==SensorType.Temperature).OrderBy(s=>s.Name.Contains("Tctl",StringComparison.OrdinalIgnoreCase)?0:s.Name.Contains("Package",StringComparison.OrdinalIgnoreCase)?1:s.Name.Contains("Average",StringComparison.OrdinalIgnoreCase)?2:3).Select(s=>CpuTempChain.Plausible(Temperature(s.Value))).FirstOrDefault(v=>v.HasValue);(cpuTemperature,cpuTempSource)=tempChain.Resolve(cpuTemperature);var modules=computer.Hardware.Where(h=>h.HardwareType==HardwareType.Memory).ToArray();foreach(var module in modules)TryUpdate(module);memoryHeat=modules.SelectMany(Flatten).SelectMany(h=>h.Sensors.Where(s=>s.SensorType==SensorType.Temperature&&s.Index==0).Select(s=>new HeatReading(h.Name,Temperature(s.Value)))).ToArray();}
        var gpuReadings=new List<GpuReading>(nvidia.Read());foreach(var gpu in gpus){TryUpdate(gpu);var sensors=Flatten(gpu).SelectMany(h=>h.Sensors).ToArray();double? Sensor(SensorType type,params string[] names)=>sensors.Where(s=>s.SensorType==type&&names.Any(n=>s.Name.Equals(n,StringComparison.OrdinalIgnoreCase))).Select(s=>Finite(s.Value)).FirstOrDefault(v=>v.HasValue);
            gpuReadings.Add(new(gpu.Identifier.ToString(),gpu.Name,Percent(Sensor(SensorType.Load,"GPU Core","D3D 3D")),Temperature((float?)Sensor(SensorType.Temperature,"GPU Core")),Sensor(SensorType.SmallData,"GPU Memory Used","D3D Dedicated Memory Used"),Sensor(SensorType.SmallData,"GPU Memory Total")));}
        if(now>=nextSlow){nextSlow=now+30000;drives=DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed).Select(ReadDrive).Where(d=>d!=null).Cast<DriveReading>().ToArray();foreach(var device in storage)TryUpdate(device);heat=storage.Select(h=>new HeatReading(h.Name,Flatten(h).SelectMany(x=>x.Sensors).Where(s=>s.SensorType==SensorType.Temperature).OrderBy(s=>s.Name.Contains("Composite",StringComparison.OrdinalIgnoreCase)?0:1).Select(s=>Temperature(s.Value)).FirstOrDefault(v=>v.HasValue))).ToArray();}
        var io=disk.Read();var network=ReadNetwork(now);string note=cpuTemperature.HasValue?sensorNote:LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled?"Sonde CPU inaccessible avec les droits actuels":"Température CPU : pilote de capteurs requis";
        watch.Stop();return new(DateTimeOffset.Now,cpu,cpuTemperature,cpuName,memoryOk?memory.TotalPhysical:0,memoryOk?memory.AvailablePhysical:0,gpuReadings.ToArray(),drives,heat,memoryHeat,io.busy,io.read,io.write,network.down,network.up,network.name,note,watch.Elapsed.TotalMilliseconds,cpuTempSource);
    }
    static IEnumerable<IHardware> Flatten(IHardware h){yield return h;foreach(var child in h.SubHardware)foreach(var nested in Flatten(child))yield return nested;}
    static void TryUpdate(IHardware h){try{h.Update();foreach(var child in h.SubHardware)TryUpdate(child);}catch(Exception ex){Store.Log("Sonde "+h.Name+": "+ex.Message);}}
    static DriveReading? ReadDrive(DriveInfo d){try{return d.IsReady?new(d.Name,d.TotalSize,d.AvailableFreeSpace):null;}catch{return null;}}
    static double? Finite(float? value)=>value.HasValue&&float.IsFinite(value.Value)?value.Value:null;
    internal static double? Percent(double? value)=>value.HasValue&&double.IsFinite(value.Value)?Math.Clamp(value.Value,0,100):null;
    static double? Temperature(float? value)=>value is >-20 and <150&&float.IsFinite(value.Value)?value.Value:null;
    double? ReadCpu(){if(!GetSystemTimes(out var idle,out var kernel,out var user))return null;ulong total=kernel+user,previous=previousKernel+previousUser;double? result=previous!=0&&total>previous?Percent(100.0*(1-(double)(idle-previousIdle)/(total-previous))):null;previousIdle=idle;previousKernel=kernel;previousUser=user;return result;}
    (double? down,double? up,string name) ReadNetwork(long now){if(now>=nextAdapters){nextAdapters=now+10000;adapters=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211&&!IsVirtual(n)).ToArray();var current=adapters.Select(n=>n.Id).ToHashSet();foreach(var key in previousNetwork.Keys.Where(k=>!current.Contains(k)).ToArray())previousNetwork.Remove(key);}
        double down=0,up=0;int valid=0;double seconds=(now-lastNetwork)/1000.0;foreach(var adapter in adapters){try{var data=adapter.GetIPStatistics();if(previousNetwork.TryGetValue(adapter.Id,out var old)&&seconds>0){down+=Rate(data.BytesReceived,old.received,seconds);up+=Rate(data.BytesSent,old.sent,seconds);valid++;}previousNetwork[adapter.Id]=(data.BytesReceived,data.BytesSent);}catch(NetworkInformationException){}}
        lastNetwork=now;string name=adapters.Length==0?"Aucune interface active":adapters.Length==1?adapters[0].Name:adapters.Length+" interfaces physiques";return valid>0?(down,up,name):((double?)null,null,name);}
    static bool IsVirtual(NetworkInterface n)=>new[]{"virtual","hyper-v","vmware","vethernet","tap-","vpn","wireguard","loopback","tunnel"}.Any(x=>(n.Name+" "+n.Description).Contains(x,StringComparison.OrdinalIgnoreCase));
    internal static double Rate(long current,long previous,double seconds)=>seconds>0&&current>=previous?(current-previous)/seconds:0;
    public void Dispose(){tempChain.Dispose();disk.Dispose();nvidia.Dispose();if(hardwareOpen)computer.Close();}
    [StructLayout(LayoutKind.Sequential)]struct MemoryStatus {internal uint Length,Load;internal ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;}
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}

internal sealed class DiskCounters:IDisposable {
    IntPtr query,read,write,idle;
    internal DiskCounters(){if(PdhOpenQueryW(null,UIntPtr.Zero,out query)!=0)return;PdhAddEnglishCounterW(query,@"\PhysicalDisk(_Total)\Disk Read Bytes/sec",UIntPtr.Zero,out read);PdhAddEnglishCounterW(query,@"\PhysicalDisk(_Total)\Disk Write Bytes/sec",UIntPtr.Zero,out write);PdhAddEnglishCounterW(query,@"\PhysicalDisk(_Total)\% Idle Time",UIntPtr.Zero,out idle);PdhCollectQueryData(query);}
    internal (double? busy,double? read,double? write) Read(){if(query==IntPtr.Zero||PdhCollectQueryData(query)!=0)return(null,null,null);double? idleValue=Value(idle);return(idleValue.HasValue?PcTelemetrySource.Percent(100-idleValue):null,Value(read),Value(write));}
    static double? Value(IntPtr counter){if(counter==IntPtr.Zero||PdhGetFormattedCounterValue(counter,0x200,out _,out var value)!=0||value.Status>1||!double.IsFinite(value.Value))return null;return Math.Max(0,value.Value);}
    public void Dispose(){if(query!=IntPtr.Zero){PdhCloseQuery(query);query=IntPtr.Zero;}}
    [StructLayout(LayoutKind.Explicit,Size=16)]struct FormattedValue {[FieldOffset(0)]internal uint Status;[FieldOffset(8)]internal double Value;}
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]static extern uint PdhOpenQueryW(string? source,UIntPtr data,out IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]static extern uint PdhAddEnglishCounterW(IntPtr query,string path,UIntPtr data,out IntPtr counter);
    [DllImport("pdh.dll")]static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")]static extern uint PdhGetFormattedCounterValue(IntPtr counter,uint format,out uint type,out FormattedValue value);
    [DllImport("pdh.dll")]static extern uint PdhCloseQuery(IntPtr query);
}
