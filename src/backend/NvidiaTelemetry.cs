// language: C# 12, file: NvidiaTelemetry.cs, target: Windows 11 / NVIDIA driver NVML
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PanneauxGlass;
internal sealed class NvidiaTelemetry:IDisposable {
    readonly List<(IntPtr handle,string name)> devices=new();bool initialized;
    internal bool Available=>devices.Count>0;
    internal NvidiaTelemetry(){try{if(!File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"nvml.dll"))||nvmlInit_v2()!=0)return;initialized=true;if(nvmlDeviceGetCount_v2(out uint count)!=0)return;for(uint i=0;i<Math.Min(count,16);i++)if(nvmlDeviceGetHandleByIndex_v2(i,out var handle)==0){var name=new StringBuilder(128);nvmlDeviceGetName(handle,name,128);devices.Add((handle,name.ToString()));}}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
    internal GpuReading[] Read(){var result=new List<GpuReading>();foreach(var device in devices){bool util=nvmlDeviceGetUtilizationRates(device.handle,out var load)==0,temp=nvmlDeviceGetTemperature(device.handle,0,out uint temperature)==0,mem=nvmlDeviceGetMemoryInfo(device.handle,out var memory)==0;result.Add(new("nvidia-"+result.Count,device.name,util?PcTelemetrySource.Percent(load.Gpu):null,temp&&temperature<150?temperature:null,mem?memory.Used/1048576.0:null,mem?memory.Total/1048576.0:null));}return result.ToArray();}
    public void Dispose(){if(initialized){nvmlShutdown();initialized=false;}}
    [StructLayout(LayoutKind.Sequential)]struct Utilization {internal uint Gpu,Memory;}
    [StructLayout(LayoutKind.Sequential)]struct Memory {internal ulong Total,Free,Used;}
    [DllImport("nvml.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int nvmlInit_v2();
    [DllImport("nvml.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int nvmlShutdown();
    [DllImport("nvml.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int nvmlDeviceGetCount_v2(out uint count);
    [DllImport("nvml.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int nvmlDeviceGetHandleByIndex_v2(uint index,out IntPtr device);
    [DllImport("nvml.dll",CharSet=CharSet.Ansi),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int nvmlDeviceGetName(IntPtr device,StringBuilder name,uint length);
    [DllImport("nvml.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int nvmlDeviceGetUtilizationRates(IntPtr device,out Utilization utilization);
    [DllImport("nvml.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int nvmlDeviceGetTemperature(IntPtr device,uint sensor,out uint temperature);
    [DllImport("nvml.dll"),DefaultDllImportSearchPaths(DllImportSearchPath.System32)]static extern int nvmlDeviceGetMemoryInfo(IntPtr device,out Memory memory);
}
